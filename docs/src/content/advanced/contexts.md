---
title: "Contexts"
description: "Implicit parameters for effects and typeclasses"
order: 2
---

Contexts are implicit parameters in Nanyx. A function can declare the contexts it needs, and the members of those contexts become available inside its body. This is how Nanyx models effects, state, and typeclass-style constraints.

## Declaring a Context

A context definition looks like a record type, but uses the `context` keyword:

```nanyx
context Console = (
  println: string -> ()
)
```

## Requiring a Context

A function can specify required contexts in its type signature. Here, `sayHello` can call `println` because it runs in `@Console`:

```nanyx
def sayHello: [@Console] () -> () = {
  println("Hello, world!")
}
```

## Loading a Context

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

## Contexts Are Part of the Type

A value's context requirement is part of its type. You can annotate just the contexts and let the rest infer:

```nanyx
def greet: [@Console] = {
  println("Hi")
}
```

## Combining Contexts

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

## Contexts as Typeclasses

Contexts can also express typeclass-style constraints. Define a context with the required operations and use it as a constraint:

```nanyx
context Sum(a) = (
  `0`: a
  `+`: a, a -> a
)

export def sum: [Sum(a)] list(a) -> a
  = { items -> items \fold(`0`) { + } }
```

## Why Contexts Matter

- Effects are explicit: if a function touches IO or state, it must declare a context.
- You can swap implementations by loading different context instances.
- They enable typeclass-style generic programming without extra syntax.
