---
title: "Variables"
description: "Def bindings and immutability in Nanyx"
order: 1
---

In Nanyx, variables are immutable. You declare them using the `def` keyword.

## Def bindings

```nanyx
def name = "Nanyx"
def version = 4
def pi = 3.14159
```

Once a value is bound, it cannot be reassigned:

```nanyx
def x = 10
x = 20  -- ✗ Compile error: cannot reassign a value
```

> **Mutation:** In Nanyx mutation is considered an advanced topic and makes use of a context called `@Memory`. See [Memory](./memory.md) for more on how mutation works and how to use it safely.

## Type annotations

Nanyx can infer types, but you can add annotations for clarity:

```nanyx
def name: string = "Nanyx"
def count: int = 42
def ratio: float = 0.75
def active: bool = true
```

## Blocks

A block expression returns the value of its last expression:

```nanyx
def result =
  def a = 10
  def b = 20
  a + b

-- result is 30
```
