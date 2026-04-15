---
title: Overview
description: "Core abstractions and types"
order: 1
---

This page gives a high-level overview of core standard library abstractions.

# Modules in this section

- [io](./io) — console and stream-style input/output helpers
- [collections](./collections) — list and collection transforms
- [string](./string) — UTF-8 string utilities
- [option](./option) — optional-value helpers
- [result](./result) — success/error composition helpers
- [math](./math) — numeric utilities
- [sequence](./sequence) — lazy/iterable sequence helpers
- [iterables](./iterables) — generic `Iter(a)`-based helpers

# Core abstractions

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

type Result(ok, err) = #some(ok) | #error(err)
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
