---
title: "result"
description: "Composing success/error values"
order: 6
---

The `result` module provides helpers for values represented as `#ok(a) | #error(e)`.

# map

```nanyx
Result.map: (#ok(a) | #error(e), (a -> b)) -> #ok(b) | #error(e)
```

Transforms successful values and leaves errors unchanged.

```nanyx
#ok(21) \Result.map { * 2 }
-- #ok(42)
```

---

# mapError

```nanyx
Result.mapError: (#ok(a) | #error(e1), (e1 -> e2)) -> #ok(a) | #error(e2)
```

Transforms error values.

```nanyx
#error("bad") \Result.mapError { "ERR: {it}" }
-- #error("ERR: bad")
```

---

# bind

```nanyx
Result.bind: (#ok(a) | #error(e), (a -> (#ok(b) | #error(e)))) -> #ok(b) | #error(e)
```

Chains result-producing operations.

```nanyx
readLine()
  \Result.bind { parseInt }
```

---

# defaultValue

```nanyx
Result.defaultValue: ((#ok(a) | #error(e)), a) -> a
```

Returns the success value or fallback.

```nanyx
Result.defaultValue(#error("x"), 0)
-- 0
```

---

# isOk

```nanyx
Result.isOk: (#ok(a) | #error(e)) -> bool
```

```nanyx
Result.isOk(#ok(1))
-- true
```

---

# isError

```nanyx
Result.isError: (#ok(a) | #error(e)) -> bool
```

```nanyx
Result.isError(#error("oops"))
-- true
```
