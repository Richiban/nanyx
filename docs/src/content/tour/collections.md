---
title: Collections
description: Lists, maps, sets, and mutability
order: 8
---

Collections are typically immutable in Nanyx, although [it is possible to use and create mutable versions](../advanced/memory). Mutable versions live in `nanyx/collections.mutable` and, by convention, have an `M` prefix in their name (for example, `MList` vs `List`).

## Immutable vs mutable

Immutable collections are persistent data structures, while mutable collections are optimized for in-place updates.

```nanyx
-- Immutable list

def numbers = [1, 2, 3]

-- Mutable list (requires $Memory context)

def addItems = {
  memory {
    def items: MList(int) = []
    items += 1
    items += 2
    MList.toList(items)
  }
}
```

## Common collection abstractions

Most of the time, Nanyx programs don't care very much about the specific implementation of a collection, just that it supports certain operations. For this reason, the standard library provides a number of common collection abstractions that can be implemented by any type:

| Collection type | Special alias | Use case |
|-----------------|-------------------|---------|
| `List(a)` | `list(a)` | Indexable collections |
| `Set(a)` | - | Membership checks |
| `Map(k, v)` | `k -> v` | Key/value lookup |
| `Sequence(a)` | `seq(a)` | Finite, deterministic sequences |
| `Generator(a)` | `gen(a)` | Potentially infinite or non-deterministic sequences |

```nanyx
def lookup = ["a" => 1, "b" => 2]

def unique: Set(int) = [1, 2, 2, 3]
```

## Syntax

Nanyx supports a generalisable literal syntax for all collection types; the square bracket form is available to any target type that implements the correct builder context:

```nanyx
context ListBuilder(a) = (
  def yield: a -> ()
)

def MyList.apply: ([ListBuilder(a)] () -> a) -> MyList(a) = { f ->
  memory {
    def list = mut MyList()

    use (
      yield = { element -> list := list \add(element) }
    )

    f(builder)

    list.value
  }
}

-- The square bracket literal syntax is now available for type MyList
def myList: MyList(int) = [1, 2, 3]
```

```nanyx
context MapBuilder(k, v) = (
  def `=>`: (k, v) -> ()
)

def MyMap.apply: ([MapBuilder(a)] () -> a) -> MyMap(a) = { f ->
  memory {
    def map = mut MyMap(string, int)()
    
    use (
      `=>` = { k, v -> map := map \ add(k, v) }
    )

    f(builder)

    builder.build() -- returns MyMap(string, int)
  }
}

-- The square bracket literal syntax is now available for type MyMap
def myMap: MyMap(string, int) = ["a" => 1, "b" => 2]
```

This works because the square bracket syntax desugars to a call to the `apply` function on the target type, which provides the builder context that defines how the literal should be constructed:

```nanyx
def myList: MyList(int) = [1, 2, 3]
-- Desugars to:
def myList: MyList(int) = MyList.apply {
  yield(1)
  yield(2)
  yield(3)
}
```