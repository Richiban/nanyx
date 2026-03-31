---
title: "Principles"
description: "The design values that shape Nanyx"
order: 1
---

Nanyx is guided by a small set of design principles. These are not rules for every program, but they inform the language defaults and the standard library shape.

## Human-readable errors

In the spirit of Elm and Rust, Nanyx aims to have human readable and understandable compiler messages. Messages should describe the problem in detail and provide information about the context, including suggestions for how to correct the problem.

## Illegal states should be unrepresentable

We believe that a language should make it easy to make illegal states unrepresentable. For example, tag unions can be used to precisely define the possible values of a type. In Nanyx, in the future, we want to take this a step further, and allow refinement of some types. For example, to express that some value must not only be an integer, but also that it must fall within a range, e.g. [0-99].

## Min-gen

Nanyx tries to provide a language that feels simultaneously high-level and powerful, but also small and simple. It does this by providing features that are general and composable, rather than adding special syntax or constructs for specific cases. This keeps the language surface minimal while still enabling a wide range of patterns.

## No warnings

The Nanyx compiler never emits warnings; only compile-time errors. Warnings can be ignored or turned off, and contributors may disagree on whether a warning should be heeded or not. 

## The compiler is not a linter

The compiler's job is to check types and surface errors, not to enforce style or best practices. Nanyx's design encourages good style by making it easy to write clear code, but it does not enforce it with warnings or errors. This allows for flexibility and personal preference in coding style while still providing strong type safety and error checking. We do not believe that a developer should be blocked from running their code because they commented a line out and were left with a now-unused variable.

Instead, the Nanyx compiler follows the [dotnet analyzer model](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview?tabs=net-10), where linting, style rules, and other code quality checks are implemented as separate tools that can be plugged into the compiler and run during compilation.

## Explicit data and errors

Represent meaning in the type. Use tag unions and descriptive cases instead of magic values or implicit failure.

## Composable by default

Favor small, orthogonal pieces that combine cleanly: pipelines, builders, and first-class functions that fit together without ceremony.

## Consistency beats special cases

If two features overlap, prefer a single predictable rule. Consistency lowers the learning curve and makes tools easier to build.

## Gradual feedback, not blocked flow

Surface type information and diagnostics early, but keep iteration fast. The compiler should inform you without getting in the way of exploration. Unlike many languages, Nanyx does not block execution on most errors*, even name or type errors. Instead, it surfaces diagnostics whilst still allowing you to run your code, making it easier to iterate and learn.

* In debug mode. To complete a Release build there must be no errors

## Effect transparency

Side effects should be visible in types and in code structure. Contexts and workflows make effects explicit without making them heavyweight.

## One language

Nanyx is one programming language. The Nanyx compiler does not have feature flags or compiler plugins that change or extend the semantics of the language. We want to avoid fragmentation in the ecosystem where programs end up being written in different "dialects" of the language. There is one language, now and forever. Of course that does not imply that the language will not evolve over time.


## Minimal surface, maximal leverage

Add features sparingly and keep syntax small. If a feature is powerful, it should work broadly and reduce the need for additional ones.

## Performance you can reason about

Defaults should be efficient and predictable. When performance matters, the language should give you clear paths to control it.

Be cautious with compiler optimizations that can make program performance less predictable. We don't want developers to accidentally tank the performance of their code by making a seemingly innocuous change that blocks a complex optimization. Instead, we prefer to provide clear, explicit tools for performance when needed, while keeping the common case straightforward and efficient.

## Tooling first

The deciding factor in whether or not a language gets adopted is tooling. Design for great editor support, readable error messages, and a smooth debugging experience. The language should work with you, not against you.

