---
title: "iter"
description: "Generic iterable helpers via Iter(a) context"
order: 9
---

The `iter` module provides generic operations for any type `a` for which an `Iter(a)` context is provided.

# Iter context model

These functions assume an iteration capability like:

```nanyx
context Iter(a) = (
  elem: type
  next: a -> #some(elem, a) | #done
)
```

The element type for an iterable argument is always available as `Iter(a).elem`.

# firstOr

```nanyx
firstOr: [Iter(a)] (a, Iter(a).elem) -> Iter(a).elem
```

Returns the first element, or the fallback when the iterable is empty.

```nanyx
firstOr(items, fallback)
```

---

# first

```nanyx
first: [Iter(a)] a -> #some(Iter(a).elem) | #emptyIterable
```

Returns the first element wrapped in `#some`, or `#emptyIterable`.

```nanyx
first(items)
```

---

# any

```nanyx
any: [Iter(a)] (a, (Iter(a).elem -> bool)) -> bool
```

Returns `true` if any element satisfies the predicate.

```nanyx
any(items, { > 0 })
```

---

# all

```nanyx
all: [Iter(a)] (a, (Iter(a).elem -> bool)) -> bool
```

Returns `true` if all elements satisfy the predicate.

```nanyx
all(items, { > 0 })
```

---

# find

```nanyx
find: [Iter(a)] (a, (Iter(a).elem -> bool)) -> #some(Iter(a).elem) | #notFound
```

Finds the first matching element.

```nanyx
find(items, { .id == targetId })
```

---

# count

```nanyx
count: [Iter(a)] a -> int
```

Counts elements by iterating through the input.

```nanyx
count(items)
```

---

# fold

```nanyx
fold: [Iter(a)] (a, state, ((state, Iter(a).elem) -> state)) -> state
```

Folds elements from left to right.

```nanyx
fold(items, 0) { + }
```

---

# toList

```nanyx
toList: [Iter(a)] a -> list(Iter(a).elem)
```

Materializes an iterable into a list.

```nanyx
toList(items)
```

For deeper background on `Iter` and associated types, see [Associated types and constraints](../advanced/associated-types-and-constraints).
