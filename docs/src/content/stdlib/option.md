---
title: "option"
description: "Working with optional values"
order: 5
---

The `option` module provides helpers for [**option-like** unions](../advanced/option-like-types).

In Nanyx, any union that includes `#some(a)` is considered option-like. These helpers are written to preserve all non-`#some` tags.

# map

```nanyx
Option.map: ((#some(a) | rest), (a -> b)) -> (#some(b) | rest)
```

Transforms the value when present.

```nanyx
#some("abc") \Option.map { .length }
-- #some(3)

def value: (#some(string) | #empty) = #some("abc")
value \Option.map { .length }
-- #some(3)
```

---

# bind

```nanyx
Option.bind: ((#some(a) | r1), (a -> (#some(b) | r2))) -> (#some(b) | r1 | r2)
```

Chains option-producing operations.

```nanyx
#some("42") \Option.bind { int.parse }
```

---

# defaultValue

```nanyx
Option.defaultValue: ((#some(a) | rest), a) -> a
```

Returns the wrapped value or a fallback.

```nanyx
Option.defaultValue(#none, 0)
-- 0
```

---

# isSome

```nanyx
Option.isSome: (#some(a) | rest) -> bool
```

```nanyx
Option.isSome(#some(10))
-- true
```

---

# isNone

```nanyx
Option.isNone: (#some(a) | rest) -> bool
```

```nanyx
Option.isNone(#none)
-- true
```

---

# toResult

```nanyx
Option.toResult: ((#some(a) | rest), err) -> #ok(a) | #error(err)
```

Converts an option to a result with a supplied error value.

```nanyx
Option.toResult(#none, "missing user")
-- #error("missing user")
```

---

# mapOr

```nanyx
Option.mapOr: ((#some(a) | rest), default: b, (a -> b)) -> b
```

Maps the contained value or returns a default.

```nanyx
Option.mapOr(#some("abc"), default = 0) { .length }
-- 3

Option.mapOr(#none, default = 0) { .length }
-- 0
```

---

# mapOrElse

```nanyx
Option.mapOrElse: ((#some(a) | rest), defaultFactory: () -> b, (a -> b)) -> b
```

Maps the contained value or computes a fallback lazily.

```nanyx
Option.mapOrElse(#none, defaultFactory = { expensiveFallback() }) { .length }
```

For deeper background on option-like unions, see [Option-like types](../advanced/option-like-types).
