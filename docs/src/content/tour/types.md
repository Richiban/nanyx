---
title: "Types"
description: "Nanyx's type system"
order: 2
---
# Types

Nanyx features a strong static type system with Hindley-Milner type inference. Types are descriptive and readable, and you can use record types for structure and tag unions for variants.

## Type Inference

Nanyx can infer most types automatically:

```nanyx
-- Type is inferred as: (number, number) -> number
def add = { x, y -> x + y }

-- Type is inferred as: string -> int
def length = { s -> s.length }
```

## Type Annotations

While inference works well, you can (and should) annotate exported functions:

```nanyx
-- Explicit type annotation
def double: int -> int = { x -> x * 2 }

-- Multi-parameter function type
def multiply: (int, int) -> int = { x, y -> x * y }
```

## Basic Types

Nanyx has several built-in primitive types:

```nanyx
def count: int = 42
def price: float = 19.99
def name: string = "Alice"
def active: bool = true
```

## Record Types

Records are structural types with named fields:

```nanyx
-- Record type definition
type Person = (
  name: string
  age: int
  email: string
)

-- Using the type
def alice: Person = (
  name = "Alice"
  age = 30
  email = "alice@example.com"
)
```

## Tuple Types

Tuples are anonymous records with numbered fields:

```nanyx
-- Tuple type
def point: (int, int) = (10, 20)

-- Named tuple (equivalent to record)
def coords: (x: int, y: int) = (x = 10, y = 20)
```

## Tag Unions

Tag unions (also called sum types or discriminated unions) represent values that can be one of several variants:

```nanyx
-- Simple tag union
type Result(a) = #ok(a) | #error(string)

-- Multiple variants
type Shape =
  | #circle(float)
  | #rectangle(float, float)
  | #triangle(float, float)

-- Using tag unions
def area: Shape -> float = { shape ->
  match shape
    | #circle(r) -> 3.14159 * r * r
    | #rectangle(w, h) -> w * h
    | #triangle(b, h) -> 0.5 * b * h
}
```

## Option Types

Nanyx has no null values. Use Option types instead:

```nanyx
type Option(a) = #some(a) | #none

def findUser: UserId -> Option(User) = { id ->
  -- lookup logic
  #some(user)  -- or #none if not found
}
```

## Generic Types

Types can be parameterized with type variables:

```nanyx
-- Generic function (type parameter α is inferred)
def identity: a -> a = { x -> x }

-- Generic list operations
def map: (list(a), (a -> b)) -> list(b) = { xs, f ->
  match xs
    | [] -> []
    | [head, ...tail] -> [f(head), ...map(tail, f)]
}

-- Multiple type parameters
def zip: (list(a), list(b)) -> list((a, b)) = { xs, ys ->
  match (xs, ys)
    | ([], _) -> []
    | (_, []) -> []
    | ([x, ...xs2], [y, ...ys2]) -> [(x, y), ...zip(xs2, ys2)]
}
```

## Type Aliases

Create meaningful names for complex types:

```nanyx
type UserId = int
type Email = string
type Age = int

type UserProfile = (
  id: UserId
  name: string
  email: Email
  age: Age
)
```

## Nominal and Protected Types

Sometimes a type's name matters more than its structure. Nanyx supports nominal types using the `@` prefix. These types cannot be constructed (or cloned) outside their home module, so you typically export constructor functions.

```nanyx
module user

export type @User = (
  id: UserId
  name: string
)

export def makeUser: (UserId, string) -> @User = { id, name ->
  @User(id = id, name = name)
}
```

Protected (opaque) types hide their structure outside the module by using `private`. This is useful for wrapping primitives safely and forcing validation through exported functions.

```nanyx
module ids

export type @UserId = private string

export def UserId: string -> @UserId = { value ->
  @UserId(value)
}
```

## Function Types

Functions have specific type signatures:

```nanyx
-- Simple function type
type Validator(a) = a -> bool

-- Function with multiple parameters (takes a record)
type Transformer(a, b) = a -> b

-- Higher-order function type
type Mapper(a, b) = ((a -> b), list(a)) -> list(b)
```
