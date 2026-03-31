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

We find that Nanyx's optional/named parameters, tag unions, and pattern matching provide enough flexibility to express different behaviors without needing overloading. If you need different behavior based on types, you can use pattern matching or tag unions to achieve that without overloading. See [Option-like types](../advanced/option-like-types) for an example of how to abstract across different unions with a shared convention.
