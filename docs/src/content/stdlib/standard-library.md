---
title: Overview
description: "Core abstractions and types"
order: 1
---

This page gives a high-level overview of core standard library abstractions.

# Collections

- `seq(a)` for possibly infinite sequences
- `series(a)` for finite sequences
- `list(a)` for indexed lists
- `set(a)` for membership checks
- `map(k, v)` for key/value lookup
- `lookup(k, v)` for multi-value lookups

# Async collections

- `stream(a)` for async streams
- `async(a)` for async computations

# Option and result

```nanyx
type Option(a) = #some(a) | #nil

type Result(ok, err) = #ok(ok) | #error(err)
```

# Map builders

Builders like `map` expose a `=>` operator in a workflow:

```nanyx
def myMap = map {
  "hello" => "world"
  "foo" => "bar"
}
```

If you want more detail, visit the specific module pages in this section.
