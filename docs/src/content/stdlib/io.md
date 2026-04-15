---
title: "io"
description: "Input and output operations"
order: 2
---

The `io` module provides functions for reading from and writing to standard I/O.

All functions in this module require a `Console` context.

# println

```nanyx
println: [Console] string -> ()
```

Prints a string followed by a newline to stdout.

```nanyx
println("Hello, World!")
```

---

# print

```nanyx
print: [Console] string -> ()
```

Prints a string to stdout without a trailing newline.

```nanyx
print("Enter your name: ")
```

---

# eprintln

```nanyx
eprintln: [Console] string -> ()
```

Prints a string followed by a newline to stderr.

```nanyx
eprintln("Configuration file missing")
```

---

# eprint

```nanyx
eprint: [Console] string -> ()
```

Prints a string to stderr without a trailing newline.

```nanyx
eprint("warning: ")
```

---

# dbg

```nanyx
dbg: [Console] a -> a
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
readLine: [Console] () -> #some(string) | #error(IoError)
```

Reads a line of input from stdin.

```nanyx
match readLine()
  | #some(line) -> println("You said: {line}")
  | #error(_) -> println("Failed to read input")
```

---

# readToEnd

```nanyx
readToEnd: [Console] () -> #some(string) | #error(IoError)
```

Reads all remaining input from stdin.

```nanyx
match readToEnd()
  | #some(text) -> println("Read {text.length} chars")
  | #error(_) -> eprintln("Failed to read stdin")
```

---

# flush

```nanyx
flush: [Console] () -> ()
```

Flushes buffered output streams.

```nanyx
print("processing...")
flush()
```
