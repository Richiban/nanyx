---
title: "Sequences and queries"
description: "Seq vs series and querying patterns"
order: 6
---

Nanyx has two enumerable abstractions: `seq(a)` and `series(a)`.

- A `seq` can be infinite (lazy or generated).
- A `series` is known to be finite and safe to iterate.

# Iteration safety

```nanyx
for 0.. | i -> println("Hello") -- Error: cannot iterate over a seq

for 1..10 | i -> println("Hello") -- OK

for 1.. \take(10) | i -> println("Hello") -- OK
```

# Enumerating with indexes

```nanyx
def items: series(string) = ...

for items, 0.. | item, i -> println("Item {i} is {item}")
```

# Iterators

Both `seq` and `series` provide `getIterator`. The main difference is that a `seq` iterator can be impure, while a `series` iterator is pure, reflecting the finiteness guarantee.
