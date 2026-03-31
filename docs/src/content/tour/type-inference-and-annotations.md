---
title: "Type inference and annotations"
description: "How Nanyx infers types and when to annotate"
order: 3
---

Type inference is foundational in Nanyx. The compiler infers types from values and usage, but annotations are recommended for exported definitions.

## Inference by value

```nanyx
def message = "Hello world" -- inferred as string

def message: string = "Hello world" -- explicitly annotated
```

## Inference for functions

```nanyx
def add = { a, b -> a + b }
-- inferred as (int, int) -> int
```

## Exported values

Exported values should be annotated:

```nanyx
export def add = { a, b -> a + b }
-- Compiler warning: missing type annotation

export def add: Nat, Nat -> Nat = { a, b -> a + b }
```

## Target typing

Nanyx can infer types based on how values are used:

```nanyx
def parse: string -> a = { json -> ... }

def obj = parse("{}")

def person: Person = obj
```

You can also use assertions to guide inference:

```nanyx
def obj = parse("..."): Person
```

## Specs

Specs let you write a type signature separately from the definition:

```nanyx
spec add: (int, int) -> int

def add(x, y) -> x + y
```
