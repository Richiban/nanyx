---
title: "Tags"
description: "Literal tags and tag unions"
order: 3
---

Tags are a foundational feature in Nanyx, somewhat similar to symbols/atoms in Ruby and Erlang or string literal types in TypeScript and Scala.

A tag is both a value and a type. The tag name identifies the exact value, which makes tags useful for precise modeling and pattern matching.

# Tag naming rules

Tag naming has both conventions and requirements:

- **Recommended for ordinary tags:** use lower-case tag names such as `#ok`, `#error`, `#readwrite`.
- **Required for custom-pattern result tags:** tags must start with an uppercase letter after `#` (for example `#Hsl`, `#ParseInt`).

For the full custom-pattern model and examples, see [Custom patterns](../advanced/custom-patterns).

# Tags as values and types

You can use tags directly as values:

```nanyx
def x = #myTag
```

You can also annotate with a specific tag type:

```nanyx
def y: #mySecondTag = #mySecondTag
```

# Tag unions

Single tags are most useful when combined into unions. A tag union is a type that can be one of several tagged cases:

```nanyx
type Color = #red | #green | #blue
```

Using plain tags gives you an enum-like shape (while remaining structural).

# Payloads

Tags can also carry payload data, which makes them equivalent to discriminated unions:

```nanyx
type Color =
  | #red
  | #green
  | #blue
  | #custom(string)
```

In type syntax, a tag with payload is written `#tagName(payload)`. The payload can be any type, including a record or another tag union:

```nanyx
type Shape =
  | #circle(float)
  | #rectangle(width: float, height: float)
```

# Tags for function flags

Tag unions are great for readable flags and mode parameters. They make legal options explicit in the function type:

```nanyx
def openFile: string, (#read | #readwrite) -> File
  = { fileName, mode ->
    ...
  }

def file = openFile("file.txt", #readwrite)
```

# Polymorphic tag unions

Because tag unions are structural, functions can be written that require specific cases while allowing unknown cases to pass through. This is a common pattern for reusable APIs.

For example, the `nanyx/option` module contains a `map` function that transforms only the `#some` case of a given union type and preserves everything else:

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

# Runtime behavior

Despite their similarity to string literal types, tags are **not** strings at runtime. A tag is its own runtime value kind, so `#ok` is not the same thing as `"ok"`.

That said, converting a tag to text gives the expected name:

```nanyx
def t = #readwrite

t.toString()   -- "#readwrite"
```

This is useful for logging and debugging, but it does not change the underlying runtime type of the value.
