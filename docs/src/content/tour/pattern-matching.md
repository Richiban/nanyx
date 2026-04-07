---
title: "Pattern matching"
description: "Powerful control flow with match expressions"
order: 6
---

Pattern matching is one of Nanyx's most powerful features. The `match` expression lets you match values against patterns and destructure data.

While most languages have some kind of multi-way branching construct like `switch` statements, not all have pattern matching like Nanyx's `match`, which is far more powerful. Pattern matching allows you to extract nested values, check conditions, and handle different data shapes all in a single expression, making code more concise and safer by ensuring all possible cases are handled.

## Basic Matching

The simplest form of pattern matching is matching against literal values:

```nanyx
match someValue
  | 0 -> "zero"
  | 1 -> "one"
  | _ -> "something else"
```

## Introducing values

Patterns can also introduce new variables that are bound to parts of the matched value:

```nanyx
match someList
  | 0 -> "zero"
  | 1 -> "one"
  | n -> "something else: {n}"
```

## Matching Custom Types

```nanyx
type Shape =
  | #circle(float)
  | #rectangle(float, float)

def area: Shape -> float = { shape ->
  match shape
    | #circle(r) -> 3.14159 * r * r
    | #rectangle(w, h) -> w * h
}
```

## Guards

Nanyx doesn't have guards in the traditional sense; instead it allows arbitrary function bodies in patterns. This means you can write complex conditions directly in the pattern:

```nanyx
def describeNumber: int -> string = { n ->
  match n
    | { < 0 } -> "negative"
    | 0 -> "zero"
    | n & { > 100 } -> "{n}, which is large!"
    | _ -> "positive"
}
```

## Destructuring

```nanyx
def (x, y) = (10, 20)

match result
  | #ok(value) -> println("Got: {value}")
  | #error(msg) -> println("Error: {msg}")
```

## Pattern Matching in Functions

Since a function whose entire body is a pattern match is so common, you can merge the function definition and match patterns together:

```nanyx
rec sumList: list(int) -> int = {
  | [] -> 0
  | [head, ...tail] -> head + sumList(tail)
}
```

## Multiple Patterns

```nanyx
match day
  | "Saturday" | "Sunday" -> "Weekend!"
  | _ -> "Weekday"
```

## Combining Patterns

```nanyx
match getValue()
  | { < 0 } & n -> println("Negative: {n}")
  | { > 100 } & n -> println("Large: {n}")
  | n -> println("Something else: {n}")
```

> **Note:** The Nanyx compiler ensures your patterns are exhaustive — every possible value must be handled. This prevents runtime crashes from unhandled cases.
