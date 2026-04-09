---
title: "Basics"
description: "Core syntax and expressions"
order: 4
---

# Basics

This page covers core syntax and expression rules to help you read and write simple Nanyx code.

## Expressions

Everything is an expression that produces a value.

```nanyx
def message = "Hello world"
```

## Definitions

Use `def` for immutable bindings:

```nanyx
def name = "Nanyx"
def version = 4
```

## Mutability

Mutable variables use `mut`, and updates use `set`:

```nanyx
mut counter = 0
set counter++
set counter = counter + 1
```

Immutability is a property of the variable, not the value. You can make mutable fields in a record with `mut`:

```nanyx
type Ref(a) = (mut value: a)

def myRef = Ref(1)
set myRef.value = 2
```

For immutable record updates, see [Copy and update (non-destructive mutation)](../tour/records.md#copy-and-update-non-destructive-mutation).

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

Single-line comments use `--`. Multi-line comments start and end with a line containing only `---`.

```nanyx
-- This is a single-line comment

---
This is a multi-line comment.
---
```

## Literals

Nanyx has familiar literals plus tags, ranges, and records:

```nanyx
def age = 21

def name = "Fred"

def factor = 1.5

def mode: #read | #readwrite = #readwrite

def range = 1..5

def coordinates = (x = 1, y = 2)

def numbers = [1, 2, 3]
```

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

Use `...` as a placeholder to keep code compiling while you iterate:

```nanyx
def myFunction = { () -> ... }
```
