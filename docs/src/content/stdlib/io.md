---
title: "io"
description: "Input and output operations"
order: 2
---

The `io` module provides functions for reading from and writing to standard I/O.

# println

```nanyx
io.println: string -> ()
```

Prints a string followed by a newline to stdout.

```nanyx
println("Hello, World!")
```

---

# print

```nanyx
io.print: string -> ()
```

Prints a string to stdout without a trailing newline.

```nanyx
print("Enter your name: ")
```

---

# dbg

```nanyx
io.dbg: a -> a
```

Prints a debug representation of any value and returns it. Useful for inspecting values in pipelines.

```nanyx
[1, 2, 3]
  \list.map { * 2 }
  \dbg
-- Prints: [2, 4, 6]
```

---

# readLine

```nanyx
io.readLine: () -> #ok(string) | #error(IoError)
```

Reads a line of input from stdin.

```nanyx
match readLine()
  | #ok(line) -> println("You said: {line}")
  | #error(_) -> println("Failed to read input")
```
