---
title: "Contexts vs interfaces"
description: "A practical guide for interface-first developers"
order: 3
---

If you are coming from C#, Java, or TypeScript, Nanyx contexts can feel unfamiliar at first.

A good starting point is: a context is like an interface constraint, but passed implicitly and usable for both typeclass-style capabilities and effects.

# The short version

- **Interfaces** describe object members and are usually implemented explicitly by a type.
- **Contexts** describe required operations and are supplied by `use` or synthesized from attached definitions.
- **Function signatures** declare context requirements directly, so dependencies are explicit in types.

# Mental model for interface users

In interface-heavy languages, you might write:

```text
sum<T>(items: List<T>) where T : IAddable<T>
```

In Nanyx, you model that as a context constraint:

```nanyx
context Sum(a) = (
  `0`: a
  `+`: (a, a) -> a
)

def sum: [Sum(a)] list(a) -> a = { items ->
  items \fold(`0`) { + }
}
```

The function says exactly what it needs (`Sum(a)`), not which concrete type hierarchy it depends on.

# Where contexts differ most

## 1) Implicit provisioning

You can provide implementations with `use`:

```nanyx
use Sum(int)(
  `0` = 0
  `+` = { a, b -> a + b }
)

sum([1, 2, 3])
```

This is closer to passing a capability dictionary than calling methods on an instance.

## 2) Can model effects too

Contexts are not only for generic constraints; they also model effects like I/O:

```nanyx
context Console = (
  println: string -> ()
)

def greet: [Console] string -> () = { name ->
  println("Hello, {name}")
}
```

## 3) One pattern for many problems

The same mechanism (`[Context(...)]`) handles:

- effect requirements (`Console`, `FileSystem`, `Memory`)
- generic capabilities (`Sum(a)`, `Iter(a)`, etc.)

# Contexts vs interfaces: when to choose what

Use **contexts** when you want:

- behavior selected by capability rather than inheritance
- explicit effect requirements in function types
- generic algorithms over operations (`+`, `next`, `length`, etc.)

Use **records + attached definitions** when you want:

- object-like data with discoverable namespaced behavior
- method-style ergonomics at call sites

In practice, these are complementary: records model data, attached definitions provide behavior, and contexts express what generic/effectful functions require.

# Migration tips

- Start by translating interface constraints to context constraints in generic functions.
- Keep data models as records; avoid recreating deep inheritance trees.
- Prefer small capability contexts (`Iter`, `Sized`, `Sum`) over large “god interfaces.”
- Treat context requirements as part of the API contract.

For full details on contexts, see [Contexts](../advanced/contexts).
