---
title: "Memory"
description: "Scoped mutation with the memory context"
order: 5
---

Mutation in Nanyx is available but bounded. You must open a `memory` context to create mutable variables or use mutable collections. This allows internal mutation while keeping external interfaces pure.

## Local mutation

```nanyx
def sort: [Ord(a)] list(a) -> list(a) = { l ->
  memory {
    def arr = MArray.of(l)
    MArray.sort(arr)
    MArray.toList(arr)
  }
}
```

## Mutability requires context

```nanyx
def main = {
  def x = mut 0 -- Error: requires memory context
}

def main = {
  memory {
    def x = mut 0 -- OK
    x := 1
  }
}
```

## Mutable collections

```nanyx
memory {
  def items: MList(int) = []
  items += 1
  items := items ++ [2, 3]
}
```

## Values cannot escape memory scope

Values that require the `memory` context cannot be returned out of a `memory { ... }` scope.

```nanyx
def bad = {
  memory {
    def arr = MArray.of([1, 2, 3])
    arr -- Error: value requires memory context and cannot escape this scope
  }
}
```

To return data, convert it to an immutable value before leaving the scope:

```nanyx
def good = {
  memory {
    def arr = MArray.of([1, 2, 3])
    MArray.toList(arr) -- OK
  }
}
```

This is how Nanyx performs escape analysis. Instead of a separate escape-analysis pass, the compiler tracks context requirements in types. If a value's type still needs `memory`, it can only be used where `memory` is available, so it cannot leak into pure code.

For the detailed proposal, see [Specifications: Context-qualified types](../specifications/context-qualified-types.md).

For the general model behind this, see [Contexts: Contexts Are Part of the Type](./contexts.md#contexts-are-part-of-the-type).

## No global mutable state

Because `memory` is scoped, global mutable state is effectively impossible. Top-level `mut` values will fail to compile because a top-level definition cannot be inside a context.
