# ODDGames.Scripting

Embedded C#-like scripting language shipped inside the Bugpunch SDK. Powers the Remote IDE's `ScriptRunner` (live, on-device evaluation from the dashboard) and any in-game tooling that needs to run untrusted code without bringing in `Reflection.Emit` or a JIT.

> **Distribution.** Ships as `ODDGames.Scripting.dll` under `Plugins/` next to `ODDGames.Bugpunch.dll`. No separate UPM package — the runtime DLL is co-located with the SDK runtime DLL on every platform.

---

## Why it exists

- **IL2CPP-safe.** No `Reflection.Emit`, no `DynamicMethod`, no `System.Reflection.Emit.AssemblyBuilder`. Mono runtime stripping won't break it. AOT platforms (iOS, console) are first-class.
- **Sandboxable.** Three trust levels (`Untrusted`, `Admin`, `System`) gate which host APIs a script can call. `[ScriptProtected]` attributes mark members off-limits below a level.
- **Re-entrant.** Each invocation gets a fresh VM instance; nested script calls don't share stack frames.
- **Small surface.** Compiler + VM live in one assembly with no dependencies. Drop in, register host types, run code.

The SDK's primary consumer is `BugpunchClient.Sources/RemoteIDE/Services/ScriptRunner.cs`. The dashboard's "Run Script" panel posts source text → server forwards via tunnel → SDK compiles + runs → result returned as a JSON envelope.

---

## Pipeline

```
source ─► Lexer ─► Parser ─► SemanticAnalyzer ─► Emitter ─► Chunk
                                                              │
                                                              ▼
                                                             VM ─► result
```

| Stage | File | Notes |
|---|---|---|
| Lexer | `Lexing/Lexer.cs` | Hand-written single-pass tokenizer; line/col tracking; 80+ token kinds in `TokenKind.cs`. |
| Parser | `Parsing/Parser.cs` | Pratt-style for expressions, recursive-descent for statements/declarations. |
| AST | `Syntax/{Expressions,Statements,Declarations}.cs` | Immutable nodes. |
| Semantic | `Binding/SemanticAnalyzer.cs` + `Binding/TypeResolver.cs` | Type inference for literals / `var`, symbol scoping, namespace import resolution, denial-list enforcement. |
| Emitter | `Emit/Emitter.cs` | Walks AST, emits to a `ChunkBuilder` (bytecode + constants + handler table + inline-cache slots). |
| Bytecode | `Bytecode/{Chunk,OpCode,InlineCache}.cs` | Operand widths: u8 / u16 / i32 little-endian. ~50 opcodes. |
| VM | `VirtualMachine/VM.cs` | Big-switch dispatcher over opcodes; `StackSlot[]` operand stack; per-call locals. |

`StackSlot` is a tagged-union struct: primitives (i32 / i64 / f32 / f64 / bool / char) live inline; reference types (string, user objects, delegates) sit in the `Obj` field. Boxing only happens at the boundary, not during arithmetic.

---

## Language surface

What the language has today, in one list. Anything not here is not supported.

**Types.** `int`, `long`, `short`, `byte`, `uint`, `ulong`, `ushort`, `sbyte`, `float`, `double`, `decimal`, `bool`, `char`, `string`, `object`, `null`. Generics on BCL collections (`List<T>`, `Dictionary<K, V>`, `IEnumerable<T>`). User-defined `class` with fields, methods, constructors, `this`. Method-overload resolution is MVP — first matching arity wins.

**Literals.** Decimal, hex (`0x...`), binary (`0b...`), char `'c'`, string `"..."` with escapes, `true`/`false`/`null`.

**Operators.** Arithmetic (`+ - * / %` and unary `-`), pre/post `++` `--`, comparison (`== != < <= > >=`), logical (`&& || !` with short-circuit), bitwise (`& | ^ ~ << >>`), compound assignment (`+= -= *= /= %= &= |= ^= <<= >>= ??=`), null-coalescing (`??`), null-conditional (`?.`), ternary (`?:`), member (`.`), indexer (`[]`), cast `(T)expr`, `typeof`, `is`, `as`, `new`.

**Control flow.** `if`/`else if`/`else`, `while`, `do/while`, `for`, `foreach`, `break`, `continue`, `return`, `throw`. Block scoping with shadowing. Implicit `return` of last expression at script top-level.

**Functions.** Lambda expressions (`x => x * 2`) and block-bodied (`x => { ... }`). Captures are by-value snapshot — outer-variable mutations after capture do not propagate. Auto-coerces to matching `Func<...>` / `Action<...>` delegate signatures via `LambdaShim`.

**Exceptions.** `try` / `catch` (untyped catch-all and typed catches with binding), `throw`, rethrow (`throw;` inside `catch`). Multiple catch clauses match in source order. Nested try/catch with proper unwinding.

**Imports.** `using System;` style directives, `using static`, alias forms. Implicit defaults: `System`. Implicit denials: anything starting with `ODDGames.Bugpunch` (configurable on `ScriptCompileOptions`).

**Not yet supported.** `finally`, `switch`/`case`, `async`/`await`, top-level free functions (only methods on script classes), property get/set codegen (syntax parses, emit is incomplete), interfaces, base-class semantics, default parameter values, expression field initializers, static fields on script classes.

---

## Host integration

A host registers types and globals through `ScriptHost`:

```csharp
var host = new ScriptHost();
host.Globals["player"] = playerInstance;            // visible as `player` in script
host.Globals["Log"] = (Action<string>)Debug.Log;    // callable as Log("hi")
```

- **Type discovery** — `TypeResolver` resolves dotted names against every loaded assembly. Add namespace imports via `ScriptCompileOptions.ImplicitUsings`. Block whole namespaces via `DeniedNamespacePrefixes`.
- **Marshalling** — `StackSlot.FromObject` / `Unbox` handle the boundary. Primitives stay unboxed inside the VM.
- **Trust levels.** Set per call via `MethodInvocationGuard.EnterTrustLevel(level)`. `ScriptRunner` runs untrusted by default; admin scripts bump the thread-local. Process-global predicates (`MethodInvocationGuard.Register`) can deny calls dynamically — the SDK uses one to block `Object.Destroy(*)` on `[ScriptProtected]` GameObjects.
- **`[ScriptProtected]`** — attribute on type or member; takes a minimum trust level. Caller below that level → `ScriptSecurityException` at the call site, before invocation.

### Example

```csharp
var compiler = new ScriptCompiler();
var compiled = compiler.Compile(@"
    var sum = 0;
    foreach (var n in nums) sum += n;
    return sum;
", new ScriptCompileOptions());

if (!compiled.Diagnostics.HasErrors) {
    var host = new ScriptHost();
    host.Globals["nums"] = new List<int> { 1, 2, 3, 4 };
    var result = compiled.Evaluate(host);   // -> 10
}
```

---

## Runtime model

- **Stack.** `StackSlot[]` operand stack, dynamically resized. `_sp` tracks top.
- **Locals.** Sized to `chunk.LocalCount` per call. Slot 0 is `this` for instance methods.
- **Calls.** Cross-call state isolated — each `Execute*` builds its own `VM` instance. No shared frame stack across nested host-script-host-script chains.
- **Exceptions.** Handler stack of `ExceptionHandler { pc, type, depth }`. On throw the VM scans handlers, rewinds the operand stack to saved depth, pushes the exception, jumps to handler PC.
- **Inline cache.** Two-entry monomorphic cache per dynamic-dispatch site (`InlineCache.cs`). Hits skip reflection; polymorphic sites fall back to a `MethodInfo` lookup.
- **GC.** Delegated to .NET. The VM holds no long-lived roots once a call returns — locals/stack are released with the `VM` instance.

### What's missing as a sandbox

- No instruction-count quota.
- No memory cap (operand stack can grow without bound).
- No wall-clock timeout in the VM itself.

If you need any of those, wrap `compiled.Evaluate(...)` in a `CancellationToken` + watchdog thread on the host side. The SDK's `ScriptRunner` accepts a hard cancel from the dashboard but does not cap CPU.

---

## Building

```bash
cd scripting/Runtime
dotnet build -c Release
# → ODDGames.Scripting.dll (netstandard2.1)
```

The SDK build copies the DLL into `package/Plugins/` next to `ODDGames.Bugpunch.dll`. Both ride together to every platform lane (Android / iOS / Standalone) — no JNI / P-Invoke for scripting; the VM is pure managed.

---

## Testing

xUnit suite under `scripting/Tests/` (~20 files): end-to-end semantics, lambdas, generics, try/catch, loops, classes, indexers, statics, parser, lexer, error reporting, microbenchmarks.

```bash
cd scripting/Tests
dotnet test
```

---

## Source map

| Concern | Path |
|---|---|
| Compile entry | `Runtime/ScriptCompiler.cs` |
| Lexer / tokens | `Runtime/Lexing/{Lexer,TokenKind}.cs` |
| Parser | `Runtime/Parsing/Parser.cs` |
| AST | `Runtime/Syntax/{Expressions,Statements,Declarations}.cs` |
| Semantic + types | `Runtime/Binding/{SemanticAnalyzer,TypeResolver,ScriptType}.cs` |
| Emitter | `Runtime/Emit/Emitter.cs` |
| Bytecode | `Runtime/Bytecode/{Chunk,OpCode,InlineCache}.cs` |
| VM | `Runtime/VirtualMachine/{VM,StackSlot,ScriptObject,LambdaValue,MethodInvocationGuard}.cs` |
| Public surface | `Runtime/{ScriptHost,ScriptCompileOptions,CompiledScript,ScriptProtectedAttribute}.cs` |
| SDK consumer | `BugpunchClient.Sources/RemoteIDE/Services/ScriptRunner.cs` |

---

## Known limits

- `finally`, `switch`, `async/await`, properties (get/set codegen), interfaces, inheritance — parser may accept the syntax; emitter does not yet implement them. Do not rely on them.
- Method overload resolution picks the first method with matching parameter count. Same-name methods of differing arity are fine; same arity with different parameter types may resolve to the wrong one.
- Lambdas capture by value. Mutating the captured local outside the lambda has no effect inside.
- No `ScriptHost.RegisterFastPath` yet — every dynamic call goes through reflection (with inline cache). Tight loops over host calls are noticeably slower than native C#.
- No stack-overflow guard on deep recursion. Untrusted scripts can crash the host with a managed `StackOverflowException`. Wrap evaluation in your own thread if you accept arbitrary input.
