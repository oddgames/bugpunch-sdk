// =============================================================================
// Defers libwebrtc startup until a stream actually starts.
//
// com.unity.webrtc initialises itself in every player at SubsystemRegistration
// (ContextManager.Init → WebRTC.InitializeInternal): it loads the native
// library, creates the libwebrtc context with its worker / signalling / network
// threads, and on iOS registers a rendering plugin. That runs for every player
// of every game that ships Bugpunch, although only a tester watching a live
// stream ever uses it — and Trucks Off Road 1.95.13041 crashed inside the
// `webrtc` image on store installs with no stream session (an uncaught C++
// exception on a libwebrtc thread; a SIGSEGV in its render-thread callback).
//
// The package has no switch for this, so the switch is made here, at build
// time: the [RuntimeInitializeOnLoadMethod] attribute is taken off
// ContextManager.Init in the compiled Unity.WebRTC assembly, and [Preserve] is
// put on in its place so managed stripping keeps the now-unreferenced method.
// WebRtcDeferredInit (package/WebRTC) calls it when the streamer is created.
//
// PLAYER BUILDS ONLY: the editor variant of ContextManager initialises from a
// static constructor and is left alone. A build whose Unity.WebRTC does not
// have the expected shape is left unwoven with a warning — it then starts
// libwebrtc at boot as before, and WebRtcDeferredInit sees the context already
// exists. Opt out with the BUGPUNCH_WEBRTC_EAGER_INIT scripting define.
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
    public sealed class WebRtcDeferredInitILPostProcessor : ILPostProcessor
    {
        const string TargetAssembly = "Unity.WebRTC";
        const string ManagerType = "Unity.WebRTC.ContextManager";
        const string InitMethod = "Init";
        const string RuntimeInitAttribute = "UnityEngine.RuntimeInitializeOnLoadMethodAttribute";

        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            if (compiledAssembly.Name != TargetAssembly) return false;
            foreach (var d in compiledAssembly.Defines ?? Array.Empty<string>())
                if (d == "UNITY_EDITOR" || d == "BUGPUNCH_WEBRTC_EAGER_INIT") return false;
            return true;
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var diagnostics = new List<DiagnosticMessage>();
            var unwoven = new ILPostProcessResult(compiledAssembly.InMemoryAssembly, diagnostics);
            try
            {
                var resolver = new MethodProfilerAssemblyResolver(compiledAssembly);
                var pdbData = compiledAssembly.InMemoryAssembly.PdbData;
                bool hasPdb = pdbData != null && pdbData.Length > 0;
                var assembly = AssemblyDefinition.ReadAssembly(
                    new MemoryStream(compiledAssembly.InMemoryAssembly.PeData),
                    new ReaderParameters
                    {
                        AssemblyResolver = resolver,
                        SymbolStream = hasPdb ? new MemoryStream(pdbData) : null,
                        SymbolReaderProvider = hasPdb ? new PortablePdbReaderProvider() : null,
                        ReadingMode = ReadingMode.Immediate,
                        ReadSymbols = hasPdb,
                        InMemory = true,
                    });
                resolver.AddAssemblyDefinitionBeingOperatedOn(assembly);

                var why = Weave(assembly.MainModule);
                if (why != null)
                {
                    diagnostics.Add(new DiagnosticMessage
                    {
                        DiagnosticType = DiagnosticType.Warning,
                        MessageData = $"[Bugpunch] WebRTC startup not deferred ({why}); libwebrtc will start at boot in this build.",
                    });
                    return unwoven;
                }

                var pe = new MemoryStream();
                var pdb = new MemoryStream();
                var writerParams = new WriterParameters();
                if (hasPdb)
                {
                    writerParams.SymbolWriterProvider = new PortablePdbWriterProvider();
                    writerParams.SymbolStream = pdb;
                    writerParams.WriteSymbols = true;
                }
                assembly.Write(pe, writerParams);
                return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), diagnostics);
            }
            catch (Exception ex)
            {
                // Never break the build over this: an unwoven assembly behaves
                // exactly as the package ships.
                diagnostics.Add(new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Warning,
                    MessageData = $"[Bugpunch] WebRTC startup not deferred ({ex.Message}); libwebrtc will start at boot in this build.",
                });
                return unwoven;
            }
        }

        /// <summary>
        /// Rewrites the module in place. Returns null on success, else why
        /// nothing was changed (the module is then untouched).
        /// </summary>
        internal static string Weave(ModuleDefinition module)
        {
            var manager = module.GetType(ManagerType);
            if (manager == null) return $"{ManagerType} not found";
            var init = manager.Methods.FirstOrDefault(m => m.Name == InitMethod && m.IsStatic && !m.HasParameters);
            if (init == null) return $"{ManagerType}.{InitMethod}() not found";
            var bootAttribute = init.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == RuntimeInitAttribute);
            if (bootAttribute == null) return $"{InitMethod} carries no [RuntimeInitializeOnLoadMethod]";

            // Resolved before anything is removed: without [Preserve] the
            // stripper would delete Init and streaming could never start.
            AssemblyDefinition core = null;
            try { core = module.AssemblyResolver.Resolve(new AssemblyNameReference("UnityEngine.CoreModule", new Version(0, 0, 0, 0))); }
            catch { }
            var preserveCtor = core?.MainModule.GetType("UnityEngine.Scripting", "PreserveAttribute")?
                .Methods.FirstOrDefault(m => m.IsConstructor && !m.IsStatic && !m.HasParameters);
            if (preserveCtor == null) return "UnityEngine.Scripting.PreserveAttribute not resolvable";
            var preserve = module.ImportReference(preserveCtor);

            init.CustomAttributes.Remove(bootAttribute);
            init.CustomAttributes.Add(new CustomAttribute(preserve));
            manager.CustomAttributes.Add(new CustomAttribute(preserve));
            return null;
        }
    }
}
