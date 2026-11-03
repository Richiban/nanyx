# Nanyx

Nanyx is a statically-typed, functional-first programming language with Hindley-Milner type inference, structural records and tag unions, context-based effects, and a pipeline operator that keeps data flow reading left to right. It compiles to WebAssembly.

This README is a quick feel for the language. The full documentation lives on the [docs site][docs].

```nanyx
module main

type Shape =
  | #circle(radius: float)
  | #rectangle(width: float, height: float)

def area: Shape -> float = {
  | #circle(radius) -> 3.14159 * radius * radius
  | #rectangle(width, height) -> width * height
}

@entry
def main = {
  [#circle(radius = 1.0), #rectangle(width = 2.0, height = 3.0)]
    \map(area)
    \sum
    \println
}
```

## A short tour

### Values and functions

Everything is an expression and every binding is immutable. Braces always mean a function; a function takes exactly one argument, and "multiple parameters" are a record being destructured.

```nanyx
def name = "Nanyx"
def double = { x -> x * 2 }
def add: (int, int) -> int = { x, y -> x + y }

def greet = { name, greeting = "Hello" ->
  "{greeting}, {name}!"
}
```

Types are inferred almost everywhere; annotations are for exported definitions and clarity. [Functions][functions] · [Type inference][inference]

### Pipes

`\` sends the value on the left into the first argument of the function on the right, so a chain reads in the order it runs. Shorthand lambdas cover the common cases.

```nanyx
def adultNames = users
  \filter { .age >= 18 }
  \map { .name }
  \sortBy { .length }

def total = [1, 2, 3] \fold(0) { + }
```

[Piping][piping] · [Higher-order functions][lambdas]

### Records and tuples

Records are structural. A tuple is just a record whose fields are positional, and the two can mix. Updates are non-destructive.

```nanyx
type Person = (name: string, age: int, email?: string)

def alice = Person(name = "Alice", age = 30)
def older = alice with age = 31
def (x, y) = (10, 20)
```

[Records and tuples][records]

### Tags and pattern matching

A tag like `#notFound` is both a value and its own type. Tags combine into unions, carry payloads, and are matched exhaustively. There is no null and no single `Option` type: any union containing `#some(a)` is "option-like", and the standard library's helpers preserve whatever other tags are present.

```nanyx
def findUser: UserId -> #some(User) | #notFound | #error(DbError) = { id -> ... }

match findUser(id)
  | #some(user) -> println("Found {user.name}")
  | #notFound -> println("No such user")
  | #error(e) -> println("Database error: {e}")

-- Transform only the #some case; #notFound and #error pass through
def name = findUser(id) \Option.map { .name }
```

Patterns nest, bind, and can use arbitrary predicates in place of guards:

```nanyx
def describe: int -> string = {
  | 0 -> "zero"
  | n & { < 0 } -> "negative: {n}"
  | _ -> "positive"
}
```

[Tags][tags] · [Pattern matching][patterns] · [Error handling][errors]

### Contexts

A context is an implicit parameter. Functions declare the contexts they need in their type, which makes effects visible and lets the same mechanism express typeclass-style constraints. `use` supplies an instance; when a type has matching attached definitions, the compiler synthesises one.

```nanyx
context Console = (println: string -> ())

def greet: [Console] string -> () = { name ->
  println("Hello, {name}!")
}

context Sum(a) = (`0`: a, `+`: (a, a) -> a)

def sum: [Sum(a)] list(a) -> a = { items -> items \fold(`0`) { + } }
```

[Contexts][contexts] · [Contexts vs interfaces][contexts-vs-interfaces]

### Attached definitions

There are no classes. Behaviour is attached to a type by name, and `x.f`, `x \f` and `T.f(x)` all resolve to the same definition. Operators are attached definitions too.

```nanyx
type Point = (x: int, y: int)

def Point.length = { p -> Math.sqrt(p.x ** 2 + p.y ** 2) }
def Point.`+` = { a, b -> Point(x = a.x + b.x, y = a.y + b.y) }

def len = (Point(x = 3, y = 4) + Point(x = 0, y = 0)).length
```

[Attached definitions][attached] · [Operators][operators] · [Classes, for OOP readers][classes]

### Scoped mutation

Values are immutable by default. Mutation requires the `$Memory` context, obtained with `memory { }`, and a mutable value cannot escape that scope, so a function can be imperative inside and pure outside.

```nanyx
def sort: [Ord(a)] list(a) -> list(a) = { l ->
  memory {
    def arr = MArray.of(l)
    MArray.sort(arr)
    MArray.toList(arr)
  }
}
```

[Memory][memory]

### Workflows

Libraries can add language-like constructs. A block after a builder is rewritten into continuation-passing calls on that builder, which is how `async`/`await`, result chaining with `try`, sequence builders and query DSLs are all implemented without compiler support.

```nanyx
def lastOrderTotal = proc {
  def customer = try getCustomer(5)
  def order = try getLastOrder(customer)
  return order.total
}
```

[Workflows][workflows] · [Option chaining cookbook][option-chaining]

## Getting started

- [Installation][install]
- [Hello, World!][hello]
- [Files, packages and projects][packages]
- [Standard library][stdlib]

## Design

Nanyx aims to feel small: few rules, few exceptions, and features that compose rather than special cases. The compiler emits only errors, never warnings, and a debug build still runs in the face of type errors so iteration stays fast; style and quality checks come from pluggable [analyzers][analyzers]. There is no function overloading, no currying by default, and no arbitrary unions, each for reasons written up in the docs.

[Principles][principles] · [Why no overloading?][overloading] · [Why no currying?][currying] · [Why no `string | int`?][unions]

## This repository

| Path | Contents |
|---|---|
| `compiler/` | The compiler, written in F#: parser, type checker, WebAssembly backend, CLI |
| `docs/` | The documentation site |
| `examples/` | Example Nanyx programs |
| `extension/` | VS Code extension |
| `llm/` | Design notes and specification drafts |

### Building from source

Requires the .NET SDK.

```bash
cd compiler
dotnet build Nanyx.Compiler.sln
dotnet test Nanyx.Compiler.sln
```

Nanyx is under active development and the language is still changing. Contributions, bug reports and documentation fixes are welcome.

[docs]: https://nanyx.dev/docs
[install]: https://nanyx.dev/docs/getting-started/installation
[hello]: https://nanyx.dev/docs/getting-started/hello-world
[packages]: https://nanyx.dev/docs/tour/packages
[functions]: https://nanyx.dev/docs/tour/functions
[inference]: https://nanyx.dev/docs/tour/type-inference-and-annotations
[piping]: https://nanyx.dev/docs/tour/piping
[lambdas]: https://nanyx.dev/docs/tour/lambdas-and-higher-order-functions
[records]: https://nanyx.dev/docs/tour/records
[tags]: https://nanyx.dev/docs/tour/tags
[patterns]: https://nanyx.dev/docs/tour/pattern-matching
[errors]: https://nanyx.dev/docs/tour/error-handling
[operators]: https://nanyx.dev/docs/tour/operators
[contexts]: https://nanyx.dev/docs/advanced/contexts
[attached]: https://nanyx.dev/docs/advanced/attached-definitions
[memory]: https://nanyx.dev/docs/advanced/memory
[workflows]: https://nanyx.dev/docs/advanced/workflows
[analyzers]: https://nanyx.dev/docs/advanced/analyzers
[option-chaining]: https://nanyx.dev/docs/cookbook/option-chaining
[stdlib]: https://nanyx.dev/docs/stdlib/standard-library
[principles]: https://nanyx.dev/docs/foundations/principles
[contexts-vs-interfaces]: https://nanyx.dev/docs/coming-from-other-languages/contexts-vs-interfaces
[classes]: https://nanyx.dev/docs/coming-from-other-languages/classes
[overloading]: https://nanyx.dev/docs/coming-from-other-languages/overloading
[currying]: https://nanyx.dev/docs/coming-from-other-languages/currying
[unions]: https://nanyx.dev/docs/coming-from-other-languages/arbitrary-unions
