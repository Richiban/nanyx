---
title: "Functions"
description: "Defining and calling functions"
order: 4
---

Functions are first-class values in Nanyx, meaning they can be passed as arguments, returned from other functions, and stored in data structures.

Functions are written with braces, `{ ... }`, and optionally define parameters: `{ x -> ... }`. The parameter list is a comma-separated list of names, and the body is an expression that computes the result.

# Function basics

A function that takes no parameters (or, more specifically, take `unit` as input) is simply `{ ... }`:

```nanyx
def main = {
  println("Hello, world!")
}
```

You could also write it as 

```nanyx
def main = { () ->
  println("Hello, world!")
}
```

But this is almost never preferred, since the unit parameter list is implied when no parameters are specified.

All functions in Nanyx take exactly one argument and return exactly one result. The function type signature is `a -> b`, where `a` is the input type and `b` is the output type.

```nanyx
-- Simple function
def double: int -> int = { x -> x * 2 }

-- Calling the function
def result = double(21)  -- 42
```

# Multi-parameter functions

Since all functions take one argument, multi-parameter functions actually take a record (or tuple, see [Records and tuples](./records)) as their argument:

```nanyx
def add: (int, int) -> int = { x, y -> x + y }
-- The type (int, int) -> int means: takes a record of two ints, returns an int

-- Calling with multiple arguments
def sum = add(5, 10)  -- 15
```

# Unit type

The `()` type (unit) represents the absence of a meaningful value. It's used for functions that don't take input or don't return output:

```nanyx
-- No meaningful return value
def printHello: () -> () = {
  println("Hello")
  -- Don't need to write `return ()`
}

-- No parameters
def getMessage: () -> string = {
  "Hello, World!"
}
```

# Lambda expressions

Lambda expressions (also called anonymous functions) are created with braces:

```nanyx
-- Explicit lambda
def print1 = { print("1") }

-- Lambda with parameter
def increment = { x -> x + 1 }

-- Lambda in higher-order function
def names = data \map { item -> item.name }
```

## Shorthand lambdas

Nanyx provides convenient shorthand syntax for common lambda patterns:

```nanyx
-- Property access
def names = users \map { .name }
-- Equivalent to: users \map { user -> user.name }

-- Binary operators
def doubled = numbers \map { * 2 }
-- Equivalent to: numbers \map { x -> x * 2 }

def incremented = numbers \map { + 1 }
-- Equivalent to: numbers \map { x -> x + 1 }

-- Comparison operators
def adults = users \filter { .age > 18 }
-- Equivalent to: users \filter { user -> user.age > 18 }
```

# Parameters

## Optional parameters

Prefix a parameter name with `?` to make it optional. The compiler wraps the type in `Option` and inserts `#none` when omitted.

```nanyx
def printMessage = { ?message ->
  message
  \match
    | #nil -> "You didn't say anything"
    | #some, msg -> "You said -> {msg}"
  \println
}
```

Default values also make parameters optional while giving you a non-optional value inside the function:

```nanyx
def printResults(left = "left", middle, right = "right") ->
  print "{left}, {middle}, {right}"
```

## Named parameters

If you name parameters in the signature, callers can (in fact, _must_) pass a record with matching fields:

```nanyx
def replace: (string, replace: string, with: string) -> string = { s, o, r -> ??? }

replace("Hello world", replace = "world", with = "Nanyx")
```

# Callable values

Any value with a type `a -> b` can be called like a function, including dictionaries and strings:

```nanyx
def dict: string -> string = ["Hello" => "Bonjour", "Goodbye" => "Au revoir"]

dict("Hello") -- "Bonjour"
```

# Higher-order functions

Functions that take other functions as arguments or return functions are called higher-order functions:

```nanyx
-- Takes a function as an argument
def apply: ((a -> b), a) -> b = { f, x -> f(x) }

-- Returns a function
def makeAdder: int -> (int -> int) = { x ->
  { y -> x + y }
}

def add5 = makeAdder(5)
def result = add5(10)  -- 15
```

# Pattern-matching functions

Since a function that consists only of a pattern match on its arguments is so common, a shorthand syntax is available: functions can pattern match directly on their arguments:

```nanyx
-- Simple pattern matching function
rec sumList: list(int) -> int = {
  | [] -> 0
  | [head, ...tail] -> head + sumList(tail)
}

-- Multiple arguments with patterns
def divide: (int, int) -> Result(int, #divideByZero) = { 
  | _, 0 -> #error(#divideByZero)
  | x, y -> #some(x / y)
}

-- Pattern matching with guards
def classify: int -> string = {
  | 0 -> "zero"
  | 1 -> "one"
  | n if n < 0 -> "negative"
  | _ -> "other"
}
```

# Recursive functions

Use the `rec` keyword to define recursive functions:

```nanyx
rec factorial: int -> int = { n ->
  if n <= 1
    -> 1
    else -> n * factorial(n - 1)
}

rec length: list(a) -> int = {
  | [] -> 0
  | [_, ...tail] -> 1 + length(tail)
}
```

## Tail recursion

Tail recursion is a recursive style where the recursive call is the **last** operation in the function. This is much more efficient because the compiler can reuse the same stack frame for each call, effectively turning the recursive function into a loop and preventing stack overflow on large inputs.

A common pattern is to nest a helper function that carries an accumulator:

```nanyx
def sum: list(int) -> int = { xs ->
  rec loop = {
    | [], acc -> acc
    | [head, ...tail], acc -> loop(tail, acc + head)
  }

  loop(xs, 0)
}
```

Here `loop` is internal to `sum`, and `acc` stores the running result. Each recursive step passes the updated accumulator forward, and the base case returns it directly.

# Function composition

Functions can be composed to create new functions:

```nanyx
def compose: ((b -> c), (a -> b)) -> (a -> c) = { f, g ->
  { x -> f(g(x)) }
}

def addOne = { + 1 }
def double = { * 2 }

def addOneThenDouble = compose(double, addOne)
def result = addOneThenDouble(5)  -- 12
```

# Currying

While Nanyx functions naturally take one argument, you can create curried-style functions:

```nanyx
def add: int -> (int -> int) = { x ->
  { y -> x + y }
}

def add5 = add(5)
def result = add5(10)  -- 15
```

# Partial application

With records, you can simulate partial application:

```nanyx
def process: (config: Config, data: Data) -> Result = { config, data ->
  -- processing logic
}

-- Create a partially applied version
def processWithConfig = { data -> process(myConfig, data) }
```

# Type Annotations for clarity

While type inference works well, annotating function signatures is recommended for exported functions:

```nanyx
-- Without annotation (inferred)
def add = { x, y -> x + y }

-- With annotation (clearer, better errors)
export def add: (int, int) -> int = { x, y -> x + y }
```

Type annotations serve as documentation and help catch errors early.

# Pure functions

Functions without effects (no contexts) are pure - they always return the same output for the same input:

```nanyx
-- Pure function
def add: (int, int) -> int = { x, y -> x + y }

-- Effectful function (requires context)
def greet: <Console> string -> () = { name ->
  println("Hello, {name}!")
}
```

# Function examples

## Map Implementation

```nanyx
rec map: (list(a), (a -> b)) -> list(b) = { xs, f ->
  match xs
    | [] -> []
    | [head, ...tail] -> [f(head), ...map(tail, f)]
}
```

## Filter implementation

```nanyx
rec filter: (list(a), (a -> bool)) -> list(a) = { xs, predicate ->
  match xs
    | [] -> []
    | [head, ...tail] ->
        if predicate(head)
          -> [head, ...filter(tail, predicate)]
          else -> filter(tail, predicate)
}
```

## Fold implementation

```nanyx
rec fold: (list(a), b, (b, a) -> b) -> b = { xs, acc, f ->
  match xs
    | [] -> acc
    | [head, ...tail] -> fold(tail, f(acc, head), f)
}
```

# Polymorphic functions

In Nanyx a polymorphic function is simply one whose return type depends on 
the types of its argument(s)

The simplest polymorphic function is probably the identity function, i.e.
a function that does nothing other than return its input

```nanyx
def id = { x -> x }
```

Thanks to type inference we don't have to write type annotations for this
function or give it a specification, but if we wanted to it would look like
this:

```nanyx
def id: α -> α = { x -> x }
```

The lowercase `a`, when used in a type position, is a type variable (equivalent
to the concept of 'generics' in other languages). 

```nanyx
def head : list(α) -> #some(α) | #emptyList  = { 
  | []      -> #emptyList
  | [x, ..] -> #some(x) 
}
```