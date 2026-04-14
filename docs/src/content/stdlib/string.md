---
title: "string"
description: "String manipulation functions"
order: 4
---

The `string` module provides functions for working with UTF-8 strings.

# length

```nanyx
string.length: string -> int
```

Returns the number of grapheme clusters in the string.

```nanyx
string.length("hello")
-- 5
```

---

# uppercase

```nanyx
string.uppercase: string -> string
```

```nanyx
string.uppercase("hello")
-- "HELLO"
```

---

# lowercase

```nanyx
string.lowercase: string -> string
```

```nanyx
string.lowercase("HELLO")
-- "hello"
```

---

# split

```nanyx
string.split: (string, on: string) -> list(string)
```

```nanyx
string.split("a,b,c", on = ",")
-- ["a", "b", "c"]
```

---

# contains

```nanyx
string.contains: (string, string) -> bool
```

```nanyx
string.contains("hello world", "world")
-- true
```

---

# startsWith

```nanyx
string.startsWith: (string, string) -> bool
```

```nanyx
string.startsWith("nanyx", "nan")
-- true
```

---

# endsWith

```nanyx
string.endsWith: (string, string) -> bool
```

```nanyx
string.endsWith("nanyx", "nyx")
-- true
```

---

# indexOf

```nanyx
string.indexOf: (string, string) -> #some(int) | #notFound
```

```nanyx
string.indexOf("hello", "ll")
-- #some(2)
```

---

# replace

```nanyx
string.replace: (string, pattern: string, with: string) -> string
```

```nanyx
string.replace("hello world", pattern = "world", with = "Nanyx")
-- "hello Nanyx"
```

---

# substring

```nanyx
string.substring: (string, start: int, length: int) -> string
```

```nanyx
string.substring("nanyx", start = 1, length = 3)
-- "any"
```

---

# trim

```nanyx
string.trim: string -> string
```

Removes leading and trailing whitespace.

```nanyx
string.trim("  hello  ")
-- "hello"
```

---

# concat

```nanyx
string.concat: list(string) -> string
```

```nanyx
string.concat(["Hello", ", ", "World"])
-- "Hello, World"
```

---

# join

```nanyx
string.join: (list(string), sep: string) -> string
```

```nanyx
string.join(["a", "b", "c"], sep = "-")
-- "a-b-c"
```
