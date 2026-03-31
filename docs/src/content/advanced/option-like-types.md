---
title: "Option-like types"
description: "Mapping #some across unions"
order: 9
---

# Option-like types

Nanyx can treat any tag union that includes `#some(a)` as “option-like.” That lets you write a single `map` that works for options, results, and custom unions by preserving all other tags.

## The core idea

The function below accepts any union that contains `#some(a)` and keeps the rest of the union intact:

```nanyx
def Option.map
  : (#some(a) | rest), (a -> b) -> (#some(b) | rest)
  = { | #some(a), f -> #some(f(a))
      | other, _ -> other }
```

`rest` represents all other tags in the union. They pass through unchanged.

## Works with options

```nanyx
def name: (#some(string) | #none) = ...

def upper = name \Option.map { .toUpperCase() }
```

## Works with results

```nanyx
def value: (#some(int) | #err(#notFound | #dbDown)) = ...

def doubled = value \Option.map { * 2 }
```

The error cases stay in the union, so you don’t lose information.

## Why this matters

- A shared `#some` convention lets you abstract across option and result-like types.
- You keep precise failure tags instead of collapsing them into a single error.
- You get reuse: one `map` works for many domain-specific unions.
