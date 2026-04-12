---
title: "Type inference and annotations"
description: "How Nanyx infers types and when to annotate"
order: 7
---

Type inference is foundational in Nanyx; type annotations very rarely _need_ to be written as the compiler infers types from values and usage. Annotations are, however, recommended for definitions exported from a module and _strongly_ recommended for definitions exported from a package.

## Inference by value

```nanyx
def message = "Hello world" -- inferred as string

def message: string = "Hello world" -- explicitly annotated
```

## Inference for functions

```nanyx
def add = { a, b -> a + b }
-- inferred as (int, int) -> int from the usage of `+` on `a` and `b`
```

Since functions conceptually have exactly one parameter, the parameter types are inferred from how the function is used. If the function is never called, or if the parameter types cannot be inferred from the body of the function, then the parameter types will be inferred as generic type variables, and the function will be inferred as a generic function:

```nanyx
def identity = { x -> x }
-- inferred as `a -> a`, meaning it can take any type `a` and returns the same type `a`
```

### Positional vs named inference

When functions are unannotated, Nanyx assumes that any deconstructed arguments are positional. If you want to use named parameters, annotate the function type:

```nanyx
-- Inferred as `(int, int) -> int` with positional parameters
def magnitude = { x, y -> math.sqrt(x * x + y * y) }
```

```nanyx
-- The annotation makes field names part of the function signature
def magnitude2: (x: int, y: int) -> () = { x, y -> math.sqrt(x * x + y * y) }
```

Field names in function signatures should be a deliberate choice, since callers must use the field names to call the function:

```nanyx
magnitude2(3, 4) -- ✗ This will not compile because the function expects named parameters

magnitude2(x = 3, y = 4) -- The correct way to call a function with named parameters
```

## Exported values

Exported values should be annotated:

```nanyx
export def add = { a, b -> a + b }
-- Analyzer warning: missing type annotation

export def add: Nat, Nat -> Nat = { a, b -> a + b }
```

## Target typing

Nanyx can infer types based on how values are used:

```nanyx
def parse: string -> a = { json -> ... }

def obj = parse("{}")

def person: Person = obj
```

You can also use assertions to guide inference:

```nanyx
def obj = parse("..."): Person
```
