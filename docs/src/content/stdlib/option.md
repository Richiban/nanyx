---
title: "option"
description: "Working with optional values"
order: 5
---

The `option` module provides helpers for values that may or may not be present.

# map

```nanyx
Option.map: (#some(a) | #nil, (a -> b)) -> (#some(b) | #nil)
```

Transforms the value when present.

```nanyx
#some("abc") \Option.map { .length }
-- #some(3)
```

---

# bind

```nanyx
Option.bind: (#some(a) | #nil, (a -> (#some(b) | #nil))) -> (#some(b) | #nil)
```

Chains option-producing operations.

```nanyx
#some("42") \Option.bind { int.parse }
```

---

# defaultValue

```nanyx
Option.defaultValue: ((#some(a) | #nil), a) -> a
```

Returns the wrapped value or a fallback.

```nanyx
Option.defaultValue(#nil, 0)
-- 0
```

---

# isSome

```nanyx
Option.isSome: (#some(a) | #nil) -> bool
```

```nanyx
Option.isSome(#some(10))
-- true
```

---

# isNil

```nanyx
Option.isNil: (#some(a) | #nil) -> bool
```

```nanyx
Option.isNil(#nil)
-- true
```

---

# toResult

```nanyx
Option.toResult: ((#some(a) | #nil), err) -> #ok(a) | #error(err)
```

Converts an option to a result with a supplied error value.

```nanyx
Option.toResult(#nil, "missing user")
-- #error("missing user")
```
