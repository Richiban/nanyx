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

-- Mutable list (requires @Memory context)

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

## Syntax

Nanyx supports a generalisable literal syntax for all collection types; the square bracket form is available to any type that implements the correct builder context:

```nanyx
context ListBuilder(a) {
  def yield: element -> ()
}

def MyList.apply = { f ->
  memory {
    def list = mut MyList()

    use (
      def yield = { element -> list := list \add(element) }
    )

    f(builder)

    list.value
  }
}

def myList: MyList(string, int) = [1, 2, 3]
```

```nanyx
context MapBuilder(a) {
  def empty: a
  def `=>`: (k, v) -> ()
}

def myMapBuilder: [Memory] MapBuilder(MyMap(string, int)) = {
  def empty = { MyMap(string, int) {} }
  def `=>` = {(m, (k, v)) -> m \ (k => v)}
}

def MyMap.apply = { f ->
  memory {
    def builder = myMapBuilder

    f(builder)

    builder.build() -- returns MyMap(string, int)
  }
}

def myMap: MyMap(string, int) = ["a" => 1, "b" => 2]
```