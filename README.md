# Nanyx Programming Language

Nanyx is a statically-typed, functional-first programming language designed for expressive workflows, clean syntax, and strong type safety. It combines the best ideas from functional programming with practical features for real-world development, featuring Hindley-Milner type inference, context-based effects, powerful pattern matching, and a unique pipeline operator for composable transformations.

## Table of Contents

- [Overview](#overview)
- [Key Features](#key-features)
- [Quick Start](#quick-start)
- [Language Features](#language-features)
  - [Basic Syntax](#basic-syntax)
  - [Functions and Lambdas](#functions-and-lambdas)
  - [Pipeline Operator](#pipeline-operator)
  - [Types and Type Inference](#types-and-type-inference)
  - [Pattern Matching](#pattern-matching)
  - [Records and Tuples](#records-and-tuples)
  - [Tag Unions](#tag-unions)
  - [Contexts as Effects](#contexts-as-effects)
  - [Generics and Type Parameters](#generics-and-type-parameters)
  - [Constraints and Refinement Types](#constraints-and-refinement-types)
- [Project Structure](#project-structure)
- [Building from Source](#building-from-source)
- [Running the Compiler](#running-the-compiler)
- [Examples](#examples)
- [Design Principles](#design-principles)
- [Development Status](#development-status)
- [Contributing](#contributing)
- [License](#license)

## Overview

Nanyx is built on the principle that code should be clear, composable, and correct. It takes inspiration from languages like F#, Haskell, OCaml, and Elm while maintaining its own unique identity through features like context-based effects and pipeline-centric programming.

**Current Implementation Status:**
- ✅ Full parser with comprehensive AST
- ✅ Hindley-Milner type inference engine
- ✅ Type checker with generics support
- ✅ JavaScript transpiler
- ✅ Command-line compiler interface
- 🚧 Standard library (in progress)
- 🚧 VS Code extension with syntax highlighting

The repository includes the full language specification, compiler implementation in F#, example programs, and documentation for learning the language.

## Key Features

### 🎯 **Expression-Oriented Design**
Everything in Nanyx is an expression that returns a value. No statements, just composable expressions that make code flow naturally.

### 🔀 **Pipeline Operator**
The `\` operator lets you chain transformations in a readable, left-to-right manner:
```nanyx
data \parse \validate \transform \save
```

### 🏷️ **Powerful Pattern Matching**
Exhaustive pattern matching on records, tag unions, literals, and more:
```nanyx
match result
  | #ok(value) -> process(value)
  | #error(msg) -> logError(msg)
```

### 🎭 **Context-Based Effects**
Manage side effects explicitly through contexts, providing algebraic effect handlers without the complexity:
```nanyx
context Console = (println: string -> ())
def greet: <Console> string -> () = { name ->
  println("Hello, {name}!")
}
```

### 🔒 **Strong Static Typing with Inference**
Full Hindley-Milner type inference means you rarely need to write type annotations, but you can when it improves clarity.

### 📦 **Records and Tag Unions**
Express your domain model precisely with structural records and discriminated unions:
```nanyx
type User = (name: string, email: string, role: #admin | #user)
```

### 🎨 **Shorthand Lambdas**
Concise syntax for common lambda patterns:
```nanyx
numbers \map { * 2 }        -- multiply by 2
users \filter { .age > 18 } -- access property
items \map { > 10 }         -- comparison
```

### 🧩 **Minimal but Comprehensive**
Small core with powerful abstractions. The language prioritizes having fewer features that compose well over many single-purpose features.

## Quick Start

### Prerequisites

- .NET 9.0 or later
- (Optional) VS Code for syntax highlighting

### Installation

Clone the repository:
```bash
git clone https://github.com/Richiban/nyx.git
cd nyx
```

Build the compiler:
```bash
cd compiler
dotnet build Nanyx.Compiler.sln
```

Run tests to verify the installation:
```bash
dotnet test Nanyx.Compiler.sln
```

### Your First Nanyx Program

Create a file `hello.nyx`:

```nanyx
module Hello

def message = "Hello, Nanyx!"

def main = {
  message \dbg
}

main()
```

Compile and type-check it:
```bash
dotnet run --project compiler/Nanyx.Compiler.Cli/NyxCompiler.Cli.fsproj hello.nyx
```

## Language Features

### Basic Syntax

Nanyx uses clean, minimal syntax with significant indentation:

```nanyx
module MyModule

-- This is a single-line comment

--- 
This is a 
multi-line comment
---

-- Value definitions
def pi = 3.14159
def greeting = "Hello"
def count = 42
def isActive = true

-- Function definition with type annotation
def add: (int, int) -> int = { x, y ->
  x + y
}
```

### Functions and Lambdas

Functions are first-class values in Nanyx:

```nanyx
-- Named function
def double: int -> int = { x -> x * 2 }

-- Multi-parameter function  
def multiply: (int, int) -> int = { x, y -> x * y }

-- Higher-order function
def apply: ((a -> b), a) -> b = { f, x -> f(x) }

-- Shorthand lambda for binary operators
def doubles = [1, 2, 3] \map { * 2 }

-- Shorthand lambda for property access
def names = users \map { .name }

-- Shorthand lambda for comparison
def adults = users \filter { .age > 18 }
```

### Pipeline Operator

The pipeline operator `\` passes the result of one expression as the first argument to the next function:

```nanyx
-- Simple pipeline
42 \double \dbg  -- prints 84

-- Pipeline with additional arguments
data \process(config, options) \validate \save

-- Chaining multiple transformations
"hello world"
  \split(" ")
  \map { .toUpperCase() }
  \join("-")
  \dbg  -- prints "HELLO-WORLD"

-- Pipeline in function composition
def processUser = { user ->
  user
    \validateEmail
    \normalizeData
    \saveToDatabase
    \sendWelcomeEmail
}
```

### Types and Type Inference

Nanyx features Hindley-Milner type inference with optional type annotations:

```nanyx
-- Type inference (no annotation needed)
def add = { x, y -> x + y }  -- inferred as: (number, number) -> number

-- Explicit type annotations for clarity
def length: string -> int = { s -> s.length }

-- Type aliases
type UserId = int
type Email = string

-- Record types
type Person = (
  name: string
  age: int
  email: Email
)

-- Function types
type Validator(a) = a -> bool
type Transformer(a, b) = a -> b
```

### Pattern Matching

Pattern matching is exhaustive and type-safe:

```nanyx
-- Matching on tag unions
def describe: Option(int) -> string = { opt ->
  match opt
    | #some(x) -> "Got value: {x}"
    | #none -> "No value"
}

-- Matching on records
def greet: Person -> string = { person ->
  match person
    | (name = "Alice", age = age) -> "Hi Alice, age {age}!"
    | (name = name, age = a) if a >= 18 -> "Hello adult {name}"
    | (name = name, _) -> "Hi young {name}"
}

-- Matching on literals
def classify: int -> string = { x ->
  match x
    | 0 -> "zero"
    | 1 -> "one"
    | n if n < 0 -> "negative"
    | n if n > 100 -> "large"
    | _ -> "other"
}

-- Matching with comparison operators
def sign: int -> string = { x ->
  match x
    | < 0 -> "negative"
    | > 0 -> "positive"
    | _ -> "zero"
}
```

### Records and Tuples

Records are structural types with named fields:

```nanyx
-- Record creation
def user = (
  name = "Alice"
  age = 30
  email = "alice@example.com"
)

-- Record access
def userName = user.name

-- Record update (creates new record)
def olderUser = user with age = 31

-- Tuples (anonymous records with numbered fields)
def point = (10, 20)
def coords = (x = 10, y = 20)  -- same as tuple but with names

-- Tuple destructuring
def (x, y) = point
```

### Tag Unions

Tag unions (also known as sum types or discriminated unions) represent values that can be one of several variants:

```nanyx
-- Simple tag union
type Result(a) = 
  | #ok(a)
  | #error(string)

-- Tag union with multiple variants
type Shape =
  | #circle(float)
  | #rectangle(float, float)
  | #triangle(float, float)

-- Using tag unions
def area: Shape -> float = { shape ->
  match shape
    | #circle(r) -> 3.14159 * r * r
    | #rectangle(w, h) -> w * h
    | #triangle(b, h) -> 0.5 * b * h
}

-- Option type (no null in Nanyx!)
type Option(a) = 
  | #some(a)
  | #none

def findUser: UserId -> Option(User) = { id ->
  -- lookup logic
  #some(user)  -- or #none if not found
}
```

### Contexts as Effects

Contexts provide a way to handle effects explicitly without threading parameters through every function:

```nanyx
-- Define a context type
context type Console = (
  println: string -> ()
  readLine: () -> string
)

-- Use context in function signature
def greet: <Console> string -> () = { name ->
  println("Hello, {name}!")
  println("How are you?")
  def response = readLine()
  println("You said: {response}")
}

-- Provide context implementation
def runGreeting = {
  use Console(
    println = { s -> -- actual console output }
    readLine = { -- actual console input }
  )
  
  greet("Alice")
}

-- State effect example
context type State(a) = (
  get: () -> a
  set: a -> ()
)

def counter: <State(int)> () -> int = {
  def current = get()
  set(current + 1)
  current
}

-- Exception handling via contexts
context type Raise = (
  ctx raise: string -> a
)

def safeDivide: <Raise> (int, int) -> int = { x, y ->
  if y == 0 -> raise("Division by zero")
  else -> x / y
}

def handleDivision = {
  use Raise(
    ctx raise = { msg -> 
      dbg("Error: {msg}")
      resume 0  -- resume with default value
    }
  )
  
  safeDivide(10, 0)  -- Returns 0 instead of crashing
}
```

### Generics and Type Parameters

Nanyx supports parametric polymorphism with automatic generalization:

```nanyx
-- Generic function (type parameter α is inferred)
def identity: a -> a = { x -> x }

-- Generic list operations
def map: (list(a), (a -> b)) -> list(b) = { xs, f ->
  match xs
    | [] -> []
    | [head, ...tail] -> [f(head), ...map(tail, f)]
}

-- Multiple type parameters
def zip: (list(a), list(b)) -> list((a, b)) = { xs, ys ->
  match (xs, ys)
    | ([], _) -> []
    | (_, []) -> []
    | ([x, ...xs2], [y, ...ys2]) -> [(x, y), ...zip(xs2, ys2)]
}

-- Constrained generics with traits
type Monoid(a, ~combine, ~neutral) = (
  ~neutral: a
  ~combine: (a, a) -> a
)

def fold: [Monoid(a, `+`, `0`)] (list(a)) -> a = { xs ->
  xs \fold(`0`, `+`)
}
```

### Constraints and Refinement Types

Nanyx supports refinement types for more precise specifications:

```nanyx
-- Constrained type definition
type Email = s :: string where isValidEmail(s)
  def isValidEmail(s :: string) = s.contains("@") && s.contains(".")

-- Natural numbers
type Nat = n :: int where n >= 0

-- Ordered pairs
type OrderedPair(n :: number) = (x :: n, y :: n) where x < y

-- Age constraint
type Age = n :: int where n >= 0 && n <= 150
```

## Project Structure

The Nanyx repository is organized as follows:

```
nyx/
├── compiler/               # Compiler implementation (F#)
│   ├── Nyx.Parser/        # FParsec-based parser
│   ├── Nanyx.Compiler/      # Type checker and inference engine
│   ├── Nanyx.Compiler.Cli/  # Command-line interface
│   ├── Nyx.Transpiler.JS/ # JavaScript code generator
│   ├── Nyx.Parser.Tests/  # Parser unit and integration tests
│   ├── Nanyx.Compiler.Tests/# Compiler and type checker tests
│   └── Nyx.Transpiler.JS.Tests/ # Transpiler tests
├── examples/              # Example Nanyx programs
│   ├── fizzbuzzbaz.nyx   # FizzBuzz variant with configurable rules
│   ├── todo-web-app.nyx  # Todo application example
│   ├── stringTransformation.nyx # String casing transformations
│   └── ...               # Many more examples
├── docs/                  # Documentation website (Next.js)
│   └── src/app/docs/     # MDX documentation pages
├── docs-md/              # Additional documentation
├── llm/                  # Language specification for LLMs
├── extension/            # VS Code extension
└── README.md             # This file
```

### Compiler Components

- **Nyx.Parser**: Parses `.nyx` source files into an Abstract Syntax Tree (AST)
- **Nanyx.Compiler**: Performs type inference and type checking using Hindley-Milner algorithm
- **Nyx.Transpiler.JS**: Transpiles type-checked AST to JavaScript
- **Nanyx.Compiler.Cli**: Command-line tool for compiling Nanyx programs

## Building from Source

### Requirements

- .NET SDK 9.0 or later (for parser tests)
- .NET SDK 10.0 or later (for compiler and transpiler)

### Build Instructions

1. Clone the repository:
   ```bash
   git clone https://github.com/Richiban/nyx.git
   cd nyx
   ```

2. Build the entire solution:
   ```bash
   cd compiler
   dotnet build Nanyx.Compiler.sln
   ```

3. Run all tests:
   ```bash
   dotnet test Nanyx.Compiler.sln
   ```

   Expected output: All 56+ tests should pass, including:
   - Parser unit tests (literals, identifiers, functions, lambdas, operators)
   - Parser integration tests (full file parsing)
   - Compiler tests (type inference, type checking)
   - Transpiler tests (JavaScript code generation)

## Running the Compiler

### Command-Line Interface

The Nanyx compiler CLI can parse, type-check, and report on `.nyx` files:

```bash
# Basic usage
dotnet run --project compiler/Nanyx.Compiler.Cli/NyxCompiler.Cli.fsproj myfile.nyx

# From the compiled binary
cd compiler/Nanyx.Compiler.Cli/bin/Debug/net10.0
./NyxCompiler.Cli myfile.nyx
```

The compiler will output:
- Current compilation phase
- Any type errors or warnings
- Inferred types for definitions
- Compilation success/failure status

### Example Compilation

Given `example.nyx`:
```nanyx
module Example

def add: (int, int) -> int = { x, y -> x + y }
def result = add(5, 10)
```

Run the compiler:
```bash
dotnet run --project compiler/Nanyx.Compiler.Cli/NyxCompiler.Cli.fsproj example.nyx
```

Output:
```
Phase: Typed
result: int
```

## Examples

The `examples/` directory contains numerous Nanyx programs demonstrating various features:

### FizzBuzzBaz - Configurable FizzBuzz

```nanyx
def numberGame: (Map(int, string)) -> (int -> string) = { rules ->
  { i ->
    rules
      \flatMap(playNumber(i))
      \String.join
      \match
        | "" -> i.toString()
        | s -> s
  }
}

def fizzbuzz = numberGame(dict {
  3 => "fizz"
  5 => "buzz"
  7 => "baz"
})
```

### Caesar Cipher with Frequency Analysis

```nanyx
def freqs: string -> list(float) = { s ->
  def lowers = list('a', 'z')
  def occurs = lowers \map { c -> s \count(c \string) }
  def total = occurs \sum
  occurs \map { i -> percent(i, total) }
}

def uncaesar: string -> string = { s ->
  def table = freqs(s)
  def chitab = [0..25] \map { n ->
    chisqr(table \rotate(n), english)
  }
  def shift = chitab \indexOf { == chitab \min } \negate
  s \encode(shift)
}
```

### String Case Transformations

```nanyx
type Casing = #camel | #pascal | #kebab | #snake | #title

def rejoin: (list(string), Casing) -> string = { words, casing ->
  match casing
    | #kebab -> words \map { .toLowerCase() } \String.join('-')
    | #snake -> words \map { .toLowerCase() } \String.join('_')
    | #pascal -> words \map { .capitalize() } \String.join
    | #camel -> 
        def [first, ...rest] = words
        [first.toLowerCase(), ...rest \map { .capitalize() }] \String.join
}
```

### Context-Based Effect Handlers

```nanyx
context type State(a) = (
  get: () -> a
  set: a -> ()
)

def sumdown: <State(int)> int -> int = { sum = 0 ->
  def i = get()
  if i <= 0 -> sum
  else ->
    set(i - 1)
    sumdown(sum + i)
}

def runSumdown = {
  state(10) {  -- Initialize state with 10
    sumdown()  -- Returns 55 (10+9+8+...+1)
  }
}
```

## Design Principles

Nanyx is built on a foundation of carefully considered design principles:

### 🎯 **Correctness Above All**
The language prioritizes correctness over convenience. Static type checking, exhaustive pattern matching, and no null values ensure fewer runtime errors.

### 🧘 **Simple ≠ Easy**
Nanyx favors conceptual simplicity over ease of initial use. The language may take longer to learn, but its consistency and orthogonality pay dividends over time.

### 📣 **Inform, Don't Block**
Warnings are treated as errors in release builds, but debug builds can still execute. This maintains a tight feedback loop while ensuring production code is warning-free.

### 🔄 **Everything is an Expression**
No statements, only expressions. If-then-else, match, and blocks all return values, making the language more composable.

### 🧪 **Separate Pure and Impure Code**
The context system cleanly separates pure functions from effectful computations, making code easier to reason about and test.

### 📖 **Principle of Least Surprise**
Sane defaults, consistent syntax, and predictable behavior. When there's no obvious default, the programmer must be explicit.

### 🔐 **No Global State**
No global variables or shared mutable state. All state must be explicitly threaded through the program or managed via contexts.

### 🚫 **No Null, No Reflection, No Implicit Coercion**
Modern best practices: use Option types instead of null, no runtime reflection, and no automatic type conversions.

### 📝 **Local Type Inference**
Hindley-Milner type inference means you rarely write types, but exported functions require signatures for documentation and API stability.

### 🎨 **Syntax Mirrors Semantics**
Type syntax mirrors value syntax: `f(a, b)` for values, `F(A, B)` for types. Consistency makes the language easier to learn.

For the complete list of design principles, see [`docs-md/50.principles.md`](docs-md/50.principles.md).

## Development Status

Nanyx is under active development. Current status:

✅ **Completed:**
- Core parser with full language syntax
- Abstract Syntax Tree (AST) representation
- Hindley-Milner type inference
- Type checker with generics
- Pattern matching type checking
- JavaScript transpiler
- Basic CLI compiler
- Comprehensive test suite (56+ tests)
- Example programs
- Language documentation

🚧 **In Progress:**
- Standard library implementation
- Enhanced error messages
- Documentation website
- VS Code extension improvements
- REPL (Read-Eval-Print Loop)

📋 **Planned:**
- Native code generation
- Package manager
- Build tool
- Debugger integration
- Language Server Protocol (LSP) implementation
- More comprehensive standard library
- Constraint/refinement type checking

## Contributing

Contributions to Nanyx are welcome! Here's how you can help:

1. **Report bugs**: Open an issue describing the problem and steps to reproduce
2. **Suggest features**: Propose new features or improvements via issues
3. **Write code**: Fork the repository, make changes, and submit a pull request
4. **Improve docs**: Documentation improvements are always appreciated
5. **Create examples**: Add example programs showcasing Nanyx features

### Development Workflow

1. Fork and clone the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Make your changes
4. Run tests: `dotnet test compiler/Nanyx.Compiler.sln`
5. Commit with clear messages
6. Push to your fork
7. Open a pull request

### Code Style

- Follow F# conventions for the compiler code
- Use clear, descriptive names
- Add tests for new features
- Update documentation as needed

## Resources

- **Documentation**: [`docs/`](docs/) - Next.js-based documentation site
- **Language Spec**: [`llm/README.md`](llm/README.md) - Full language specification
- **Examples**: [`examples/`](examples/) - Sample Nanyx programs
- **Compiler README**: [`compiler/README.md`](compiler/README.md) - Detailed compiler documentation
- **Design Principles**: [`docs-md/50.principles.md`](docs-md/50.principles.md) - Language design philosophy

## License

This project is open source. See the LICENSE file for details.

---

**Nanyx** - A language for clear, composable workflows.

Built with ❤️ in F#
