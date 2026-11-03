---
title: "Types"
description: "Nanyx's type system"
order: 2
---

Nanyx features a strong static type system with Hindley-Milner type inference. Types are descriptive and readable, and you can use record types for structure and tag unions for variants.

# Type inference

Nanyx can infer most types automatically:

```nanyx
-- Type is inferred as: (int, int) -> int
def add = { x, y -> x + y }

-- Type is inferred as: string -> int, from the call to String.length
def length = { s -> String.length(s) }
```

# Type annotations

While inference works well, you can (and should) annotate exported functions:

```nanyx
-- Explicit type annotation
def double: int -> int = { x -> x * 2 }

-- Multi-parameter function type
def multiply: (int, int) -> int = { x, y -> x * y }
```

# Basic types

Nanyx has several built-in primitive types:

```nanyx
def count: int = 42
def price: float = 19.99
def name: string = "Alice"
def active: bool = true
```

There is no separate `number` type. Numeric operations are generic over a `Num(a)` [context](../advanced/contexts), and `int` and `float` are the built-in types that satisfy it.

# Record types

Records are structural types with named fields. They have their own Language Tour page covering record types, structural typing, optional fields, and the unified record/tuple model.

See [Records and tuples](./records) for the full guide.

# Top and bottom types

`any` is the top type (accepts any value), and `undefined` is the bottom type (no values). These appear when you build generic or unreachable code paths.

# Tuple types

Tuples are anonymous records with numbered fields:

```nanyx
-- Tuple type
def point: (int, int) = (10, 20)

-- Named tuple (equivalent to record)
def coords: (x: int, y: int) = (x = 10, y = 20)
```

# Unified tuples and records

In Nanyx, tuples and records are one unified structure. You can mix positional and named fields in the same record, making a tuple just a record with all positional fields.

```nanyx
def mixed = (1, 2, label = "origin", unit = "px")
```

The only restriction is ordering: all positional fields must come before any named fields.

```nanyx
-- Valid
def valid = (10, 20, x = 10, y = 20)

-- Invalid (named field before positional field)
-- def invalid = (x = 10, 20)
```

See [Records and tuples](./records) for more on record shapes and field rules.

# Tag unions

Tag unions (also called sum types or discriminated unions) represent values that can be one of several variants:

```nanyx
-- Simple tag union
type Result(a, e) = #some(a) | #error(e)

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

# Option types

Nanyx has no null values. Use tag unions to represent optional values instead:

```nanyx
def findUser: UserId -> #some(User) | #notFound = { id ->
  def found = ... -- lookup logic
  if found then #some(user) else #notFound
}
```

# Generic types

Types can be parameterized with type variables:

```nanyx
-- Generic function (the type variable `a` is introduced automatically)
def identity: a -> a = { x -> x }

-- Generic list operations
def map: (list(a), (a -> b)) -> list(b) = { xs, f ->
  match xs
    | [] -> []
    | [head, ...tail] -> [f(head), ...map(tail, f)]
}

def zip: (list(a), list(b)) -> list((a, b)) = { xs, ys ->
  match (xs, ys)
    | ([], _) -> []
    | (_, []) -> []
    | ([x, ...xs2], [y, ...ys2]) -> [(x, y), ...zip(xs2, ys2)]
}
```

Type variables are automatically created when used in a function definition, and must be a single lowercase character such as `a`, `b`, or `c`. Type variables with multiple characters are reserved for [associated types](../advanced/associated-types-and-constraints).

# Type aliases

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

# Function types

Functions have specific type signatures:

```nanyx
-- Simple function type
type Validator(a) = a -> bool

-- Function with multiple parameters (takes a record)
type Transformer(a, b) = a -> b

-- Higher-order function type
type Mapper(a, b) = (list(a), (a -> b)) -> list(b)
```

## Function type precedence

In a type, `,` binds tighter than `->`. That means these two annotations are the same type:

```nanyx
def add: (int, int) -> int = { x, y -> x + y }
def add: int, int -> int = { x, y -> x + y }
```

Both describe a function whose single argument is a two-field record of ints. The parenthesised form is the one the formatter emits, and the one used throughout these docs. Parentheses are only _required_ when a function type appears as a field of the argument record, as in `(list(a), (a -> b)) -> list(b)`, so that the inner `->` is not read as the outer one.

# typeof

`typeof x` is a type expression that means "whatever the type of the value `x` is", in the same way as TypeScript's `typeof`. It can be used anywhere a type is expected:

```nanyx
def defaults = (host = "localhost", port = 8080)

-- Same shape as `defaults`, without restating the record type
def override: typeof defaults = (host = "example.com", port = 443)
```

This is useful for referring to the type of a value whose type was inferred rather than declared, such as a builder record passed to `use`.
