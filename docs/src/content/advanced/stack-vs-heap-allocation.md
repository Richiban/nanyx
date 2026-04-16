---
title: "Stack vs heap allocation"
description: "How Nanyx chooses allocation and passing strategy"
order: 4
---

Nanyx handles object allocation automatically. You write values normally, and the compiler decides whether each object is placed on the stack or on the heap.

When an object is stack-allocated, Nanyx passes it either by value or by reference based on its size.

# What this means today

- Allocation strategy is compiler-managed.
- Stack-allocated objects may be passed by value or by ref depending on size.
- You do not currently annotate values to force a specific strategy.

# Planned configurability

Nanyx is planned to expose controls so you can choose allocation and passing behavior explicitly when needed, including:

- forcing a copy,
- forcing a reference,
- forcing stack allocation,
- forcing heap allocation.

Until these controls land, allocation and passing decisions remain automatic.

For a broader overview, see [Compiler optimisations](../foundations/compiler-optimisations).
