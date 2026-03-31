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

memory {
  def x = mut 0 -- OK
  x := 1
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

## No global mutable state

Because `memory` is scoped, global mutable state is effectively impossible. Top-level `mut` values fail to compile.
