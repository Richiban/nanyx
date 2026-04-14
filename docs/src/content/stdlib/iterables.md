---
title: "iterables"
description: "Generic iterable helpers via Iter(a) context"
order: 9
---

The `iterables` module provides generic operations for any type `a` that has an `Iter(a)` context.

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
Iterables.firstOr: [Iter(a)] (a, Iter(a).elem) -> Iter(a).elem
```

Returns the first element, or the fallback when the iterable is empty.

```nanyx
Iterables.firstOr(items, fallback)
```

---

# first

```nanyx
Iterables.first: [Iter(a)] a -> #some(Iter(a).elem) | #emptyIterable
```

Returns the first element wrapped in `#some`, or `#emptyIterable`.

```nanyx
Iterables.first(items)
```

---

# any

```nanyx
Iterables.any: [Iter(a)] (a, (Iter(a).elem -> bool)) -> bool
```

Returns `true` if any element satisfies the predicate.

```nanyx
Iterables.any(items, { > 0 })
```

---

# all

```nanyx
Iterables.all: [Iter(a)] (a, (Iter(a).elem -> bool)) -> bool
```

Returns `true` if all elements satisfy the predicate.

```nanyx
Iterables.all(items, { > 0 })
```

---

# find

```nanyx
Iterables.find: [Iter(a)] (a, (Iter(a).elem -> bool)) -> #some(Iter(a).elem) | #notFound
```

Finds the first matching element.

```nanyx
Iterables.find(items, { .id == targetId })
```

---

# count

```nanyx
Iterables.count: [Iter(a)] a -> int
```

Counts elements by iterating through the input.

```nanyx
Iterables.count(items)
```

---

# fold

```nanyx
Iterables.fold: [Iter(a)] (a, state, ((state, Iter(a).elem) -> state)) -> state
```

Folds elements from left to right.

```nanyx
Iterables.fold(items, 0) { + }
```

---

# toList

```nanyx
Iterables.toList: [Iter(a)] a -> list(Iter(a).elem)
```

Materializes an iterable into a list.

```nanyx
Iterables.toList(items)
```

For deeper background on `Iter` and associated types, see [Associated types and constraints](../advanced/associated-types-and-constraints).
