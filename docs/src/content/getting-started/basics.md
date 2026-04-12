---
title: "Basics"
description: "Core syntax and expressions"
order: 4
---

This page covers core syntax and expression rules to help you read and write simple Nanyx code.

## Expressions

Everything is an expression that produces a value.

```nanyx
def message = "Hello world"
```

## Definitions

Use `def` to bind a name to a value. The value can be any expression, including another definition or a function:

```nanyx
def name = "Nanyx"
def version = 4
def getMessage = { name -> "Hello, {name}!" }
```

## Blocks and indentation

Blocks use indentation. A more-indented line belongs to the previous line. If a line ends with `->`, the next indented block is part of the expression.

```nanyx
def message =
  def preamble = "Hello"
  def audience = "World"

  "{preamble} {audience}"
```

You can also continue an expression on the next line when the previous line is incomplete:

```nanyx
def message = "Hello " +
  "World"
```

A block returns the value of its last expression:

```nanyx
def answer =
  def x = 20
  def y = 22
  x + y
```

For more on expression forms, see [Functions](../tour/functions) and [Pattern matching](../tour/pattern-matching).

## Numbers

Nanyx includes familiar numeric literals:

```nanyx
def count = 42
def ratio = 3.14159
def million = 1_000_000
```

You can use standard arithmetic and comparison operators:

```nanyx
def total = 10 + 5 * 2
def bigger = total > 12
```

For operator behavior, see [Operators](../tour/operators). For range syntax, see [Ranges](../tour/ranges).

## Strings

String literals can use double quotes or single quotes:

```nanyx
def a = "Hello"
def b = 'Nanyx'
```

String interpolation uses `{...}` inside a string literal:

```nanyx
def name = "Alex"
def greeting = "Hello, {name}!"
```

For library string operations (`length`, `split`, `trim`, etc.), see [string stdlib](../stdlib/string).

## Equality and comparison

Use `==` and `!=` for equality and inequality:

```nanyx
def same = 10 == 10
def different = "a" != "b"
```

Use `<`, `<=`, `>`, `>=` for ordering comparisons:

```nanyx
def isAdult = 21 >= 18
```

These operators are part of the language's operator system; see [Operators](../tour/operators) for details.

## Mutability

Mutability is available in Nanyx, but is considered a somewhat advanced feature. Read about [the Memory context](../advanced/memory) for more.

For immutable record updates, see [Copy and update (non-destructive mutation)](../tour/records#copy-and-update-non-destructive-mutation).

## Commas and newlines

You can use commas or newlines to separate items in lists and records:

```nanyx
def items = [1, 2, 3]

def items = [
  1
  2
  3
]

def myRecord = (a = 1, b = 2, c = 3)

def myRecord = (
  a = 1
  b = 2
  c = 3
)
```

## Comments

Single-line comments use `--`. Multi-line comments start and end with a line containing only three or more `---`.

```nanyx
-- This is a single-line comment

---
This is a multi-line comment.
---

----------------------------------
This is also a multi-line comment.
----------------------------------
```

Multi-line comments must be closed with the same number of dashes that they were opened with.

### Tags, ranges and records

Nanyx also supports tags, ranges, and records as core value forms. See [Tags](../tour/tags), [Ranges](../tour/ranges), and [Records and tuples](../tour/records).

## Table literals

Table literals are a shorthand for lists of records with the same shape:

```nanyx
def namesAndAges =
  [| name     | age |
   | "Alex"   | 22  |
   | "Taylor" | 30  |
   | "Sam"    | 28  |
   | "Morgan" | 45  |]
```

## Conditionals

Nanyx uses `if` and supports multi-branch forms:

```nanyx
if cond ->
  someCode()
else ->
  someOtherCode()

if | cond1 ->
     someCode()
   | cond2 ->
     someOtherCode()
   | else ->
     yetMoreCode()
```

## Unimplemented code

Use `???` as a placeholder to keep code compiling while you iterate:

```nanyx
def myFunction = { () -> ??? }
```
