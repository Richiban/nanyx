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
- [iter](./iter) — generic `Iter(a)`-based helpers

# Core abstractions

- `seq(a)` for finite sequences
- `gen(a)` for potentially infinite sequences
- `list(a)` for indexed lists
- `set(a)` for membership checks
- `map(k, v)` for key/value lookup
- `lookup(k, v)` for multi-value lookups

# Async collections

- `stream(a)` for async streams
- `async(a)` for async computations

# Option and result

There is no `Option` type. Any union containing `#some(a)` is [option-like](../advanced/option-like-types) by convention, and the `option` module's helpers work on all of them; `#some(a) | #none` is simply the most common shape.

The `Result` alias exists for the equally common success/error shape:

```nanyx
type Result(a, e) = #some(a) | #error(e)
```

# Map literals

Maps are written with the square-bracket literal syntax and the `=>` operator:

```nanyx
def myMap = ["hello" => "world", "foo" => "bar"]
```

Square-bracket literals desugar to a call to the target type's builder, which is what makes the same syntax work for lists, sets and maps. See [Collections](../tour/collections).

If you want more detail, visit the specific module pages in this section.
