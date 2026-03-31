---
title: "Operators"
description: "Operators as functions"
order: 14
---

Operators are ordinary functions with special syntax. They can be used infix, prefix, postfix, or passed around like any other function.

## Defining operators

You define operators by naming a function with the operator token, usually in backticks:

```nanyx
def `+++`: string, string -> string = { a, b -> a + b }

def r = "one" +++ "two"
```

Operators can take more than two arguments. Extra arguments are passed as a record:

```nanyx
def `+?` = { a, b, c -> ... }

def r = a +? (b, c)
```

## Arithmetic and comparison

Core arithmetic (`+`, `-`, `*`, `/`, `%`, `**`) and comparison (`==`, `!=`, `<`, `<=`, `>`, `>=`) are functions provided by contexts. You can overload them by supplying a context.

```nanyx
context VectorOps = (
  `+`: Vector, Vector -> Vector
)

use vectorOps in a + b
```

## Pipe and composition

Piping uses `\`, and composition uses `>>` and `<<`:

```nanyx
def normalize = trim >> toLowerCase

def result = data
  \filter { .isActive }
  \map { .name }
  \sort
```

## Assignment and mutation

`=` defines an immutable binding. Use `mut` and `set` for mutable storage.

```nanyx
mut count = 0
set count = count + 1
```
