---
title: "list"
description: "Operations on lists"
order: 2
---

# list

The `list` module provides functions for working with lists.

## Functions

### `map: (list(a), (a -> b)) -> list(b)`

Applies a function to every element in a list.

```nanyx
[1, 2, 3] \list.map { * 2 }
-- [2, 4, 6]
```

### `filter: (list(a), (a -> bool)) -> list(a)`

Keeps only elements that satisfy the predicate.

```nanyx
[1, 2, 3, 4, 5] \list.filter { > 3 }
-- [4, 5]
```

### `fold: (list(a), b, ((b, a) -> b)) -> b`

Reduces a list to a single value.

```nanyx
[1, 2, 3] \list.fold(0) { + }
-- 6
```

### `find: (list(a), (a -> bool)) -> #some(a) | #notFound`

Returns the first element matching the predicate.

```nanyx
[1, 2, 3] \list.find { > 1 }
-- #some(2)
```

### `flatten: list(list(a)) -> list(a)`

Flattens a list of lists into a single list.

```nanyx
list.flatten([[1, 2], [3, 4]])
-- [1, 2, 3, 4]
```

### `length: list(a) -> int`

Returns the number of elements.

```nanyx
list.length([1, 2, 3])
-- 3
```
