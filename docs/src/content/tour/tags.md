---
title: "Tags"
description: "Literal tags and tag unions"
order: 3
---

Tags are a foundational feature in Nanyx, somewhat similar to symbols in Ruby or atoms in Erlang.

A tag is both a value and a type. The tag name identifies the exact value, which makes tags useful for precise modeling and pattern matching.

## Tags as values and types

You can use tags directly as values:

```nanyx
def x = #myTag
```

You can also annotate with a specific tag type:

```nanyx
def y: #mySecondTag = #mySecondTag
```

## Tag unions

Single tags are most useful when combined into unions. A tag union is a type that can be one of several tagged cases:

```nanyx
type Color = #red | #green | #blue
```

Using plain tags gives you an enum-like shape (while remaining structural).

Tags can also carry payload data, which makes them equivalent to discriminated unions:

```nanyx
type Color =
  | #red
  | #green
  | #blue
  | #custom(string)
```

This lets each case carry exactly the data it needs.

## Tags for function flags

Tag unions are great for readable flags and mode parameters. They make legal options explicit in the function type:

```nanyx
def openFile: string, (#read | #readwrite) -> File
  = { fileName, mode ->
    ...
  }

def file = openFile("file.txt", #readwrite)
```

## Polymorphic tag unions

Because tag unions are structural, you can write functions that require specific cases while allowing unknown cases to pass through. This is a common pattern for reusable APIs.

For example, `Option.map` transforms only the `#some` case and preserves everything else:

```nanyx
def Option.map
  : (#some(a) | rest), (a -> b) -> (#some(b) | rest)
  = { | #some(a), f -> #some(f(a))
      | other, _ -> other }
```

This preserves non-success cases while still transforming success values:

```nanyx
def x: (#some(string) | #notFound) = ...
def y: (#some(int) | #divideByZero) = ...

x \Option.map { .length }
y \Option.map { \Math.abs }
```

For broader union and matching patterns, see [Pattern matching](./pattern-matching) and [Types](./types).
