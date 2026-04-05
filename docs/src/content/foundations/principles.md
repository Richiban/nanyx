---
title: "Principles"
description: "The design values that shape Nanyx"
order: 1
---

Nanyx is guided by a small set of design principles. These are not rules for every program, but they inform the language defaults and the standard library shape.

## Human-readable errors

In the spirit of Elm and Rust, Nanyx aims to have human readable and understandable compiler messages. Messages should describe the problem in detail and provide information about the context, including suggestions for how to correct the problem.

## Illegal states should be unrepresentable

We believe that a language should make it easy to make illegal states unrepresentable. For example, tag unions can be used to precisely define the possible values of a type.

## No warnings

The Nanyx compiler never emits warnings; only compile-time errors. Warnings can be ignored or turned off, and contributors may disagree on whether a warning should be heeded or not. 

## The compiler is not a linter

The compiler's job is to check types and surface errors, not to enforce style or best practices. Nanyx's design encourages good style by making it easy to write clear code, but it does not enforce it with warnings or errors. This allows for flexibility and personal preference in coding style while still providing strong type safety and error checking. We do not believe that a developer should be blocked from running their code because they commented a line out and were left with a now-unused variable.

Instead, the Nanyx compiler follows the [dotnet analyzer model](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview?tabs=net-10), where linting, style rules, and other code quality checks are implemented as separate tools that can be plugged into the compiler and run during compilation.

## Explicit data and errors

Represent meaning in the type. Use tag unions and descriptive cases instead of magic values or implicit failure.

## Minimal surface, maximal leverage

Nanyx tries to provide a language that is high-level and powerful, but also _feels small and simple_. It does this by providing features that are general and composable, rather than adding special syntax or constructs for specific cases. This keeps the language surface minimal while still enabling a wide range of patterns.

Favor small, orthogonal pieces that combine cleanly: pipelines, builders, and first-class functions that fit together without ceremony. 

Add features sparingly and keep syntax small. If a feature is powerful, it should work broadly and reduce the need for additional ones. Consistency lowers the learning curve and makes tools easier to build.

Operators and symbols should always have a single, clear meaning. For example, braces are always a function definition, not a record or a block of statements.

## Gradual feedback, not blocked flow

Surface type information and diagnostics early, but keep iteration fast. The compiler should inform you without getting in the way of exploration. Unlike many languages, Nanyx does not block execution on most errors†, even name or type errors. Instead, it surfaces diagnostics whilst still allowing you to run your code, making it easier to iterate and learn.

† In debug mode. To complete a Release build there must be no errors

## Effect transparency

Side effects should be visible in types and in code structure. Contexts and workflows make effects explicit without making them heavyweight.

## One language

Nanyx is one programming language. The Nanyx compiler does not have feature flags or compiler plugins that change or extend the semantics of the language. We want to avoid fragmentation in the ecosystem where programs end up being written in different "dialects" of the language. There is one language, now and forever. Of course that does not imply that the language will not evolve over time.

## Performance you can reason about

Defaults should be efficient and predictable. When performance matters, the language should give you clear paths to control it.

Be cautious with compiler optimizations that can make program performance less predictable. We don't want developers to accidentally tank the performance of their code by making a seemingly innocuous change that blocks a complex optimization. Instead, we prefer to provide clear, explicit tools for performance when needed, while keeping the common case straightforward and efficient.

## Tooling first

The deciding factor in whether or not a language gets adopted is tooling. Design for great editor support, readable error messages, and a smooth debugging experience. The language should work with you, not against you.

## No global state

In Nanyx there is no global shared state. This avoids a plethora of issues, including difficulties with initialization order and race conditions in the presence of concurrency. A Nanyx programmer is free to construct some state in the main function and pass it around, but there is no built-in mechanism to declare global variables. In a real system, the programmer still has to deal with the state of the world, e.g. the state of the file system, the network, and other resources.

## No overloading

Nanyx does not have function overloading. Each function has a unique name, and the type system is powerful enough to express different behaviors without needing overloading. This keeps the language simpler and avoids ambiguity in function calls. If you need different behavior based on types, you can use pattern matching or tag unions to achieve that without overloading. See [Overloading](../coming-from-other-languages/overloading) for details.

## Exhaustive pattern matches

The Nanyx compiler enforces that pattern matches handle all cases of an algebraic data type. If a match expression is found to be non-exhaustive, the program is rejected. We believe this encourages more robust code and enables safer refactoring of algebraic data types.

## Separate pure and impure code

Nanyx supports functional, imperative, and logic programming. The type and effect system of Nanyx cleanly and safely separates pure code from impure code. That is, if a function is pure then the programmer can trust that the function behaves like a mathematical function: it returns the same value when given the same arguments and it has no side-effects.
