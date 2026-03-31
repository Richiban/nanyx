---
title: "Collections"
description: "Lists, maps, sets, and mutability"
order: 8
---

Collections are immutable by convention in Nanyx. Mutable versions live in `nanyx/collections/mut` and use a `Mut` prefix (for example, `MList` vs `List`).

## Immutable vs mutable

Immutable collections are persistent data structures, while mutable collections are optimized for in-place updates.

```nanyx
-- Immutable list

def numbers = [1, 2, 3]

-- Mutable list (requires memory context)

def addItems = {
  memory {
    def items: MList(int) = []
    items += 1
    items += 2
    items
  }
}
```

## Common collection types

- `list(a)` for indexed lists
- `set(a)` for membership checks
- `map(k, v)` for key/value lookup
- `series(a)` for finite sequences
- `seq(a)` for potentially infinite sequences

```nanyx
def lookup = ["a" => 1, "b" => 2]

def unique = set [1, 2, 2, 3]
```
