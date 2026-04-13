---
title: collections
description: Operations on collections such as lists, maps, and sets
order: 3
---

The `collections` module provides functions for working with lists, maps, sets, and other collection types.

## List

Since lists are immutable, all transform operations on lists return new lists.

### all

```nanyx
List.all: (list(a), (a -> bool)) -> bool
```

Returns `true` if all elements satisfy the predicate.

```nanyx
[1, 2, 3] \List.all { > 0 }
-- true
```

Note: it can seem counter intuitive, but `List.all` always returns `true` for an empty list, since there are no elements that fail the predicate.

---

### any

```nanyx
List.any: (list(a), (a -> bool)) -> bool
```

Returns `true` if any element satisfies the predicate.

```nanyx
[1, 2, 3] \List.any { > 2 }
-- true
```

Note: it can seem counter intuitive, but `List.any` returns `false` for an empty list, since there are no elements that satisfy the predicate.

---

### append

```nanyx
List.append: (list(a), a) -> list(a)
```

Returns a new list with the element added to the end.

```nanyx
[1, 2] \List.append(3)
-- [1, 2, 3]
```

### filter

```nanyx
List.filter: (list(a), (a -> bool)) -> list(a)
```

Keeps only elements that satisfy the predicate.

```nanyx
[1, 2, 3, 4, 5] \List.filter { > 3 }
-- [4, 5]
```

---

### find

```nanyx
List.find: (list(a), (a -> bool)) -> #some(a) | #notFound
```

Returns the first element matching the predicate.

```nanyx
[1, 2, 3] \List.find { > 1 }
-- #some(2)
```

---

### flatten

```nanyx
List.flatten: list(list(a)) -> list(a)
```

Flattens a list of lists into a single list.

```nanyx
List.flatten([[1, 2], [3, 4]])
-- [1, 2, 3, 4]
```

---

### fold

```nanyx
List.fold: (list(a), b, ((b, a) -> b)) -> b
```

Reduces a list to a single value. Runs through the list from left to right, applying the function to an accumulator and each element.

```nanyx
[1, 2, 3] \List.fold(0) { + }
-- 6
```

---

### foldBack

```nanyx
List.foldBack: (list(a), b, ((b, a) -> b)) -> b
```

Reduces a list to a single value. Runs through the list backwards (from right to left), applying the function to an accumulator and each element.

```nanyx
[1, 2, 3] \List.foldBack(0) { - }
-- 0
```

---

### groupBy

```nanyx
List.groupBy: (list(a), (a -> b)) -> map(b, list(a))
```

Groups elements of a list by a key function, returning a map from keys to lists of elements.

```nanyx
[1, 2, 3, 4] \List.groupBy { % 2 }
-- Map [ 0 => [2, 4], 1 => [1, 3] ]
```

---

### length

```nanyx
List.length: list(a) -> int
```

Returns the number of elements.

```nanyx
List.length([1, 2, 3])
-- 3
```

---

### map

```nanyx
List.map: (list(a), (a -> b)) -> list(b)
```

Applies a function to every element in a list, collecting the results in a new list. The resulting list always has the same length as the input list.

```nanyx
[1, 2, 3] \List.map { * 2 }
-- [2, 4, 6]
```

---

### select

```nanyx
List.select: (list(a), [Yield(b)] (a -> ())) -> list(b)
```

For every element in the input list, runs the function with a `Yield` context that allows it to yield any number of output elements. Collects all yielded elements into a new list.

`select` can be thought of as a `map` and a `filter` at the same time, or a `map` followed by a `flatten` (but more efficient since it doesn't create intermediate lists).

```nanyx
type Color = #red | #green | #blue | #other(string)

def f: list(Color) -> list(string) = { items ->
    items \List.select { item ->
        if item is #other(v) then yield(v)
    }
}
```

---

### selectOne

```nanyx
List.selectOne: (list(a), [Yield(b)] (a -> ())) -> #some(b) | #emptyList
```

For every element in the input list, runs the function with a `Yield` context that allows it to yield any number of output elements. Immediately returns the first yielded element wrapped in `#some`, or `#emptyList` if no elements were yielded.

`selectOne` can be thought of as a `map` and a `find` at the same time.

```nanyx
type Color = #red | #green | #blue | #other(string)

def f: list(Color) -> #some(string) | #emptyList = { items ->
    items \List.selectOne { item ->
        if #other(v) = item then yield(v)
    }
}
```

---

### unfold

```nanyx
List.unfold: (b, (b -> #some(a, b) | #stop)) -> list(a)
```

Generates a sequence from an initial state value and a function that produces either `#stop` to end the sequence or `#some(element, newState)` to yield an element and a new state for the next step. Returns a list of all yielded elements.

```nanyx
def fibonacci = List.unfold((0, 1)) { current, next ->
    if current > 100 then #stop
    else #some(current, (next, current + next))
}
```
