---
title: Pattern matching
description: Powerful control flow with match expressions
order: 6
---

While most languages have some kind of multi-way branching construct like `switch` statements, not all have pattern matching like Nanyx's `match`, which is far more powerful. Pattern matching allows you to extract nested values, check conditions, and handle different data shapes all in a single expression, making code more concise and safer by ensuring all possible cases are handled.

# Overview

The simplest form of pattern matching is matching against literal values, which is directly equivalent to a C-style `switch` statement:

```nanyx
match someValue
  | 0 -> "zero"
  | 1 -> "one"
  | _ -> "something else"
```

Note the use of `_` as a wildcard pattern that matches anything not previously matched. This may be required to ensure exhaustiveness (depending on the type being matched), and all match expressions must be exhaustive in Nanyx.

# Pattern order

Unlike a C-style `switch`, Nanyx's `match` patterns are evaluated in the order they are written. This allows for more flexible patterns and the ability to handle overlapping cases:

```nanyx
match value
  | 0 -> "zero"
  | 1 -> "one"
  | _ -> "something else"
```

The compiler will emit an error if it detects that a pattern is unreachable due to a previous pattern matching all possible cases.

# Introducing values

Patterns can also introduce new variables that are bound to parts of the matched value:

```nanyx
match someFunction()
  | 0 -> "zero"
  | 1 -> "one"
  | n -> "something else: {n}"
```

# Destructuring

Destructuring is a syntax that breaks a data structure into its components and binds them to variables. This is especially useful for working with tuples, lists, and custom data types.

## Lists

```nanyx
-- Match an empty list
match myList
  | [] -> "empty!"
  | [head, ...tail] -> "head is {head}"
```

In this example, the pattern `[head, ...tail]` matches a non-empty list, binding the first element to `head` and the rest of the list to `tail`. The `...` syntax (called the spread operator) is used to indicate that `tail` should capture all remaining elements of the list.

## Tuples

```nanyx
-- Destructure a tuple
def (x, y) = (10, 20)
```

Destructuring is available in pattern matching, allowing for simulatneous testing of data and extracting values at the same time:

```nanyx
match p
  | (0, y) -> "x is zero, y is {y}"
  | (x, 0) -> "y is zero, x is {x}"
  | (x, y) -> "x is {x}, y is {y}"
```

Note how the patterns `(0, y)` and `(x, 0)` use a combination of literal matching and variable binding to match specific shapes of the tuple. 

The final pattern `(x, y)` matches any tuple and binds both elements to variables.

## Records

Records can also be destructured in patterns, allowing you to match on specific fields while ignoring others:

```nanyx
match r
  | (name = "Alice", ...) -> "Alice is {age} years old"
  | (name = name, age = age) -> "Alice is {age} years old"
```

Here, the pattern `(name = "Alice", ...)` matches any record where the `name` field is "Alice", while ignoring all other fields. The second pattern matches any record with `name` and `age` fields, binding them to variables.

As with tuples, records patterns must be of the correct shape to match the type being matched, and all fields must be accounted for either by matching them directly or using the spread operator to ignore them.

## Tags

Tags can also be destructured in patterns, using the same syntax that is used to construct them. For example, if you have a sum type representing shapes:

```nanyx
type Shape =
  | #point
  | #circle(float)
  | #rectangle(float, float)

def area: Shape -> float = { shape ->
  match shape
    | #point -> 0.0
    | #circle(r) -> 3.14159 * r * r
    | #rectangle(w, h) -> w * h
}
```

# Nesting patterns

Where pattern matching really shines is in its ability to nest patterns inside each other, allowing you to make complex data structure comparisons and extract values in a single expression. Below is an artificial complex example that demonstrates this:

```nanyx
type Result =
  | #some(int)
  | #error(string)

type Request = (
  id: int
  status: Result
  tags: list(string)
)

def processRequests: list(Request) -> string = { requests ->
  match requests
    | [] -> 
      "No requests"
    | [(id = 1, status = #some(value), tags = ["urgent", ...rest]), ...more] ->
      "Urgent request 1 with value {value}"
    | [(id, status = #error(msg), tags = []), (status = #some(val), ...), ...tail] ->
      "Error in request {id}: {msg}, followed by success"
    | [(status = #some(n), ...), ...rest] & requests & (length = { > 5 }) ->
      "Multiple requests with first one ok, total: {requests->length}"
    | _ -> 
      "Other pattern"
}
```

# Guards

Nanyx doesn't have guards in the traditional sense; instead it allows arbitrary function bodies to be used as patterns. This means you can write complex conditions directly in the pattern:

```nanyx
def describeNumber: int -> string = { n ->
  match n
    | { < 0 } -> "negative"
    | 0 -> "zero"
    | n & { > 100 } -> "{n}, which is large!"
    | _ -> "positive"
}
```

# Pattern-matching functions

Since a function whose entire body is a pattern match is so common, you can merge the function definition and match patterns together:

```nanyx
rec sumList: list(int) -> int = {
  | [] -> 0
  | [head, ...tail] -> head + sumList(tail)
}
```

This is purely syntactic sugar, but it can also aid in readability by joining together the code paths of a function with the function's parameters, for example:

```nanyx
def map: list(a) -> (a -> b) -> list(b) = {
  | []             , _ -> []
  | [head, ...tail], f -> [f(head), ...map(tail, f)]
}
```

In this example, the patterns can guide us in understanding the structure of the function, as the empty list case explicitly doesn't use the function argument (the function argument is discarded with `_`), while the non-empty case does.

# "Or" patterns

Sometimes we may wish to handle multiple patterns with the same result. This can be done with "or" patterns, which use the `|` operator to combine multiple patterns into one:

```nanyx
match day
  | "Saturday" | "Sunday" -> "Weekend!"
  | _ -> "Weekday"
```

# "And" patterns

"And" patterns allow for the combination of multiple patterns that must all match simultaneously. This is done with the `&` operator:

```nanyx
match getValue()
  | { < 0 } & n -> println("Negative: {n}")
  | { > 100 } & n -> println("Large: {n}")
  | n -> println("Something else: {n}")
```

> **Note:** The Nanyx compiler ensures your patterns are exhaustive — every possible value must be handled. This prevents runtime crashes from unhandled cases.

# If-matching

Patterns can be used outside of `match` expressions as well. Simple variable assignments can make use of patterns to destructure values:

```nanyx
def (x, y) = getCoordinates()

def Hsl(h, s, l) = getColor()
```

Bear in mind that patterns used in this way must be _total_, meaning they must match all possible values of the type being destructured. For example, if `getCoordinates()` returns a tuple of two integers, then the pattern `(x, y)` is total and will always match. However, if `Hsl` is a _partial_ pattern over the return type of `getColor()` then the pattern `Hsl(h, s, l)` is not total and will cause a compile-time error.

If the pattern is not total and you only care about a single case, you can use `if` with a pattern instead of a full `match` expression:

```nanyx
if #some(value) = getResult() then
  println("Got a value: {value}")
```

This is equivalent to a `match` expression that only handles one case and ignores everything else:

```nanyx
match getResult()
  | #some(value) -> println("Got a value: {value}")
  | _ -> ()
```

# Custom patterns

Custom patterns are covered in detail in [Advanced: Custom patterns](../advanced/custom-patterns).