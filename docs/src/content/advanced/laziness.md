---
title: "Laziness"
description: "Lazy values with where blocks"
order: 4
---

Nanyx is eager by default, but you can define lazy values using a `where` block. The value is evaluated on first use and cached.

```nanyx
def f() ->
  println(message)
  println(message) where
    message = "Hello, World!"
```

Use this to avoid recomputing expensive intermediate values while keeping the definition readable.
