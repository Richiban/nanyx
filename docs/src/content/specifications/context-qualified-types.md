---
title: "Context-qualified types"
description: "Type-level context requirements for values and functions"
order: 1
---

This page defines the proposed syntax and semantics for attaching context requirements to any type, not just function definitions.

## Motivation

Today, context requirements are most visible on function signatures, for example:

```nanyx
def f: [C] (x, y) -> z
```

To support values like `MArray` and `MRef` safely, context requirements need to be expressible for value types as well.

## Syntax

Use the same syntax form already used by function signatures:

```nanyx
type MyType = [C] (int, string)
```

This means:
- The underlying shape is `(int, string)`.
- Values of `MyType` require context `C` to be used.

This keeps signatures and aliases aligned: any type expression from a signature can be extracted into a `type` alias unchanged.

## Core semantics

`[C] T` means a value of type `T` with required context `C`.

Requirements are part of the type and flow with the value:
- Assigning, returning, or storing values preserves requirements.
- Combining values unions requirements.
- A value can only be used where all its required contexts are available.

## Scope and escape

A scoped context can discharge its own requirement inside the scope, but not outside it.

```nanyx
def bad = {
  memory {
    def arr = MArray.of([1, 2, 3])
    arr
  }
}
```

The example above is rejected because the result still requires `$Memory` and escapes the `memory` scope.

In the standard library naming, `$Memory` is the context and `memory { ... }` is the helper that creates that context for a scope.

Converting to a context-free value before returning is valid:

```nanyx
def good = {
  memory {
    def arr = MArray.of([1, 2, 3])
    MArray.toList(arr)
  }
}
```

## Generality

This mechanism is intentionally generic and applies to any context, not only `$Memory`:
- mutable memory capabilities,
- IO capabilities,
- transaction or resource capabilities,
- user-defined contexts.

## Design notes

- Prefer wording like "requires context" over "can only exist".
- Context requirements should be tracked uniformly by type inference and assignability.
- Escape analysis for mutable values then falls out of standard type checking.
