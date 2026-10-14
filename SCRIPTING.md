# ODDGames.Scripting

Embedded C#-subset scripting language shipped inside the Bugpunch SDK. It powers the Remote IDE's script panel (live, on-device evaluation from the dashboard) and any in-game tooling that needs to run code at runtime without `Reflection.Emit` or a JIT.

> **Distribution.** Ships as `ODDGames.Scripting.dll` in `Plugins/`, next to `ODDGames.Bugpunch.dll`. There is no separate UPM package or manifest entry — every Bugpunch consumer gets it with the SDK. Namespace: `ODDGames.Scripting`.

---

## Why it exists

- **IL2CPP-safe.** A bytecode VM — no `Reflection.Emit`, `DynamicMethod` or JIT. AOT platforms (iOS, consoles) are first-class.
- **Sandboxed.** A hard deny-list that no trust level can lift, plus three trust levels (`Untrusted`, `Admin`, `System`) and `[ScriptProtected]` for host members. The VM caps instructions, call depth and stack size.
- **Re-entrant.** VMs are pooled per thread; a nested host → script → host → script call gets its own VM and frames.
- **Self-contained.** Compiler, VM and the optional browser IDE live in one `netstandard2.1` assembly with no dependencies.

The SDK's main consumer is `RemoteIDE/Services/ScriptRunner.cs`: the dashboard's script panel sends source → the server forwards it over the device tunnel → the SDK compiles and runs it → the result comes back as JSON (`{ok, output}` or `{ok:false, errors}`).

---

## Pipeline

```
source ─► Lexer ─► Parser ─► SemanticAnalyzer (+ lowering) ─► Emitter ─► Chunk
                                                                           │
                                                                           ▼
                                                                          VM ─► result
```

| Stage | Files (under `scripting/Runtime/`) | Notes |
|---|---|---|
| Lexer | `Lexing/{Lexer,Token,TokenKind}.cs` | Hand-written single-pass tokenizer with line/column tracking. |
| Parser | `Parsing/Parser.cs` | Pratt parsing for expressions, recursive descent for statements and declarations. |
| AST | `Syntax/{Expressions,Statements,Declarations,TypeRefs}.cs` | Immutable nodes. |
| Semantic | `Binding/{SemanticAnalyzer,TypeResolver,ScriptType,Symbol}.cs` | Type inference, scoping, imports, deny-list enforcement. |
| Lowering | `Binding/{AsyncLowering,IteratorLowering,UsingLowering}.cs` | `async`/`await`, iterators and `using` declarations rewritten into simpler constructs. |
| Emitter | `Emit/Emitter.cs` | Walks the AST into a chunk: bytecode, constants, handler table, inline-cache slots. |
| Bytecode | `Bytecode/{Chunk,OpCode,InlineCache}.cs` | Compact opcodes with u8 / u16 / i32 operands. |
| VM | `VirtualMachine/VM.cs` | Switch dispatcher over a `StackSlot[]` operand stack. |

`StackSlot` is a tagged union: primitives (i32 / i64 / f32 / f64 / bool / char) live inline, and references (strings, script objects, delegates) go in an object field. Values are only boxed at the host boundary, not during arithmetic.

Compiled scripts are cached: an LRU keyed by a SHA-256 of the source plus the options that affect emission. Set `ScriptCompileOptions.CacheKey = "-"` to bypass it, or call `ScriptCompiler.ClearCache()`.

---

## Language surface

A broad C# subset. Supported today:

- **Types** — all numeric primitives, `decimal`, `bool`, `char`, `string`, `object`, nullable value types, arrays, generics (BCL generics and generic script methods/types), tuples, anonymous types.
- **Declarations** — classes with fields, constructors, properties, indexers, static members, field initializers, inheritance and `abstract`; interfaces; structs; enums; operator overloading; extension methods; top-level functions and local functions; default parameter values; `out`/`ref` parameters.
- **Expressions** — the full arithmetic/bitwise/logical set, compound assignment, `??` / `??=` / `?.`, ternary, casts, `is`/`as` with pattern matching, `typeof`, `nameof`, `checked`, string interpolation, object and collection initializers, lambdas (expression and block bodied).
- **Statements** — `if`, `while`, `do`, `for`, `foreach`, `switch` statements and expressions, `break`/`continue`/`goto`, `return`, `throw`, `try`/`catch`/`finally`, `using` statements and declarations, `yield return`/`yield break` iterators, `async`/`await`.
- **Imports** — `using`, `using static` and alias directives. `System` is always imported.
- **Top level** — statements run in order; the value of a top-level `return` is what `Evaluate` returns.

Semantics to be aware of:

- **Lambdas capture by value.** A lambda sees a snapshot of a captured local; later changes to that local outside the lambda aren't visible inside it (referenced objects are shared, as usual). Lambdas convert to matching `Func<…>` / `Action<…>` delegates when passed to host APIs.
- **Overloads** resolve by best-fitting argument types.
- **Undeclared identifiers are compile errors**, unless the host lists them in `ScriptCompileOptions.GlobalNames` (see below).

---

## Sandbox

Always enforced, at every trust level (`MethodInvocationGuard`):

- **Denied namespaces** — `System.Reflection`, `System.IO`, `System.Diagnostics`, `System.Net`, `System.Runtime.InteropServices`, `System.Runtime.Loader`, `System.Threading` (except `System.Threading.Tasks`), `System.Security`, `System.Configuration`, `System.Resources`, `Microsoft`.
- **Denied types** — `Type`, `Activator`, `AppDomain`, `AppContext`, `Environment`, `GC`, `Delegate`, `MulticastDelegate`, `OperatingSystem`, `WeakReference`.
- **VM limits** — 50,000,000 instructions per evaluation, call depth 100 and 1,048,576 stack slots. Exceeding any of them throws `ScriptRuntimeException`, so runaway loops and deep recursion fail cleanly instead of hanging or overflowing the host.

Configurable:

- **`ScriptCompileOptions.DeniedNamespacePrefixes`** — namespaces hidden from scripts at compile time. The default is `ODDGames.Bugpunch`, which matches that namespace and its children.
- **Trust levels** — `ScriptCompileOptions.TrustLevel` (default `Untrusted`) plus `MethodInvocationGuard.EnterTrustLevel(level)` around evaluation. A member marked `[ScriptProtected(level)]` can't be called from below that level. A blocked call throws `ScriptRuntimeException("Blocked: …")` before invocation.
- **Dynamic predicates** — `MethodInvocationGuard.Register(...)` adds process-wide checks. The SDK registers one that refuses `Object.Destroy` on `[ScriptProtected]` GameObjects.

The Remote IDE runs dashboard scripts at **`Admin`** trust. Only members marked `[ScriptProtected(ScriptTrustLevel.System)]` stay out of reach there.

There is no wall-clock timeout and no heap cap. The instruction budget bounds CPU per evaluation, but a script can still allocate large objects through host APIs.

---

## Host integration

```csharp
using System.Collections.Generic;
using ODDGames.Scripting;

var options = new ScriptCompileOptions();
options.GlobalNames.Add("nums");              // declare host globals at compile time
options.DefaultUsings.Add("UnityEngine");     // implicit using for every script

if (ScriptCompiler.TryCompile(@"
        var sum = 0;
        foreach (var n in nums) sum += n;
        return sum;
    ", out var compiled, options))
{
    var host = new ScriptHost();
    host.Globals["nums"] = new List<int> { 1, 2, 3, 4 };
    var result = compiled.Evaluate(host);    // 10
}
else
{
    foreach (var d in compiled.Diagnostics)
        UnityEngine.Debug.LogError(d);
}
```

- **`ScriptCompiler`** is static. `Compile(source, options)` throws `ScriptCompileException` (with `Diagnostics`) on errors; `TryCompile` returns `false` instead. `Warmup()` pre-resolves common Unity types so the first script hits a warm reflection cache; `ClearCache()` / `ClearReflectionCache()` flush them.
- **`CompiledScript`** — `IsValid`, `Diagnostics`, `Evaluate(host = null)` (null uses `ScriptHost.Default`), plus `DeclaredTypes`, `CreateInstance` and `Invoke` for driving script-defined classes.
- **`ScriptHost`** — `Globals` holds the values for the names declared in `GlobalNames`. `RegisterFastPath(method, invoker)` installs a direct invoker for a hot host method so calls skip reflection.
- **`ScriptCompileOptions`** — `DefaultUsings`, `AssemblyAllowlist` (limit type resolution to named assemblies), `DeniedNamespacePrefixes`, `TrustLevel`, `CacheKey`, `GlobalNames`, `SourceName` (used in diagnostics).
- **Type discovery** — `TypeResolver` resolves names against the loaded assemblies (or `AssemblyAllowlist`), minus denied namespaces and types. Members are reflected once and then hit a per-call-site inline cache.
- **Runtime errors** surface as `ScriptRuntimeException` with a source `Location`.

### Script behaviours and the browser IDE

- **`ScriptBehaviour`** gives a script-defined class a `Start()` / `Update(dt)` / `Destroy()` lifecycle plus named events (`Send("OnGoal")`). Context is injected with `With("self", gameObject)`. It is engine-agnostic and IL2CPP-safe.
- **`ODDGames.Scripting.Ide.ScriptIde.Host(rootDirectory)`** starts an in-process, Monaco-based browser IDE: file tree, IntelliSense from the runtime's own resolver, diagnostics and a Run panel. It binds to loopback by default. It can read, write and run code on the host, so expose it only on a trusted network.

---

## Runtime model

- **VMs** are rented from a thread-local pool. Nested calls rent another VM, so frames are never shared.
- **Locals** are sized per chunk. Slot 0 is `this` for instance methods.
- **Exceptions** use a handler table: on throw, the VM finds the nearest handler, rewinds the operand stack and jumps to it. `finally` blocks run on every exit path.
- **Inline caches** at dynamic-dispatch sites skip repeat reflection lookups.
- **GC** is .NET's. The VM holds no long-lived roots once an evaluation returns.

---

## Building and testing

```bash
cd scripting/Runtime
dotnet build -c Release          # → ODDGames.Scripting.dll (netstandard2.1)

cd ../Tests
dotnet test                      # xUnit suite (net8.0)
```

The source of truth is the private `oddgames/unity-scripting` repo, checked out as the sibling `scripting/` directory. The SDK's `build-all.ps1` compiles it and copies the DLL into `package/Plugins/`. The VM is pure managed code, so the same DLL serves every lane (Android / iOS / Editor / Standalone).

The test suite (~80 files under `scripting/Tests/`) covers language semantics end to end: classes, inheritance, generics, lambdas, async, iterators, pattern matching, exceptions, the sandbox, the IDE and microbenchmarks.

---

## Source map

| Concern | Path (under `scripting/Runtime/`) |
|---|---|
| Compile entry + cache | `ScriptCompiler.cs`, `CompiledScriptCache.cs` |
| Public surface | `ScriptHost.cs`, `ScriptCompileOptions.cs`, `CompiledScript.cs`, `ScriptInstance.cs`, `ScriptProtectedAttribute.cs`, `ScriptCompileException.cs`, `ScriptDiagnostic.cs` |
| Lexer | `Lexing/` |
| Parser + AST | `Parsing/Parser.cs`, `Syntax/` |
| Binding + lowering | `Binding/` |
| Emitter + bytecode | `Emit/Emitter.cs`, `Bytecode/` |
| VM + sandbox | `VirtualMachine/` (`VM`, `StackSlot`, `ScriptObject`, `LambdaValue`, `LambdaShim`, `ScriptIterator`, `ScriptAsyncBuilder`, `MethodInvocationGuard`) |
| Behaviours | `ScriptBehaviour.cs`, `ScriptBehaviourHost.cs`, `ScriptBehaviourManager.cs` |
| Browser IDE | `Ide/` |
| SDK consumer | `server/sdk/csharp-src/Bugpunch/Sources/RemoteIDE/Services/ScriptRunner.cs` |
