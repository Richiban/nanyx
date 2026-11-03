---
title: "result"
description: "Composing success/error values"
order: 6
---

The `result` module provides helpers for values represented as `#some(a) | #error(e)`.

# map

```nanyx
Result.map: (#some(a) | #error(e), (a -> b)) -> #some(b) | #error(e)
```

Transforms successful values and leaves errors unchanged.

```nanyx
#some(21) \Result.map { * 2 }
-- #some(42)
```

---

# mapError

```nanyx
Result.mapError: (#some(a) | #error(e1), (e1 -> e2)) -> #some(a) | #error(e2)
```

Transforms error values.

```nanyx
#error("bad") \Result.mapError { e -> "ERR: {e}" }
-- #error("ERR: bad")
```

---

# bind

```nanyx
Result.bind: (#some(a) | #error(e), (a -> (#some(b) | #error(f)))) -> #some(b) | #error(e | f)
```

Chains result-producing operations. The error cases of both steps are preserved in the result, so the caller can still distinguish them.

```nanyx
readLine()
  \Result.bind(parseInt)
-- #some(int) | #error(IoError | #invalidInt)
```

---

# defaultValue

```nanyx
Result.defaultValue: ((#some(a) | #error(e)), a) -> a
```

Returns the success value or fallback.

```nanyx
Result.defaultValue(#error("x"), 0)
-- 0
```

---

# isOk

```nanyx
Result.isOk: (#some(a) | #error(e)) -> bool
```

```nanyx
Result.isOk(#some(1))
-- true
```

---

# isError

```nanyx
Result.isError: (#some(a) | #error(e)) -> bool
```

```nanyx
Result.isError(#error("oops"))
-- true
```
