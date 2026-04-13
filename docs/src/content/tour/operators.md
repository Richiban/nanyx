---
title: "Operators"
description: "Operators as functions"
order: 14
---

Operators are ordinary functions with special names. They can be used infix, prefix, postfix, or passed around like any other function.

# Defining operators

You define operators by naming a function with a valid operator name, encased in backticks:

```nanyx
def `+++`: string, string -> string = { a, b -> a + b }

def r = "one" +++ "two"
```

Operators can take more than two arguments. Extra arguments are passed as a record:

```nanyx
def `+?` = { a, b, c -> ... }

def r = a +? (b, c)
```

# Arithmetic and comparison

Core arithmetic (`+`, `-`, `*`, `/`, `%`, `**`) and comparison (`==`, `!=`, `<`, `<=`, `>`, `>=`) are functions provided by contexts. You can overload them by supplying a context.

```nanyx
context VectorOps = (
  `+`: Vector, Vector -> Vector
)

use vectorOps in a + b
```

# Pipe and composition

Piping uses `\`, and composition uses `>>` and `<<`:

```nanyx
def normalize = trim >> toLowerCase

def result = data
  \filter { .isActive }
  \map { .name }
  \sort
```

# Valid operator names

The following symbols are valid operator names: `+`, `-`, `*`, `/`, `%`, `**`, `=`, `!`, `<`, `>`, `&`, `|`, `^`, `~`, `?`. They can be repeated, but the whole name must consist only of characters from the above list. For example, `++` and `>>>` are valid operator names, but `+a` and `` are not. Note: some operator names are reserved by the language, such as `=`.