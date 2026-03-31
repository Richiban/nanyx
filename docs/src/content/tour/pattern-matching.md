---
title: "Pattern matching"
description: "Powerful control flow with match expressions"
order: 6
---

# Pattern matching

Pattern matching is one of Nanyx's most powerful features. The `match` expression lets you match values against patterns and destructure data.

## Basic Matching

```nanyx
match someValue
  | 0 -> "zero"
  | 1 -> "one"
  | _ -> "something else"
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

```nanyx
def describeNumber: int -> string = { n ->
  match n
    | n if n < 0 -> "negative"
    | 0 -> "zero"
    | n if n > 100 -> "large"
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
