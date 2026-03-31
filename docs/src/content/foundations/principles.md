---
title: "Principles"
description: "The design values that shape Nanyx"
order: 1
---

Nanyx is guided by a small set of design principles. These are not rules for every program, but they inform the language defaults and the standard library shape.

## Clarity over cleverness

Prefer code that explains itself. A few extra characters are worth it when they reduce mental translation or prevent ambiguity.

## Min-gen

Nanyx tries to provide a language that feels simultaneously high-level and powerful, but also small and simple. It does this by providing features that are general and composable, rather than adding special syntax or constructs for specific cases. This keeps the language surface minimal while still enabling a wide range of patterns.

## Explicit data and errors

Represent meaning in the type. Use tag unions and descriptive cases instead of magic values or implicit failure.

## Composable by default

Favor small, orthogonal pieces that combine cleanly: pipelines, builders, and first-class functions that fit together without ceremony.

## Consistency beats special cases

If two features overlap, prefer a single predictable rule. Consistency lowers the learning curve and makes tools easier to build.

## Gradual feedback, not blocked flow

Surface type information and diagnostics early, but keep iteration fast. The compiler should inform you without getting in the way of exploration. Unlike many languages, Nanyx does not block execution on most errors*, even name or type errors. Instead, it surfaces diagnostics whilst still allowing you to run your code, making it easier to iterate and learn.

* In debug mode

## Effect transparency

Side effects should be visible in types and in code structure. Contexts and workflows make effects explicit without making them heavyweight.

## Minimal surface, maximal leverage

Add features sparingly and keep syntax small. If a feature is powerful, it should work broadly and reduce the need for additional ones.

## Performance you can reason about

Defaults should be efficient and predictable. When performance matters, the language should give you clear paths to control it.

## Tooling first

Design for great editor support, readable error messages, and a smooth debugging experience. The language should work with you, not against you.
