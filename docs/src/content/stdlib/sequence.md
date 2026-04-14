---
title: "sequence"
description: "Helpers for sequence processing"
order: 8
---

The `sequence` module provides operations for finite and lazy sequence-like values.

# map

```nanyx
Sequence.map: (seq(a), (a -> b)) -> seq(b)
```

```nanyx
numbers \Sequence.map { * 2 }
```

---

# filter

```nanyx
Sequence.filter: (seq(a), (a -> bool)) -> seq(a)
```

```nanyx
numbers \Sequence.filter { > 0 }
```

---

# take

```nanyx
Sequence.take: (seq(a), int) -> seq(a)
```

```nanyx
fibonacci \Sequence.take(10)
```

---

# skip

```nanyx
Sequence.skip: (seq(a), int) -> seq(a)
```

```nanyx
items \Sequence.skip(5)
```

---

# fold

```nanyx
Sequence.fold: (seq(a), b, ((b, a) -> b)) -> b
```

```nanyx
numbers \Sequence.fold(0) { + }
```

---

# toList

```nanyx
Sequence.toList: seq(a) -> list(a)
```

Materializes a sequence into a strict list.

```nanyx
numbers \Sequence.take(3) \Sequence.toList
-- [first, second, third]
```
