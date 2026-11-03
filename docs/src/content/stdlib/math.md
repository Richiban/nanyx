---
title: "math"
description: "Numeric utility functions"
order: 7
---

The `math` module provides common numeric and floating-point operations.

Functions that work on any numeric type are generic over the `Num(a)` [context](../advanced/contexts); `int` and `float` are its built-in instances, so these functions accept either without conversion. Functions that only make sense for floating-point values take `float` directly.

# abs

```nanyx
math.abs: [Num(a)] a -> a
```

```nanyx
math.abs(-42)
-- 42
```

---

# min

```nanyx
math.min: [Num(a)] (a, a) -> a
```

```nanyx
math.min(3, 7)
-- 3
```

---

# max

```nanyx
math.max: [Num(a)] (a, a) -> a
```

```nanyx
math.max(3, 7)
-- 7
```

---

# clamp

```nanyx
math.clamp: [Num(a)] (a, min: a, max: a) -> a
```

```nanyx
math.clamp(120, min = 0, max = 100)
-- 100
```

---

# sqrt

```nanyx
math.sqrt: float -> float
```

```nanyx
math.sqrt(81.0)
-- 9.0
```

---

# pow

```nanyx
math.pow: [Num(a)] (a, int) -> a
```

```nanyx
math.pow(2, 8)
-- 256
```

---

# floor

```nanyx
math.floor: float -> int
```

```nanyx
math.floor(3.9)
-- 3
```

---

# ceil

```nanyx
math.ceil: float -> int
```

```nanyx
math.ceil(3.1)
-- 4
```

---

# round

```nanyx
math.round: float -> int
```

```nanyx
math.round(3.5)
-- 4
```
