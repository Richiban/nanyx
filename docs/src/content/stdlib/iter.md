---
title: "iter"
description: "Generic iterable helpers via Iter(a) context"
order: 9
---

The `iter` module provides generic operations for any type `a` for which an `Iter(a)` context is provided.

# Contexts

## Iter(a)

```nanyx
context Iter(a) = (
  type elem
  next: a -> #some(elem, a) | #done
)
```

The element type for the iterable is stored as an [associated type](../advanced/associated-types-and-constraints) on the `Iter(..)` context with the name `elem`; for an iterable argument, it is always available as `elem` in the function signature (or `Iter(a).elem`, if disambiguation is needed).

# Definitions

## firstOr

```nanyx
firstOr: [Iter(a)] (a, elem) -> elem
```

Returns the first element, or the fallback when the iterable is empty.

```nanyx
items\firstOr(fallback)
```

---

## first

```nanyx
first: [Iter(a)] a -> #some(elem) | #emptyIterable
```

Returns the first element wrapped in `#some`, or `#emptyIterable`.

```nanyx
first(items)
```

---

## any

```nanyx
any: [Iter(a)] (a, (elem -> bool)) -> bool
```

Returns `true` if any element satisfies the predicate.

```nanyx
items\any { > 0 }
```

---

## all

```nanyx
all: [Iter(a)] (a, (elem -> bool)) -> bool
```

Returns `true` if all elements satisfy the predicate.

```nanyx
items\all { > 0 }
```

---

## find

```nanyx
find: [Iter(a)] (a, (elem -> bool)) -> #some(elem) | #notFound
```

Finds the first matching element.

```nanyx
items\find { .id == targetId }
```

---

## count

```nanyx
count: [Iter(a)] a -> int
```

Counts elements by iterating through the input.

```nanyx
items\count
```

---

## fold

```nanyx
fold: [Iter(a)] (a, state, ((state, elem) -> state)) -> state
```

Folds elements from left to right.

```nanyx
items\fold(0) { + }
```

---

## toList

```nanyx
toList: [Iter(a)] a -> list(elem)
```

Materializes an iterable into a list.

```nanyx
items\toList
```
