---
title: "Naming conventions"
description: "Recommended casing and identifiers"
order: 15
---

Nanyx uses camel case for values and functions, and Pascal case for types. A function definition that starts with an uppercase letter generates both a value and a type with the same name.

```nanyx
-- Value

def message = "Hello world"

-- Function

def sayHello = { println("Hello!") }

-- Type

type Greeter = (greet: () -> ())

```
