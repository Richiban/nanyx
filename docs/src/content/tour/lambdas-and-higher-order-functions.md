---
title: "Higher-order functions"
description: "Passing and returning functions"
order: 5
---

A higher-order function takes a function as an argument or returns one. Nanyx treats functions as values, so higher-order programming is idiomatic.

# Passing functions

```nanyx
def add1 = { x -> x + 1 }

def numbers = [1, 2, 3]

def biggerNumbers = numbers \map(add1)
```

# Lambdas

```nanyx
-- Full lambda

def f = { (x: int): int -> x * 2 }

-- Inline lambda

def biggerNumbers = numbers \map { x -> x + 1 }
```

# Shorthand lambdas

The shorthand form omits the parameter and lifts members into scope:

```nanyx
def names = people \map { .name }

def increment = numbers \map { + 1 }
```

# Pattern matching in lambdas

```nanyx
things \map {
  | #nil -> 0
  | #some(_) -> 1
} \sum
```

# Builder-style lambdas

Builders can run lambdas in a scoped context. This pattern powers workflows and collection builders:

```nanyx
@builderPattern
type Point = (x: int, y: int)

def p = Point {
  set x = 4
  set y = 5
}
```
