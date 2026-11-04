# Nanyx Parser

A parser for the Nanyx programming language built with FParsec.

## Features

Currently supports parsing:

- **Module declarations**: `module ModuleName`
- **Value definitions**: `def identifier = expression`
- **Literals**:
  - Strings: `"Hello world"`
  - Integers: `42`
  - Floats: `3.14159`
  - Booleans: `true`, `false`
- **Identifiers**: Variable references like `someValue`
- **Function calls**: `println("Hello")`, `add(1, 2)`, nested calls
- **Lambda expressions**: 
  - Standard: `{ x -> x }`, `{ x, y -> add(x, y) }`, `{ 42 }`
  - **Shorthand lambdas**:
    - Binary operators: `{ * }` → `{ x, y -> x * y }`
    - Unary operations: `{ * 2 }` → `{ x -> x * 2 }`, `{ > 10 }` → `{ x -> x > 10 }`
    - Property access: `{ .name }` → `{ x -> x.name }`
- **Pipe operator**: `\`
  - Simple: `x \f` → `f(x)`
  - With arguments: `x \f(y, z)` → `f(x, y, z)`
  - Chaining: `x \f \g \h` → `h(g(f(x)))`
- **Binary operators**: `+`, `-`, `*`, `/`, `<`, `>`, `<=`, `>=`, `==`, `!=`
  - With correct precedence (multiplication/division before addition/subtraction)
- **Parenthesized expressions**: `(1 + 2) * 3`
- **Whitespace handling**: Flexible whitespace and newlines

## Project Structure

```
compiler/
├── Nanyx.Parser/              # Parser library (F#, FParsec)
│   └── Program.fs             # Parser implementation and AST
├── Nanyx.Parser.Tests/        # Parser unit and fixture tests
├── Nanyx.Compiler/            # Type checker (F#)
├── Nanyx.Compiler.Tests/      # Compiler-level unit/integration tests
├── Nanyx.Compiler.Wasm/       # WebAssembly text (.wat) backend (F#)
├── Nanyx.Compiler.Wasm.Tests/ # Backend fixture, end-to-end and execution tests
├── Nanyx.Compiler.Runtime/    # Runs emitted modules on an embedded Wasmtime engine (F#)
├── Nanyx.Compiler.Cli/        # The `nanyx` executable (C#, Consolo)
├── Nyx.Compiler.slnx          # Solution covering all projects
└── README.md
```

## Building

```powershell
cd compiler
dotnet build Nyx.Compiler.slnx
```

## Running Tests

```powershell
cd compiler
dotnet test Nyx.Compiler.slnx
```

All 56 tests should pass:
- **Unit tests** (49): Literal parsing, identifiers, function calls, lambdas (standard & shorthand), pipe operator, binary operators, whitespace, errors
- **Integration tests** (7): Full .nyx file parsing from `testdata/` directory
  - `literals.nyx`: String, int, float, boolean literals
  - `function-calls.nyx`: Various function call patterns
  - `lambdas.nyx`: Lambda expressions with different parameter counts
  - `shorthand-lambdas.nyx`: Shorthand lambda syntax (binary ops, unary ops, property access)
  - `piping.nyx`: Pipe operator usage (simple, with args, chaining)
  - `binary-operators.nyx`: Arithmetic, comparison, and equality operators
  - `comprehensive.nyx`: Mix of all features
  - `binary-operators.nyx`: Arithmetic, comparison, and equality operators
  - `comprehensive.nyx`: Mix of all features

Run specific test files:
```powershell
dotnet test --filter "Parse literals.nyx"
```

## Continuous integration and releases

The `Compiler` workflow (`.github/workflows/compiler.yml`) builds and tests the solution on
Linux and Windows for every pull request and push to `main` that touches `compiler/`, and
publishes a self-contained `nanyx` executable for each supported platform as a workflow artifact.

To cut a release, bump `<Version>` in `Nanyx.Compiler.Cli/NanyxCompiler.Cli.csproj`, merge,
then push a tag of the form `compiler/<version>`:

```powershell
git tag compiler/0.1.0
git push origin compiler/0.1.0
```

The workflow checks that the tag matches the project version and attaches the binaries to a
GitHub release. (The VS Code extension is released the same way with `extension/<version>` tags.)

## Running programs

The parser, type checker, WASM backend and runtime are libraries; the `nanyx` CLI is the only
executable. It takes a target and a `.nyx` file:

| Target  | What it does                                                                 |
| ------- | ---------------------------------------------------------------------------- |
| `run`   | Compiles the file and runs it on the embedded Wasmtime engine                |
| `wasm`  | Emits WebAssembly text (`.wat`) next to the file, or to `--out <path>`       |
| `check` | Type-checks the file and prints the inferred types                           |
| `parse` | Prints the AST                                                               |

The hello world from the docs works as written:

```nanyx
import nanyx/console

println("Hello, Nanyx!")
```

```powershell
dotnet run --project compiler/Nanyx.Compiler.Cli/NanyxCompiler.Cli.csproj -- run hello.nyx
# Hello, Nanyx!
```

`run` keeps its own output off stdout: only the program writes there, diagnostics go to stderr, and
the exit code is 0 on success, 1 when the program fails to compile or traps, and 2 for a usage error.
The project form (`module main` with `@entry def main = { ... }`) runs the same way: `@entry` marks the
definition the program starts at.

The standard library does not exist yet. `import nanyx/console` is accepted and does nothing, and
`println` (`string -> unit`) is built in, unless the module declares a context member of that name.

## WebAssembly (MVP)

Current WASM backend support is intentionally minimal and focused on bootstrapping:

- top-level `def` values exported as wasm functions
- lambda-backed defs compiled as wasm functions with `i32` params/result
- integer literals
- integer binary operators (`+`, `-`, `*`, `/`, `%`, comparisons)
- bare tag literals (`#ok`, `#error`, etc.) lowered to deterministic `i32` discriminants
- function calls to known module defs
- simple expression blocks with local `def` assignments (`local.set`/`local.get`)
- top-level expressions (and the `@entry` definition) run from a WebAssembly start function
- `println(...)` and `dbg(...)` of integers and strings write to stdout through WASI `fd_write`
- `dbg(#tag)` calls a host import `(import "env" "dbg_tag_<tag>" (func))`, which the runtime provides

Other language constructs will currently fail fast with an explicit "Unsupported ... for WASM MVP backend" message.

## AST Structure

```fsharp
type Literal =
    | StringLit of string
    | IntLit of int
    | FloatLit of float
    | BoolLit of bool

type Expression =
    | LiteralExpr of Literal
    | IdentifierExpr of Identifier

type Definition =
    | ValueDef of Identifier * Expression

type TopLevelItem =
    | ModuleDecl of ModuleName
    | Def of Definition

type Module = TopLevelItem list
```

## Next Steps

To extend the parser, consider adding:
- Function definitions with parameters
- Lambda expressions
- Function calls
- Operators and binary expressions
- Pattern matching
- Type annotations
- Comments (single-line `--` and multi-line `---`)
- Indentation-based blocks
- Collections (lists, records, tuples)

## Example

Input:
```nanyx
module Test

def message = "Hello world"
def count = 42
def pi = 3.14159
def flag = true
```

Parsed AST:
```fsharp
[ModuleDecl "Test";
 Def (ValueDef ("message", LiteralExpr (StringLit "Hello world")));
 Def (ValueDef ("count", LiteralExpr (IntLit 42)));
 Def (ValueDef ("pi", LiteralExpr (FloatLit 3.14159)));
 Def (ValueDef ("flag", LiteralExpr (BoolLit true)))]
```
