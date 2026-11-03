---
title: "string"
description: "String manipulation functions"
order: 4
---

The `string` module provides functions for working with UTF-8 strings.

These functions are [attached definitions](../advanced/attached-definitions) on `String`, so when the receiver is known to be a string they can be used without qualification: `s \split(on = ",")`, `s.length`, `s.startsWith("nan")`.

# length

```nanyx
String.length: string -> int
```

Returns the number of grapheme clusters in the string.

```nanyx
String.length("hello")
-- 5
```

---

# uppercase

```nanyx
String.uppercase: string -> string
```

```nanyx
String.uppercase("hello")
-- "HELLO"
```

---

# lowercase

```nanyx
String.lowercase: string -> string
```

```nanyx
String.lowercase("HELLO")
-- "hello"
```

---

# split

```nanyx
String.split: (string, on: string) -> list(string)
```

```nanyx
String.split("a,b,c", on = ",")
-- ["a", "b", "c"]
```

---

# contains

```nanyx
String.contains: (string, string) -> bool
```

```nanyx
String.contains("hello world", "world")
-- true
```

---

# startsWith

```nanyx
String.startsWith: (string, string) -> bool
```

```nanyx
String.startsWith("nanyx", "nan")
-- true
```

---

# endsWith

```nanyx
String.endsWith: (string, string) -> bool
```

```nanyx
String.endsWith("nanyx", "nyx")
-- true
```

---

# indexOf

```nanyx
String.indexOf: (string, string) -> #some(int) | #notFound
```

```nanyx
String.indexOf("hello", "ll")
-- #some(2)
```

---

# replace

```nanyx
String.replace: (string, pattern: string, replacement: string) -> string
```

```nanyx
String.replace("hello world", pattern = "world", replacement = "Nanyx")
-- "hello Nanyx"
```

---

# substring

```nanyx
String.substring: (string, start: int, length: int) -> string
```

```nanyx
String.substring("nanyx", start = 1, length = 3)
-- "any"
```

---

# trim

```nanyx
String.trim: string -> string
```

Removes leading and trailing whitespace.

```nanyx
String.trim("  hello  ")
-- "hello"
```

---

# concat

```nanyx
String.concat: list(string) -> string
```

```nanyx
String.concat(["Hello", ", ", "World"])
-- "Hello, World"
```

---

# join

```nanyx
String.join: (list(string), sep: string) -> string
```

```nanyx
String.join(["a", "b", "c"], sep = "-")
-- "a-b-c"
```
