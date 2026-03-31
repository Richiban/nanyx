---
title: "Tags"
description: "Literal tags and tag unions"
order: 7
---

Tags are a foundational feature in Nanyx. A tag's name is its value and its type. Tags are similar to symbols in Ruby or atoms in Erlang.

## Tags as values and types

```nanyx
def x = #myTag

def y: #mySecondTag = #mySecondTag
```

## Tag unions

Tags are most useful in tag unions, where they act like enums or discriminated unions:

```nanyx
type Color = #red | #green | #blue

type Color =
  | #red
  | #green
  | #blue
  | #custom(string)
```

## Tags for function flags

Tag unions are great for readable flags:

```nanyx
def openFile
  : string, (#read | #readwrite) ->
  = { fileName, mode ->
    ...
  }

openFile("file.txt", #readwrite)
```

## Polymorphic tag unions

Because tag unions are structural, you can write functions that require specific tags but allow others to pass through. This is how `Option.map` works:

```nanyx
def Option.map
  : (#some(a) | rest), (a -> b) -> (#some(b) | rest)
  = { | #some(a), f -> #some(f(a))
      | other, _ -> other }
```

This preserves non-success cases:

```nanyx
def x: (#some(string) | #notFound) = ...
def y: (#some(int) | #divideByZero) = ...

x \Option.map { .length }
y \Option.map { \Math.abs }
```
