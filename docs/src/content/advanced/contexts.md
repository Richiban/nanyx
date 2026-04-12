---
title: "Contexts"
description: "Implicit parameters for effects and typeclasses"
order: 2
---

Contexts are implicit parameters in Nanyx. A function can declare the contexts it needs, and the members of those contexts become available inside its body. This is how Nanyx models effects, state, and typeclass-style constraints.

## Declaring a context

A context definition looks like a record type, but uses the `context` keyword:

```nanyx
context Console = (
  println: string -> ()
)
```

## Requiring a context

A function can specify required contexts in its type signature. Here, `sayHello` can call `println` because it runs in `$Console`:

```nanyx
def sayHello: [$Console] () -> () = {
  println("Hello, world!")
}
```

## Loading a context

Use the `use` keyword to load an instance of a context into scope. Once loaded, any functions that require it can be called:

```nanyx
def main = {
  use Console(
    println = { message -> hostPrint(message) }
  )

  sayHello()
}
```

You can also load a context for a single expression with `use ... in`:

```nanyx
def result = use Console(println = hostPrint) in {
  println("Hello from a scoped context")
}
```

## Contexts are part of the type

A value's context requirement is part of its type. You can annotate just the contexts and let the rest infer:

```nanyx
def greet: [$Console] = {
  println("Hi")
}
```

This is also how scoped mutation is enforced: values that still require `memory` cannot escape into code that does not provide that context. See [Memory: Values cannot escape memory scope](../advanced/memory#values-cannot-escape-memory-scope).

For a formal proposal of type-level context requirements, see [Specifications: Context-qualified types](../specifications/context-qualified-types).

## Combining contexts

You can require multiple contexts by combining them:

```nanyx
def readAndPrintFile
  : [Console & FileSystem] string -> ()
  = { path ->
    def contents = openFile(path).contents
    println(contents)
  }
```

## Console by default

The program entrypoint runs in `Console`, which is why `println` is available in `main` without extra setup.

## Contexts as typeclasses

Contexts can also express typeclass-style constraints. Define a context with the required operations and use it as a constraint:

```nanyx
context Sum(a) = (
  `0`: a
  `+`: a, a -> a
)

export def sum: [Sum(a)] list(a) -> a
  = { items -> items \fold(`0`) { + } }
```

For ergonomic generic API design with associated members and constrained capabilities, see [Associated types and constraints](./associated-types-and-constraints).

## Context synthesis using attached definitions

When a function requires a context, you don't always have to load or even create it manually. If the type in question has [attached definitions](./attached-definitions) that match the context's members, the compiler can **synthesize** the context automatically.

For example, given a `Sum` context:

```nanyx
context Sum(a) = (
  `0`: a
  `+`: a, a -> a
)
```

And a type with matching attached definitions:

```nanyx
type Point = (x: int, y: int)

def Point.`0` = Point(x = 0, y = 0)

def Point.`+` = { a, b ->
  Point(x = a.x + b.x, y = a.y + b.y)
}
```

You can call `sum` on a `list(Point)` without explicitly loading a `Sum(Point)` context -- the compiler sees that `Point.``0``` and `Point.``+``` satisfy the context and synthesizes it:

```nanyx
def points = [Point(1, 2), Point(3, 4), Point(5, 6)]

def total = sum(points)
-- total = Point(x = 9, y = 12)
```

This is analogous to how Haskell derives typeclass instances or how Rust resolves trait implementations. The key difference is that in Nanyx the "instance" is just a collection of attached definitions -- there is no separate declaration required.

### When synthesis applies

Context synthesis occurs when:

- A function requires a context parameterized by a concrete type
- That type has attached definitions matching every member of the context
- The types of those definitions are compatible with the context's signatures

If any member is missing or has an incompatible type, synthesis fails and the compiler requires you to load the context explicitly.

### Explicit loading takes priority

If you manually load a context with `use`, that instance is always used, even if attached definitions exist. This lets you override the default behavior when needed:

```nanyx
def reverseSum = {
  use Sum(Point)(
    `0` = Point(x = 0, y = 0)
    `+` = { a, b -> Point(x = a.x - b.x, y = a.y - b.y) }
  )

  sum(points)
}
```

## Why contexts matter

- Effects are explicit: if a function touches IO or state, it must declare a context.
- You can swap implementations by loading different context instances.
- They enable typeclass-style generic programming without extra syntax.
