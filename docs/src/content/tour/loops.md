---
title: "Loops"
description: "Iterating with for"
order: 10
---

Nanyx uses the `for` keyword to iterate. The iterable comes first, which keeps loops readable and supports pattern matching on the loop variable.

## Basic loops

```nanyx
for 1..5 -> print("Hello")

for 1..5 | i -> print("Hello times {i}")
```

## Pattern matching in loops

```nanyx
for 1..5 | { < 5 } -> println("Ready...")
         | _       -> println("Go!")
```

## Looping collections

```nanyx
def data = [1, 2, 3]

for data | item -> println(item)
```

## Enumerating with indexes

Zip a collection with a range to keep a counter:

```nanyx
def data = ["one", "two", "three"]

for data, 0.. | item, i ->
  print "Item number {i} is {item}"
```

This is equivalent to:

```nanyx
data \iteri { item, i -> print("Item number {i} is {item}") }
```

## Dictionaries

When iterating a map, the loop variable is a record of the key and value:

```nanyx
def dict = map { "one" => "uno"; "two" => "dos" }

for dict | key, value -> print "{key} => {value}"
```

## Ignoring loop variables

Use `_` to ignore unused variables:

```nanyx
for dict | _, value ->
  print "Only care about {value}"
```
