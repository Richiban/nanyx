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

A function can specify required contexts in its type signature. Here, `sayHello` can call `println` because it runs in `@Console`:

```nanyx
def sayHello: [@Console] () -> () = {
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

## Contexts Are part of the type

A value's context requirement is part of its type. You can annotate just the contexts and let the rest infer:

```nanyx
def greet: [@Console] = {
  println("Hi")
}
```

This is also how scoped mutation is enforced: values that still require `memory` cannot escape into code that does not provide that context. See [Memory: Values cannot escape memory scope](./memory.md#values-cannot-escape-memory-scope).

For a formal proposal of type-level context requirements, see [Specifications: Context-qualified types](../specifications/context-qualified-types.md).

## Combining contexts

You can require multiple contexts by combining them:

```nanyx
def readAndPrintFile
  : [Console + FileSystem] string -> ()
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

## Why contexts matter

- Effects are explicit: if a function touches IO or state, it must declare a context.
- You can swap implementations by loading different context instances.
- They enable typeclass-style generic programming without extra syntax.
