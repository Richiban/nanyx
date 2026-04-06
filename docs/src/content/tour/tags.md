---
title: "Tags"
description: "Literal tags and tag unions"
order: 3
---

Tags are a foundational feature in Nanyx, somewhat similar to symbols in Ruby or atoms in Erlang.

A tag's name is its value and also its type.

## Tags as values and types

```nanyx
def x = #myTag

def y: #mySecondTag = #mySecondTag
```

## Tag unions

Tags don't seem to be much use on their own but, unlike other types in Nanyx, they can be combined into unions: a powerful way to model data. A tag union is a type that can be one of several tags:

```nanyx
type Color = #red | #green | #blue
```

Using plain tags creates what other language might call an enum (although they are structural).

Tags can also carry data meaning they can also emulate discriminated unions:

```nanyx

type Color =
  | #red
  | #green
  | #blue
  | #custom(string)
```

## Tags for function flags

Tag unions are great for readable flags:

```nanyx
def openFile: string, (#read | #readwrite) -> File
  = { fileName, mode ->
    ...
  }

def file = openFile("file.txt", #readwrite)
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
