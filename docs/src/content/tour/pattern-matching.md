---
title: Pattern matching
description: Powerful control flow with match expressions
order: 6
---

While most languages have some kind of multi-way branching construct like `switch` statements, not all have pattern matching like Nanyx's `match`, which is far more powerful. Pattern matching allows you to extract nested values, check conditions, and handle different data shapes all in a single expression, making code more concise and safer by ensuring all possible cases are handled.

## Overview

The simplest form of pattern matching is matching against literal values, which is directly equivalent to a C-style `switch` statement:

```nanyx
match someValue
  | 0 -> "zero"
  | 1 -> "one"
  | _ -> "something else"
```

Note the use of `_` as a wildcard pattern that matches anything not previously matched. This may be required to ensure exhaustiveness (depending on the type being matched), and all match expressions must be exhaustive in Nanyx.

## Pattern order

Unlike a C-style `switch`, Nanyx's `match` patterns are evaluated in the order they are written. This allows for more flexible patterns and the ability to handle overlapping cases:

```nanyx
match value
  | 0 -> "zero"
  | 1 -> "one"
  | _ -> "something else"
```

The compiler will emit an error if it detects that a pattern is unreachable due to a previous pattern matching all possible cases.

## Introducing values

Patterns can also introduce new variables that are bound to parts of the matched value:

```nanyx
match someFunction()
  | 0 -> "zero"
  | 1 -> "one"
  | n -> "something else: {n}"
```

## Destructuring

Destructuring is a syntax that breaks a data structure into its components and binds them to variables. This is especially useful for working with tuples, lists, and custom data types.

### Lists

```nanyx
-- Match an empty list
match myList
  | [] -> "empty!"
  | [head, ...tail] -> "head is {head}"
```

In this example, the pattern `[head, ...tail]` matches a non-empty list, binding the first element to `head` and the rest of the list to `tail`. The `...` syntax (called the spread operator) is used to indicate that `tail` should capture all remaining elements of the list.

### Tuples

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

### Records

Records can also be destructured in patterns, allowing you to match on specific fields while ignoring others:

```nanyx
match r
  | (name = "Alice", ...) -> "Alice is {age} years old"
  | (name = name, age = age) -> "Alice is {age} years old"
```

Here, the pattern `(name = "Alice", ...)` matches any record where the `name` field is "Alice", while ignoring all other fields. The second pattern matches any record with `name` and `age` fields, binding them to variables.

As with tuples, records patterns must be of the correct shape to match the type being matched, and all fields must be accounted for either by matching them directly or using the spread operator to ignore them.

### Tags

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

## Nesting patterns

Where pattern matching really shines is in its ability to nest patterns inside each other, allowing you to make complex data structure comparisons and extract values in a single expression:

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

## Guards

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

## Pattern-matching functions

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

## "Or" patterns

Sometimes we may wish to handle multiple patterns with the same result. This can be done with "or" patterns, which use the `|` operator to combine multiple patterns into one:

```nanyx
match day
  | "Saturday" | "Sunday" -> "Weekend!"
  | _ -> "Weekday"
```

## "And" patterns

"And" patterns allow for the combination of multiple patterns that must all match simultaneously. This is done with the `&` operator:

```nanyx
match getValue()
  | { < 0 } & n -> println("Negative: {n}")
  | { > 100 } & n -> println("Large: {n}")
  | n -> println("Something else: {n}")
```

> **Note:** The Nanyx compiler ensures your patterns are exhaustive — every possible value must be handled. This prevents runtime crashes from unhandled cases.

## If-matching

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

## Custom patterns

The `pattern` keyword allows for the encapsulation of matching logic in reusable components.

A custom pattern is ultimately a function with a signature but no name; instead the pattern is identified by the tag(s) it returns. Custom patterns return tags that use the type naming scheme (to distinguish them from standard tags), meaning they must start with a capital letter after the `#`.

The function body contains the logic for matching and extracting values, and returns one of the tags used in the pattern signature. As with tags elsewhere in the language, members of the pattern union can carry payloads. This gives the custom pattern the ability to return values that are then available in the match body.

### Total patterns

A pattern is _total_ if it matches all possible inputs. 

A total pattern can have one choice:

```nanyx
pattern Rgb -> #Hsl(h: float, s: float, l: float) = { rgb ->
  -- conversion logic here
}

match getRgbColor()
  | #Hsl(h, s, l) -> println("Turned color into HSL: {h}, {s}, {l}")

-- since the pattern is single-choice and total, we can even use it directly in a value binding:

def #Hsl(h, s, l) = getRgbColor()
```
 
or many:

```nanyx
pattern int -> #Even | #Odd = { n -> if n % 2 == 0 then #Even else #Odd }
```

### Partial patterns

Custom patterns can be partial (meaning they might not match all possible inputs). For a pattern to be partial, one of its return values must be a wildcard (`_`).

As a convenience, Nanyx allows a partial pattern with exactly one choice and no output valuess to return a `bool` instead of an explicit tag:

```nanyx
pattern int -> #Zero | _:  = { n -> n == 0 }

match num
  | #Zero -> "zero"
  | _ -> "non-zero"

-- Alternatively, we can use if-matching style:
if #Zero = num then
  println("It's zero!")
else
  println("It's not zero.")
```

In the case of partial patterns with multiple choices or output values, the wildcard case must be explicitly returned using the `_` value:

```nanyx
pattern string -> #ParseInt(int) | #ParseBool(bool) | _ = { s ->
  if | #some(i) = int.parse(s) -> #ParseInt(i) 
     | #some(b) = bool.parse(s) -> #ParseBool(b)
     | else -> _
}

match getUserInput()
  | #ParseInt(i) -> println("Parsed an integer: {i}")
  | #ParseBool(b) -> println("Parsed a boolean: {b}")
  | _ -> println("Input is neither an integer nor a boolean.")
```

### Patterns with arguments

Patterns can also take arguments, which are values that are passed in when the pattern is used. This allows for more flexible and reusable patterns and are useful for complex conditions like regex matching:

```nanyx
pattern Regex -> string -> #MatchesRegex | _:  = { s, r ->
  s \Regex.matches(r)
}

match userInput
  | #MatchesRegex("[^@]+@\w+\.\w+") -> "valid email"
  | _ -> "invalid format"
```

A pattern with arguments has the form `<args> -> <input> -> <output>`, where the first part is the argument type, the second part is the input type that the pattern matches against, and the third part is the output tag union indicating match success or failure.

```nanyx
-- A pattern that checks whether a string contains a given character and splits on it
pattern char -> string -> #SplitOnChar(string, string) | _ = { c, s ->
  def parts = s.split(c)
  if parts.length == 2 then #SplitOnChar(parts[0], parts[1]) else #_
}

match "hello,world"
  | #SplitOnChar(',')(s1, s2) -> "First: {s1}, Second: {s2}"
  | _ -> "Input string does not contain a comma"
```

You can also compose patterns to handle more sophisticated matching:

```nanyx
pattern int -> #PositiveInt | _ = { > 0 }

match value
  | #PositiveInt & { < 1000 } -> "small positive"
  | #PositiveInt -> "large positive"
  | _ -> "non-positive"
```