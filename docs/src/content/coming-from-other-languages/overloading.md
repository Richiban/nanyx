---
title: "Overloading"
description: "Why Nanyx avoids overloaded function names"
order: 3
---

Many languages let multiple functions share the same name as long as their type signatures differ. That is called overloading. Nanyx intentionally does not support it.

## Why Nanyx avoids overloading

Overloading looks convenient, but it adds costs that show up in everyday use:

- It reduces clarity at the call site. A single name can hide multiple meanings, so readers have to infer which one was chosen.
- It weakens error messages. When a call fails, the compiler has to explain a failed overload resolution rather than a direct mismatch.
- It makes refactors riskier. Adding a new overload can change which implementation existing code picks, even if nothing else changed.
- It complicates tooling. Jump-to-definition, auto-complete, and documentation become less precise because the name is ambiguous.
- It makes type inference harder. The compiler needs more context to decide which function you meant, especially in pipelines.

Nanyx prefers a single, explicit meaning for each function name. That keeps call sites readable and makes the language easier to reason about.

## How to model overload-like behavior

You can still provide multiple behaviors, but you do it explicitly:

- Use distinct names for distinct behaviors (`parseInt` vs `parseFloat`).
- Use tag unions to encode different inputs and match on them.
- Use modules or records to group related functions under clear names.
- Accept a function argument and let the caller choose behavior explicitly.

This trades a little conciseness for clarity, predictability, and better tooling.
