---
title: "Official style analyzer"
description: "Style-focused diagnostics from the official Nanyx analyzer"
order: 7.5
---

Nanyx keeps compiler errors focused on correctness, while style and convention checks are handled by analyzers.

The **official Nanyx style analyzer** enforces recommended coding conventions, including guidance around exported definitions.

## Why it matters

The style analyzer helps teams keep APIs predictable and readable by flagging patterns such as:

- Exported definitions missing type annotations
- Convention or naming issues
- Other style-oriented recommendations that do not block compilation

## Example: exported annotations

```nanyx
export def add = { a, b -> a + b }
-- Analyzer warning: missing type annotation

export def add: (Nat, Nat) -> Nat = { a, b -> a + b }
```

These diagnostics are warnings/hints from the analyzer, not compiler errors.

## Learn more

For the full analyzer model (diagnostic levels, configuration, and custom analyzers), see [Analyzers](../advanced/analyzers).
