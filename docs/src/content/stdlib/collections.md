---
title: collections
description: Operations on collections such as lists, maps, and sets
order: 3
---

The `collections` module provides functions for working with lists, maps, sets, and other collection types.

## List

Since lists are immutable, all transform operations on lists return new lists.

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

### length

```nanyx
List.length: list(a) -> int
```

Returns the number of elements.

```nanyx
List.length([1, 2, 3])
-- 3
```
