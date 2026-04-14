---
title: "math"
description: "Numeric utility functions"
order: 7
---

The `math` module provides common numeric and floating-point operations.

# abs

```nanyx
math.abs: number -> number
```

```nanyx
math.abs(-42)
-- 42
```

---

# min

```nanyx
math.min: (number, number) -> number
```

```nanyx
math.min(3, 7)
-- 3
```

---

# max

```nanyx
math.max: (number, number) -> number
```

```nanyx
math.max(3, 7)
-- 7
```

---

# clamp

```nanyx
math.clamp: (number, min: number, max: number) -> number
```

```nanyx
math.clamp(120, min = 0, max = 100)
-- 100
```

---

# sqrt

```nanyx
math.sqrt: number -> number
```

```nanyx
math.sqrt(81)
-- 9
```

---

# pow

```nanyx
math.pow: (number, number) -> number
```

```nanyx
math.pow(2, 8)
-- 256
```

---

# floor

```nanyx
math.floor: number -> int
```

```nanyx
math.floor(3.9)
-- 3
```

---

# ceil

```nanyx
math.ceil: number -> int
```

```nanyx
math.ceil(3.1)
-- 4
```

---

# round

```nanyx
math.round: number -> int
```

```nanyx
math.round(3.5)
-- 4
```
