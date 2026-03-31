---
title: "What is Nanyx?"
description: "Write your first Nanyx program"
order: 3
---

# What is Nanyx?

Nanyx is a statically-typed, functional-first programming language designed for expressive workflows, clean syntax, and strong type safety. It combines the best ideas from functional programming with practical features for real-world development, featuring Hindley-Milner type inference, context-based effects, powerful pattern matching, and a unique pipeline operator for composable transformations.

It compiles to WASM, making it ideal for web development, but it's also great for general-purpose programming. Whether you're building a web app, a CLI tool, or a server, Nanyx has you covered.

## What Nanyx feels like

- **Expression-oriented**: Everything returns a value. No statements, just composable expressions.
- **Records and tags**: Carry the structure of your data with precision and clarity.
- **Pipelines**: Keep data flow obvious with the `\` operator for readable transformations.
- **Type inference**: Rarely write types, but get strong static guarantees.
- **Pattern matching**: Exhaustive matching on records, tag unions, and more.

## Key Features at a Glance

### Pipeline Operator
The `\` operator lets you chain transformations in a readable, left-to-right manner:
```nanyx
data \parse \validate \transform \save
```

### Context-Based Effects
Manage side effects explicitly through contexts, providing algebraic effect handlers without the complexity:
```nanyx
context Console = (println: string -> ())
def greet: <Console> string -> () = { name ->
  println("Hello, {name}!")
}
```

### Powerful Pattern Matching
Exhaustive pattern matching on records, tag unions, literals, and more:
```nanyx
match result
  | #ok(value) -> process(value)
  | #error(msg) -> logError(msg)
```

### Strong Static Typing with Inference
Full Hindley-Milner type inference means you rarely need to write type annotations, but you can when it improves clarity.

## Philosophy

Nanyx is built on the principle that code should be **clear, composable, and correct**. It takes inspiration from languages like F#, Haskell, OCaml, and Elm while maintaining its own unique identity.

- **Correctness above all**: Static type checking, exhaustive pattern matching, and no null values ensure fewer runtime errors.
- **Simple ≠ Easy**: The language may take longer to learn, but its consistency pays dividends over time.
- **Inform, don't block**: Warnings are treated as build failures, but debug builds can still execute for tight feedback loops.
- **Explicitness over cleverness**: Names are short, types are readable, and workflows are designed to be traced.
