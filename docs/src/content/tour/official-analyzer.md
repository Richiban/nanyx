---
title: "Official style analyzer"
description: "Style-focused diagnostics from the official Nanyx analyzer"
order: 7.5
---

Nanyx keeps compiler errors focused on correctness, while style and convention checks are handled by analyzers.

The **official Nanyx style analyzer** enforces recommended coding conventions, including guidance around exported definitions.

Analyzers are just Nanyx libraries that run during compilation and emit diagnostics. The official Nanyx style analyzer is installable / uninstallable in any project, and is included in the default project template. It is entirely possible (even encouraged) to write custom analyzers for your team or project.

## Why it matters

The style analyzer helps teams keep APIs predictable and readable by flagging patterns such as:

- Exported definitions missing type annotations
- Convention or naming issues
- Other style-oriented recommendations that do not block compilation

## Rules 

### Exported annotations

```nanyx
-- Analyzer warning: missing type annotation
export def add = { a, b -> a + b }
```

These diagnostics are warnings/hints from the analyzer, not the compiler.

### Unused local value

```nanyx
def total =
  def taxRate = 0.2 -- Analyzer warning: `taxRate` is never used
  100
```

### Unused function argument

```nanyx
-- Analyzer warning: argument `title` is never used
def greet: (string, string) -> string = { name, title ->
  "Hello, {name}!"
}
```

### Unused pattern variable

```nanyx
match response
  | #ok(value) -> "Success" -- Analyzer warning: `value` is introduced by pattern but never used
  | #error(msg) -> "Error"  -- Analyzer warning: `msg` is introduced by pattern but never used
```

### Unused import

```nanyx
import (
  nanyx/math as m
  nanyx/string as s -- Analyzer warning: import `s` is never used
)

def x = m.sqrt(16)
```

### Unreachable match branch

```nanyx
match n
  | _ -> "always"
  | 0 -> "never reached"
-- Analyzer warning/error: unreachable pattern branch
```

### Unreachable code path

```nanyx
-- Analyzer warning: unreachable code
```

### Unused return value

```nanyx
def compute: [Console] int -> int = { n ->
  readLine() -- Analyzer error: the result of `readLine` is computed but never used
  42
}
```

## Learn more

For the full analyzer model (diagnostic levels, configuration, and custom analyzers), see [Analyzers](../advanced/analyzers).
