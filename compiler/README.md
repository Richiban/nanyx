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
├── Nyx.Parser/                # Main parser library (F#)
│   ├── Program.fs             # Parser implementation and AST
│   └── NyxParser.fsproj
├── Nyx.Parser.Tests/          # Parser-focused unit tests
│   ├── ParserTests.fs
│   ├── Parser.Tests.fsproj
│   └── testdata/
├── Nanyx.Compiler/              # Type-checker core library
│   └── NyxCompiler.fsproj
├── Nanyx.Compiler.Cli/          # CLI entry point wrapping the compiler
│   └── NyxCompiler.Cli.fsproj
├── Nanyx.Compiler.Tests/        # Compiler-level unit/integration tests
│   └── Nanyx.Compiler.Tests.fsproj
├── Nyx.Transpiler.JS/         # JS transpiler
│   └── Transpiler.fsproj
├── Nyx.Transpiler.JS.Tests/   # Transpiler tests
│   └── Transpiler.Tests.fsproj
├── Nanyx.Compiler.sln           # Solution covering all projects
└── README.md
```

## Building

```powershell
cd compiler
dotnet build Nanyx.Compiler.sln
```

## Running Tests

```powershell
cd compiler
dotnet test Nanyx.Compiler.sln
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

## Running the Parser

```powershell
dotnet run --project compiler/Nyx.Parser/NyxParser.fsproj sample.nyx
```

## WebAssembly (MVP)

You can now emit WebAssembly text format (`.wat`) directly from the compiler CLI:

```powershell
dotnet run --project compiler/Nanyx.Compiler.Cli/NyxCompiler.Cli.fsproj -- sample.nyx --target wasm
```

Optional output path:

```powershell
dotnet run --project compiler/Nanyx.Compiler.Cli/NyxCompiler.Cli.fsproj -- sample.nyx --target wasm --out sample.wat
```

Current WASM backend support is intentionally minimal and focused on bootstrapping:

- top-level `def` values exported as wasm functions
- lambda-backed defs compiled as wasm functions with `i32` params/result
- integer literals
- integer binary operators (`+`, `-`, `*`, `/`, `%`, comparisons)
- bare tag literals (`#ok`, `#error`, etc.) lowered to deterministic `i32` discriminants
- function calls to known module defs
- simple expression blocks with local `def` assignments (`local.set`/`local.get`)
- magic `dbg(...)` support via wasm host import: `(import "env" "dbg" (func $dbg (param i32)))`

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
