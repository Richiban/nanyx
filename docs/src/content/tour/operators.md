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

Operators can take more than two arguments. Extra arguments are passed as a record, [in the same way as piping](../tour/piping#piping-with-extra-arguments):

```nanyx
def `+?` = { a, b, c -> ... }

def r = a +? (b, c)
```

# Using operators as functions

Since operators are just specially-named functions, you can also use them in a non-operator way:

```nanyx
def r1 = "one" +++ "two"
def r2 = `+++`("one", "two")
def f = `+++`
```

Also, lambda syntax supports an [operator-based shorthand](../tour/functions#shorthand-lambdas) for using operators:

```nanyx
[1, 2, 3] \fold(0) { + }
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