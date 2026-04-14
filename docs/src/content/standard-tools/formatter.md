---
title: "Formatter"
description: "Formatting Nanyx code with the standard formatter"
order: 2
---

Nanyx includes a standard formatter for producing consistent code layout across teams and projects.

The formatter focuses on whitespace, indentation, line wrapping, and other presentational concerns. It does not change program behavior.

## Why use it

- Keeps style consistent across contributors
- Reduces formatting-only diffs in reviews
- Works well with analyzer guidance and editor tooling

## Typical workflow

Format files before committing changes, and enable format-on-save in your editor where possible.

```nanyx
-- before
def add=(a,b)=>a+b

-- after
def add: (int, int) -> int = { a, b -> a + b }
```

## Related

See [Official style analyzer](./official-analyzer) for style diagnostics that complement formatting.
