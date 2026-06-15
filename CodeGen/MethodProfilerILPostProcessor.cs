// =============================================================================
// Method-profiler IL weaver — the build-time half of the release pseudo
// profiler (runtime half: BugpunchMethodProfiler in the runtime DLL).
//
// PLAYER BUILDS ONLY. WillProcess refuses any compilation that defines
// UNITY_EDITOR, so editor iteration never pays the weave cost and a weaver
// bug can never break editor compiles. Opt out per project with the
// BUGPUNCH_NO_METHOD_PROFILER scripting define. For debugging the profiler
// itself, the BUGPUNCH_WEAVE_IN_EDITOR scripting define temporarily allows
// editor compilations to be woven (remove it when done).
//
// What gets rewritten (game assemblies only — never Unity's, never ours):
//
//   Profiler.BeginSample("literal")   ldstr → ldsfld <site-id-field>
//                                     call BeginSample → call Enter(int)
//                                     (weave-time id — no runtime string cost)
//   Profiler.BeginSample(expr)        call → call EnterDynamic(string)
//   Profiler.BeginSample(expr, ctx)   call → call EnterDynamic(string, Object)
//   Profiler.EndSample()              call → call Exit()
//   marker.Begin() / marker.End()     ldsflda+call → ldsfld <id>+Enter / nop+Exit
//                                     (only when every Begin/End in the method
//                                     pattern-matches — partial = skip method)
//   using (marker.Auto())             Enter(id) inserted after the AutoScope
//                                     stloc, Exit() inserted after Dispose
//                                     (counts must match or the method is
//                                     skipped). Auto() survives release IL —
//                                     it returns a struct so it can't be
//                                     [Conditional] — which makes it the one
//                                     marker form recoverable without defines.
//   MonoBehaviour Update/FixedUpdate/ whole-method try/finally wrap with an
//   LateUpdate                        "update" site — per-script timing with
//                                     no marks required from the game.
//
// NOTE on release builds: Begin/End/BeginSample call sites only exist in the
// compiled IL if ENABLE_PROFILER was defined for the GAME's compilation
// (Unity defines it for development builds; add it to Scripting Define
// Symbols to keep marks in release). Auto() scopes and the MonoBehaviour
// wraps need nothing — they're always recoverable.
//
// Each woven assembly gets a generated __BugpunchProfiledSites class: one
// internal static int field per site plus a cctor that registers the site
// descriptor table with BugpunchMethodProfiler and stores base+index ids
// into the fields. First profiled call triggers the cctor — no startup hook.
//
// Each descriptor also carries a weave-time SOURCE location pulled from the
// PDB ("Assets/Rel/Path.cs:line"), so a profiler row reads "Ticker.Update —
// PosterAssignment.cs:758" and the dashboard can deep-link it to the source
// bundle the SDK uploaded for the build. Normalised to the same
// "Assets/..."/"Packages/..." form the uploader stores. Costs nothing at
// runtime — it's a build-time string baked into the descriptor table.
//
// PLACEMENT: this file ships as SOURCE in package/CodeGen/ under an asmdef
// named ODDGames.Bugpunch.CodeGen. Unity only discovers ILPostProcessor
// implementations in assemblies whose name ends with ".CodeGen" (same as
// Netcode / Mirror) — hosting this class in ODDGames.Bugpunch.Editor.dll
// silently never runs. Don't move it back.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace ODDGames.BugpunchSdk.CodeGen
{
    public sealed class MethodProfilerILPostProcessor : ILPostProcessor
    {
        const string ProfilerType = "UnityEngine.Profiling.Profiler";
        const string MarkerType = "Unity.Profiling.ProfilerMarker";
        const string AutoScopeType = "Unity.Profiling.ProfilerMarker/AutoScope";
        const string CollectorNamespace = "ODDGames.BugpunchSdk";
        const string CollectorType = "BugpunchMethodProfiler";
        const char Sep = '\u001f';
        const string WeaveLogPath = "Library/BugpunchMethodProfilerWeave.log";

        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            // Player builds only — editor compiles never pay the weave cost.
            // BUGPUNCH_WEAVE_IN_EDITOR is a debugging escape hatch: add it to
            // Scripting Define Symbols to weave editor compilations too (so
            // the pseudo profiler can be exercised in play mode), remove it
            // when done.
            bool isEditorCompile = false, allowEditor = false;
            foreach (var d in compiledAssembly.Defines ?? Array.Empty<string>())
            {
                if (d == "UNITY_EDITOR") isEditorCompile = true;
                else if (d == "BUGPUNCH_WEAVE_IN_EDITOR") allowEditor = true;
                else if (d == "BUGPUNCH_NO_METHOD_PROFILER") return false; // project opt-out
            }
            if (isEditorCompile && !allowEditor) return false;
            var name = compiledAssembly.Name;
            if (name.StartsWith("ODDGames.")) return false;
            if (name.StartsWith("Unity.") || name.StartsWith("UnityEngine") || name.StartsWith("UnityEditor")) return false;
            if (name == "mscorlib" || name == "netstandard" || name.StartsWith("System")) return false;
            foreach (var r in compiledAssembly.References)
            {
                var f = Path.GetFileNameWithoutExtension(r);
                if (f == "UnityEngine.CoreModule" || f == "UnityEngine") return true;
            }
            return false;
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var diagnostics = new List<DiagnosticMessage>();
            AssemblyDefinition assembly;
            try
            {
                assembly = LoadAssembly(compiledAssembly);
            }
            catch (Exception ex)
            {
                diagnostics.Add(Warn($"[Bugpunch method profiler] Failed to load {compiledAssembly.Name}: {ex.Message}"));
                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, diagnostics);
            }

            WeaveSession session;
            try
            {
                session = new WeaveSession(assembly.MainModule, compiledAssembly);
                foreach (var type in assembly.MainModule.Types.ToList())
                    session.WeaveType(type);
                session.EmitSiteHolder();
            }
            catch (Exception ex)
            {
                // A weave failure must never break the build — ship the
                // assembly unmodified.
                diagnostics.Add(Warn($"[Bugpunch method profiler] Weave error in {compiledAssembly.Name}: {ex.Message} — assembly left unwoven"));
                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, diagnostics);
            }

            if (session.SiteCount == 0)
                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, diagnostics);

            AppendWeaveLog(compiledAssembly.Name, session);

            var pe = new MemoryStream();
            var pdb = new MemoryStream();
            var writerParams = new WriterParameters();
            if (compiledAssembly.InMemoryAssembly.PdbData != null && compiledAssembly.InMemoryAssembly.PdbData.Length > 0)
            {
                writerParams.SymbolWriterProvider = new PortablePdbWriterProvider();
                writerParams.SymbolStream = pdb;
                writerParams.WriteSymbols = true;
            }
            assembly.Write(pe, writerParams);
            return new ILPostProcessResult(
                new InMemoryAssembly(pe.ToArray(), pdb.ToArray()),
                diagnostics);
        }

        static DiagnosticMessage Warn(string msg) => new DiagnosticMessage
        {
            DiagnosticType = DiagnosticType.Warning,
            MessageData = msg,
        };

        static AssemblyDefinition LoadAssembly(ICompiledAssembly compiledAssembly)
        {
            var resolver = new MethodProfilerAssemblyResolver(compiledAssembly);
            bool hasPdb = compiledAssembly.InMemoryAssembly.PdbData != null
                && compiledAssembly.InMemoryAssembly.PdbData.Length > 0;
            var readerParams = new ReaderParameters
            {
                AssemblyResolver = resolver,
                SymbolStream = hasPdb ? new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData) : null,
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                ReadingMode = ReadingMode.Immediate,
                ReadSymbols = hasPdb,
                InMemory = true,
            };
            var asm = AssemblyDefinition.ReadAssembly(
                new MemoryStream(compiledAssembly.InMemoryAssembly.PeData),
                readerParams);
            resolver.AddAssemblyDefinitionBeingOperatedOn(asm);
            return asm;
        }

        static readonly object s_logLock = new();
        static void AppendWeaveLog(string asmName, WeaveSession session)
        {
            try
            {
                lock (s_logLock)
                {
                    File.AppendAllText(WeaveLogPath,
                        $"{DateTime.Now:HH:mm:ss} {asmName}: {session.SiteCount} sites " +
                        $"(samples={session.SampleSites} markers={session.MarkerSites} updates={session.UpdateSites})\n");
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[Bugpunch method profiler] weave log append failed: {e.Message}");
            }
        }

        // ── Per-module weave state ─────────────────────────────────────────

        sealed class WeaveSession
        {
            readonly ModuleDefinition _module;
            readonly MethodReference _enter, _enterDynamic, _exit, _registerSites;
            MethodReference _enterDynamicCtx;   // built lazily off the original call's Object param
            readonly TypeReference _collector;

            TypeDefinition _holder;
            readonly List<string> _descriptors = new();
            readonly List<FieldDefinition> _fields = new();
            readonly Dictionary<string, bool> _monoBehaviourCache = new();
            readonly Dictionary<string, string> _markerNameCache = new();

            public int SiteCount => _descriptors.Count;
            public int SampleSites, MarkerSites, UpdateSites;

            public WeaveSession(ModuleDefinition module, ICompiledAssembly compiled)
            {
                _module = module;
                var bpRef = GetOrAddBugpunchRef(module, compiled);
                _collector = new TypeReference(CollectorNamespace, CollectorType, module, bpRef);
                var ts = module.TypeSystem;
                _enter = StaticMethod("Enter", ts.Void, ts.Int32);
                _enterDynamic = StaticMethod("EnterDynamic", ts.Void, ts.String);
                _exit = StaticMethod("Exit", ts.Void);
                _registerSites = StaticMethod("RegisterSites", ts.Int32, new ArrayType(ts.String));
            }

            MethodReference StaticMethod(string name, TypeReference ret, params TypeReference[] ps)
            {
                var m = new MethodReference(name, ret, _collector) { HasThis = false };
                foreach (var p in ps) m.Parameters.Add(new ParameterDefinition(p));
                return m;
            }

            static AssemblyNameReference GetOrAddBugpunchRef(ModuleDefinition module, ICompiledAssembly compiled)
            {
                foreach (var ar in module.AssemblyReferences)
                    if (ar.Name == "ODDGames.Bugpunch") return ar;
                // Assembly doesn't reference the SDK (asmdef without the ref,
                // but containing MonoBehaviours we still want timed). Add a
                // name-only reference — the player resolves by simple name at
                // runtime; the DLL ships in every build that has the package.
                var version = new Version(0, 0, 0, 0);
                var dllPath = compiled.References.FirstOrDefault(r =>
                    string.Equals(Path.GetFileName(r), "ODDGames.Bugpunch.dll", StringComparison.OrdinalIgnoreCase));
                if (dllPath != null)
                {
                    try { using var a = AssemblyDefinition.ReadAssembly(dllPath); version = a.Name.Version; }
                    catch { version = new Version(0, 0, 0, 0); }
                }
                var nr = new AssemblyNameReference("ODDGames.Bugpunch", version);
                module.AssemblyReferences.Add(nr);
                return nr;
            }

            // ── Site table ────────────────────────────────────────────────

            FieldDefinition AddSite(string kind, string name, string method, string source)
            {
                if (_holder == null)
                {
                    _holder = new TypeDefinition(
                        "ODDGames.BugpunchSdk.Generated", "__BugpunchProfiledSites",
                        TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed
                        | TypeAttributes.Class | TypeAttributes.AnsiClass,
                        _module.TypeSystem.Object);
                    _module.Types.Add(_holder);
                }
                var field = new FieldDefinition(
                    "s" + _fields.Count,
                    FieldAttributes.Assembly | FieldAttributes.Static,
                    _module.TypeSystem.Int32);
                _holder.Fields.Add(field);
                _fields.Add(field);
                _descriptors.Add(kind + Sep + name + Sep + method + Sep + source);
                return field;
            }

            // ── Source location (weave-time, from the PDB) ─────────────────
            // The site descriptor's 4th field is "Assets/Rel/Path.cs:line",
            // pulled from Cecil sequence points. Zero runtime cost — it's a
            // build-time string — and the path is normalised to the same
            // "Assets/..."/"Packages/..." form the SDK's source-bundle
            // uploader stores, so the dashboard can deep-link the profiler row
            // straight to the uploaded source (server matches exact-then-suffix).

            const int HiddenLine = 0xFEEFEE;   // PDB "hidden" sequence-point sentinel
            static readonly string[] s_pathRoots = { "/Assets/", "/Packages/" };

            /// <summary>First real (non-hidden) sequence point of a method —
            /// used for whole-method sites (the Update wrap).</summary>
            static string SourceForMethod(MethodDefinition m)
            {
                var di = m.DebugInformation;
                if (di == null || !di.HasSequencePoints) return "";
                foreach (var sp in di.SequencePoints)
                    if (sp != null && sp.StartLine > 0 && sp.StartLine != HiddenLine)
                        return Format(sp);
                return "";
            }

            /// <summary>Sequence point at or before a specific instruction —
            /// used for in-method sites (BeginSample / marker calls) so the
            /// line points at the mark, not the method header. Falls back to
            /// the method's first line.</summary>
            static string SourceForInstruction(MethodDefinition m, Instruction ins)
            {
                var di = m.DebugInformation;
                if (di == null) return "";
                for (var c = ins; c != null; c = c.Previous)
                {
                    var sp = di.GetSequencePoint(c);
                    if (sp != null && sp.StartLine > 0 && sp.StartLine != HiddenLine)
                        return Format(sp);
                }
                return SourceForMethod(m);
            }

            static string Format(SequencePoint sp)
            {
                var rel = NormalizeSourcePath(sp.Document?.Url);
                return rel.Length == 0 ? "" : rel + ":" + sp.StartLine;
            }

            /// <summary>Build-machine absolute path → project-relative
            /// "Assets/..."/"Packages/..." (the form the source bundle stores).
            /// Basename as a last resort so a link target always exists.</summary>
            static string NormalizeSourcePath(string url)
            {
                if (string.IsNullOrEmpty(url)) return "";
                var p = url.Replace('\\', '/');
                foreach (var root in s_pathRoots)
                {
                    int idx = p.IndexOf(root, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0) return p.Substring(idx + 1);   // drop leading '/'
                }
                if (p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                    || p.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                    return p;
                int slash = p.LastIndexOf('/');
                return slash >= 0 ? p.Substring(slash + 1) : p;
            }

            /// <summary>Generated cctor: register descriptors, store base+i into each id field.</summary>
            public void EmitSiteHolder()
            {
                if (_holder == null) return;
                var ts = _module.TypeSystem;
                var cctor = new MethodDefinition(".cctor",
                    MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                    ts.Void);
                var body = cctor.Body;
                body.InitLocals = true;
                body.Variables.Add(new VariableDefinition(ts.Int32));
                var il = body.GetILProcessor();
                il.Emit(OpCodes.Ldc_I4, _descriptors.Count);
                il.Emit(OpCodes.Newarr, ts.String);
                for (int i = 0; i < _descriptors.Count; i++)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldstr, _descriptors[i]);
                    il.Emit(OpCodes.Stelem_Ref);
                }
                il.Emit(OpCodes.Call, _registerSites);
                il.Emit(OpCodes.Stloc_0);
                for (int i = 0; i < _fields.Count; i++)
                {
                    il.Emit(OpCodes.Ldloc_0);
                    if (i > 0)
                    {
                        il.Emit(OpCodes.Ldc_I4, i);
                        il.Emit(OpCodes.Add);
                    }
                    il.Emit(OpCodes.Stsfld, _fields[i]);
                }
                il.Emit(OpCodes.Ret);
                _holder.Methods.Add(cctor);
            }

            // ── Type / method traversal ───────────────────────────────────

            public void WeaveType(TypeDefinition type)
            {
                foreach (var nested in type.NestedTypes.ToList())
                    WeaveType(nested);
                if (HasBurst(type)) return;

                bool isMb = IsMonoBehaviour(type);
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody || method.Body.Instructions.Count == 0) continue;
                    if (HasBurst(method)) continue;
                    bool modified = WeaveCalls(method);
                    if (isMb && IsWrappableUpdate(method))
                        modified |= WrapUpdateMethod(method);
                    if (modified)
                        WidenShortBranches(method.Body);
                }
            }

            static bool IsWrappableUpdate(MethodDefinition m) =>
                (m.Name == "Update" || m.Name == "FixedUpdate" || m.Name == "LateUpdate")
                && !m.IsStatic && !m.IsAbstract && m.Parameters.Count == 0
                && m.ReturnType.FullName == "System.Void";

            static bool HasBurst(ICustomAttributeProvider p) =>
                p.HasCustomAttributes
                && p.CustomAttributes.Any(a => a.AttributeType.Name == "BurstCompileAttribute");

            bool IsMonoBehaviour(TypeDefinition type)
            {
                if (_monoBehaviourCache.TryGetValue(type.FullName, out var cached)) return cached;
                bool result = false;
                try
                {
                    var bt = type.BaseType;
                    int guard = 0;
                    while (bt != null && guard++ < 32)
                    {
                        if (bt.FullName == "UnityEngine.MonoBehaviour") { result = true; break; }
                        if (bt.FullName == "System.Object") break;
                        bt = bt.Resolve()?.BaseType;
                    }
                }
                catch { result = false; }
                _monoBehaviourCache[type.FullName] = result;
                return result;
            }

            // ── Profiler-call redirection ─────────────────────────────────

            bool WeaveCalls(MethodDefinition method)
            {
                var body = method.Body;
                var il = body.GetILProcessor();
                var targets = CollectTargets(body);
                string methodLabel = method.DeclaringType.FullName + "." + method.Name;
                bool modified = false;

                // Marker + Auto sites are collected first and only applied
                // when the method's pattern coverage is total — a partially
                // woven marker pair would corrupt the depth stack.
                int markerBeginTotal = 0, markerEndTotal = 0, autoTotal = 0;
                var markerBegins = new List<(Instruction[] seq, Instruction call, FieldReference field)>();
                var markerEnds = new List<(Instruction[] seq, Instruction call)>();
                var autoSites = new List<(Instruction stloc, FieldReference field)>();
                var disposeCalls = new List<Instruction>();

                foreach (var ins in body.Instructions.ToList())
                {
                    if (ins.OpCode.Code != Code.Call && ins.OpCode.Code != Code.Callvirt) continue;
                    if (!(ins.Operand is MethodReference mr)) continue;
                    var declaring = mr.DeclaringType?.FullName;

                    if (declaring == ProfilerType)
                    {
                        if (mr.Name == "BeginSample" && mr.Parameters.Count == 1)
                        {
                            var prev = ins.Previous;
                            if (prev != null && prev.OpCode.Code == Code.Ldstr
                                && !targets.Contains(prev) && !targets.Contains(ins))
                            {
                                // Literal sample name → weave-time integer id.
                                var f = AddSite("sample", (string)prev.Operand, methodLabel, SourceForInstruction(method, ins));
                                SampleSites++;
                                prev.OpCode = OpCodes.Ldsfld;
                                prev.Operand = f;
                                ins.OpCode = OpCodes.Call;
                                ins.Operand = _enter;
                            }
                            else
                            {
                                // Computed name → dictionary path at runtime.
                                SampleSites++;
                                ins.OpCode = OpCodes.Call;
                                ins.Operand = _enterDynamic;
                            }
                            modified = true;
                        }
                        else if (mr.Name == "BeginSample" && mr.Parameters.Count == 2)
                        {
                            if (_enterDynamicCtx == null)
                            {
                                var ctxType = _module.ImportReference(mr.Parameters[1].ParameterType);
                                _enterDynamicCtx = StaticMethod("EnterDynamic",
                                    _module.TypeSystem.Void, _module.TypeSystem.String, ctxType);
                            }
                            SampleSites++;
                            ins.OpCode = OpCodes.Call;
                            ins.Operand = _enterDynamicCtx;
                            modified = true;
                        }
                        else if (mr.Name == "EndSample" && mr.Parameters.Count == 0)
                        {
                            ins.OpCode = OpCodes.Call;
                            ins.Operand = _exit;
                            modified = true;
                        }
                    }
                    else if (declaring == MarkerType)
                    {
                        if (mr.Name == "Begin" && mr.Parameters.Count == 0)
                        {
                            markerBeginTotal++;
                            var seq = MatchMarkerLoad(ins, targets, out var fr);
                            if (seq != null) markerBegins.Add((seq, ins, fr));
                        }
                        else if (mr.Name == "End" && mr.Parameters.Count == 0)
                        {
                            markerEndTotal++;
                            var seq = MatchMarkerLoad(ins, targets, out _);
                            if (seq != null) markerEnds.Add((seq, ins));
                        }
                        else if (mr.Name == "Auto" && mr.Parameters.Count == 0)
                        {
                            autoTotal++;
                            var seq = MatchMarkerLoad(ins, targets, out var fr2);
                            var next = ins.Next;
                            if (seq != null && next != null && IsStloc(next.OpCode.Code))
                                autoSites.Add((next, fr2));
                        }
                    }
                    else if (mr.Name == "Dispose" && IsAutoScopeDispose(ins, mr))
                    {
                        disposeCalls.Add(ins);
                    }
                }

                // Begin/End: weave only with total coverage and balanced counts.
                if (markerBeginTotal > 0 && markerBeginTotal == markerBegins.Count
                    && markerEndTotal == markerEnds.Count && markerBeginTotal == markerEndTotal)
                {
                    foreach (var (seq, call, field) in markerBegins)
                    {
                        var f = AddSite("marker", ResolveMarkerName(field), methodLabel, SourceForInstruction(method, call));
                        MarkerSites++;
                        // NOP everything but the final load slot, which becomes
                        // the int site id — net stack effect identical.
                        for (int i = 0; i < seq.Length - 1; i++)
                        {
                            seq[i].OpCode = OpCodes.Nop;
                            seq[i].Operand = null;
                        }
                        var last = seq[seq.Length - 1];
                        last.OpCode = OpCodes.Ldsfld;
                        last.Operand = f;
                        call.OpCode = OpCodes.Call;
                        call.Operand = _enter;
                    }
                    foreach (var (seq, call) in markerEnds)
                    {
                        foreach (var s in seq)
                        {
                            s.OpCode = OpCodes.Nop;
                            s.Operand = null;
                        }
                        call.OpCode = OpCodes.Call;
                        call.Operand = _exit;
                    }
                    modified = true;
                }

                // Auto: insert Enter after the AutoScope stloc (outside the
                // using's try — EH boundaries keep pointing at the original
                // first try instruction) and Exit right after each Dispose
                // (inside the finally). Counts must match exactly.
                if (autoTotal > 0 && autoTotal == autoSites.Count && autoTotal == disposeCalls.Count)
                {
                    foreach (var (stloc, field) in autoSites)
                    {
                        var f = AddSite("marker", ResolveMarkerName(field), methodLabel, SourceForInstruction(method, stloc));
                        MarkerSites++;
                        // InsertAfter in reverse order → [stloc][ldsfld][call Enter]
                        il.InsertAfter(stloc, il.Create(OpCodes.Call, _enter));
                        il.InsertAfter(stloc, il.Create(OpCodes.Ldsfld, f));
                    }
                    foreach (var d in disposeCalls)
                        il.InsertAfter(d, il.Create(OpCodes.Call, _exit));
                    modified = true;
                }

                return modified;
            }

            static bool IsStloc(Code c) =>
                c == Code.Stloc || c == Code.Stloc_S
                || c == Code.Stloc_0 || c == Code.Stloc_1 || c == Code.Stloc_2 || c == Code.Stloc_3;

            static int StlocIndex(Instruction ins) => ins.OpCode.Code switch
            {
                Code.Stloc_0 => 0,
                Code.Stloc_1 => 1,
                Code.Stloc_2 => 2,
                Code.Stloc_3 => 3,
                Code.Stloc or Code.Stloc_S => (ins.Operand as VariableDefinition)?.Index ?? -1,
                _ => -1,
            };

            static int LdlocaIndex(Instruction ins) =>
                ins.OpCode.Code == Code.Ldloca || ins.OpCode.Code == Code.Ldloca_S
                    ? (ins.Operand as VariableDefinition)?.Index ?? -1
                    : -1;

            /// <summary>
            /// Matches the marker-receiver load feeding a ProfilerMarker call
            /// and returns the load instruction sequence (or null). Shapes:
            ///   direct:        ldsflda F                   (mutable static field)
            ///   readonly copy: ldsfld F; stloc V; ldloca V (static readonly —
            ///                  Roslyn's defensive copy)
            /// Either way every instruction in the sequence is NOP-safe: the
            /// pushes and pops cancel out within the sequence.
            /// </summary>
            static Instruction[] MatchMarkerLoad(Instruction call, HashSet<Instruction> targets, out FieldReference field)
            {
                field = null;
                if (targets.Contains(call)) return null;
                var p1 = call.Previous;
                if (p1 == null) return null;
                if (p1.OpCode.Code == Code.Ldsflda && p1.Operand is FieldReference f1 && !targets.Contains(p1))
                {
                    field = f1;
                    return new[] { p1 };
                }
                int slot = LdlocaIndex(p1);
                if (slot >= 0 && !targets.Contains(p1))
                {
                    var p2 = p1.Previous;
                    var p3 = p2?.Previous;
                    if (p2 != null && p3 != null && StlocIndex(p2) == slot
                        && p3.OpCode.Code == Code.Ldsfld && p3.Operand is FieldReference f3
                        && !targets.Contains(p2))
                    {
                        field = f3;
                        return new[] { p3, p2, p1 };
                    }
                }
                return null;
            }

            /// <summary>
            /// AutoScope.Dispose appears either as a direct struct call or as
            /// `constrained. AutoScope` + `callvirt IDisposable::Dispose`
            /// (how Roslyn lowers `using` over the struct).
            /// </summary>
            static bool IsAutoScopeDispose(Instruction ins, MethodReference mr)
            {
                var declaring = mr.DeclaringType?.FullName;
                if (declaring == AutoScopeType) return true;
                return declaring == "System.IDisposable"
                    && ins.Previous?.OpCode.Code == Code.Constrained
                    && ins.Previous.Operand is TypeReference tr
                    && tr.FullName == AutoScopeType;
            }

            /// <summary>
            /// Marker name = the ldstr fed to `new ProfilerMarker("…")` before
            /// the stsfld in the declaring type's cctor. Falls back to the
            /// field name when the init shape doesn't match.
            /// </summary>
            string ResolveMarkerName(FieldReference field)
            {
                var key = field.DeclaringType.FullName + "::" + field.Name;
                if (_markerNameCache.TryGetValue(key, out var cached)) return cached;
                string name = field.Name;
                try
                {
                    var fd = field.Resolve();
                    var cctor = fd?.DeclaringType?.Methods.FirstOrDefault(m => m.Name == ".cctor");
                    if (cctor?.HasBody == true)
                    {
                        string lastLdstr = null;
                        foreach (var ins in cctor.Body.Instructions)
                        {
                            if (ins.OpCode.Code == Code.Ldstr) lastLdstr = ins.Operand as string;
                            else if (ins.OpCode.Code == Code.Stsfld
                                && ins.Operand is FieldReference fr && fr.Name == field.Name
                                && fr.DeclaringType.FullName == field.DeclaringType.FullName)
                            {
                                if (lastLdstr != null) name = lastLdstr;
                                break;
                            }
                        }
                    }
                }
                catch { name = field.Name; }
                _markerNameCache[key] = name;
                return name;
            }

            // ── MonoBehaviour Update wrap ─────────────────────────────────

            bool WrapUpdateMethod(MethodDefinition method)
            {
                var body = method.Body;
                var il = body.GetILProcessor();
                var f = AddSite("update", method.Name, method.DeclaringType.FullName, SourceForMethod(method));
                UpdateSites++;

                var first = body.Instructions[0];
                // Original rets become leave → finalRet. Collected before we
                // append our trailer (whose ret must not be rewritten).
                var rets = body.Instructions.Where(x => x.OpCode.Code == Code.Ret).ToList();

                il.InsertBefore(first, il.Create(OpCodes.Ldsfld, f));
                il.InsertBefore(first, il.Create(OpCodes.Call, _enter));

                var exitCall = il.Create(OpCodes.Call, _exit);
                var endFinally = il.Create(OpCodes.Endfinally);
                var finalRet = il.Create(OpCodes.Ret);
                il.Append(exitCall);
                il.Append(endFinally);
                il.Append(finalRet);

                foreach (var r in rets)
                {
                    r.OpCode = OpCodes.Leave;
                    r.Operand = finalRet;
                }

                // Appended last → outermost handler; any pre-existing EH nests
                // inside the new try range.
                body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
                {
                    TryStart = first,
                    TryEnd = exitCall,
                    HandlerStart = exitCall,
                    HandlerEnd = finalRet,
                });
                return true;
            }

            // ── IL hygiene ────────────────────────────────────────────────

            static HashSet<Instruction> CollectTargets(MethodBody body)
            {
                var set = new HashSet<Instruction>();
                foreach (var ins in body.Instructions)
                {
                    if (ins.Operand is Instruction t) set.Add(t);
                    else if (ins.Operand is Instruction[] arr)
                        foreach (var x in arr) set.Add(x);
                }
                foreach (var h in body.ExceptionHandlers)
                {
                    if (h.TryStart != null) set.Add(h.TryStart);
                    if (h.TryEnd != null) set.Add(h.TryEnd);
                    if (h.HandlerStart != null) set.Add(h.HandlerStart);
                    if (h.HandlerEnd != null) set.Add(h.HandlerEnd);
                    if (h.FilterStart != null) set.Add(h.FilterStart);
                }
                return set;
            }

            /// <summary>
            /// Insertions and ret→leave rewrites change instruction offsets;
            /// Cecil does not auto-widen short-form branches on write, so any
            /// modified body gets them widened wholesale.
            /// </summary>
            static void WidenShortBranches(MethodBody body)
            {
                foreach (var ins in body.Instructions)
                {
                    if (ins.OpCode.OperandType != OperandType.ShortInlineBrTarget) continue;
                    ins.OpCode = ins.OpCode.Code switch
                    {
                        Code.Br_S => OpCodes.Br,
                        Code.Brfalse_S => OpCodes.Brfalse,
                        Code.Brtrue_S => OpCodes.Brtrue,
                        Code.Beq_S => OpCodes.Beq,
                        Code.Bge_S => OpCodes.Bge,
                        Code.Bgt_S => OpCodes.Bgt,
                        Code.Ble_S => OpCodes.Ble,
                        Code.Blt_S => OpCodes.Blt,
                        Code.Bne_Un_S => OpCodes.Bne_Un,
                        Code.Bge_Un_S => OpCodes.Bge_Un,
                        Code.Bgt_Un_S => OpCodes.Bgt_Un,
                        Code.Blt_Un_S => OpCodes.Blt_Un,
                        Code.Ble_Un_S => OpCodes.Ble_Un,
                        Code.Leave_S => OpCodes.Leave,
                        _ => ins.OpCode,
                    };
                }
            }
        }
    }

    /// <summary>
    /// Resolves referenced assemblies from the paths the compile pipeline
    /// hands us — the ILPostProcessor sandbox has no general file-system
    /// resolution.
    /// </summary>
    internal sealed class MethodProfilerAssemblyResolver : IAssemblyResolver
    {
        readonly ICompiledAssembly _compiled;
        readonly Dictionary<string, AssemblyDefinition> _cache = new();

        public MethodProfilerAssemblyResolver(ICompiledAssembly compiled) { _compiled = compiled; }
        public void AddAssemblyDefinitionBeingOperatedOn(AssemblyDefinition asm) { _cache[asm.Name.Name] = asm; }

        public AssemblyDefinition Resolve(AssemblyNameReference name) => Resolve(name, new ReaderParameters());
        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            if (_cache.TryGetValue(name.Name, out var cached)) return cached;
            var path = _compiled.References.FirstOrDefault(r => Path.GetFileNameWithoutExtension(r) == name.Name);
            if (path == null) return null;
            try
            {
                parameters.AssemblyResolver = this;
                var asm = AssemblyDefinition.ReadAssembly(path, parameters);
                _cache[name.Name] = asm;
                return asm;
            }
            catch { return null; }
        }

        public void Dispose()
        {
            foreach (var v in _cache.Values) v?.Dispose();
            _cache.Clear();
        }
    }
}
