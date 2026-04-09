---
title: "Attributes"
description: "Metadata annotations such as @entry and @workflow"
order: 13
---

Attributes are annotations that start with `@` and modify how the compiler treats the declaration that follows.

## Basic syntax

Place an attribute immediately before the declaration it applies to:

```nanyx
@entry module main
```

```nanyx
@workflow
def optionChain = (...)
```

A declaration can have one or more attributes, each on its own line.

## Common attributes

### `@entry`

Marks the program entry module in project-based apps.

```nanyx
@entry module main
```

See [Hello, World!](../getting-started/hello-world.md).

### `@workflow`

Marks a workflow builder definition and enables workflow helper generation.

```nanyx
@workflow
def optionChain = (
  def try = { ... }
  def unit = #none
)
```

See [Option chaining](../cookbook/option-chaining.md) and [Workflows](../advanced/workflows.md).

### `@customKeywordType(...)`

Used in workflow builders to specify how a custom keyword behaves.

```nanyx
def async = (
  @customKeywordType(#bind)
  def await = { m, f -> ... }
)
```

See [Workflows](../advanced/workflows.md).

### `@baseUnit`

Marks a unit declaration as a base/reference unit for unit-of-measure definitions.

```nanyx
@baseUnit unit(s) Seconds of number = 10_000_000ticks
```

See [Units of measure](../advanced/units-of-measure.md).

### `@builderPattern`

Marks types intended for builder-style initialization.

```nanyx
@builderPattern
type Point = (x: int, y: int)
```

See [Higher-order functions](./lambdas-and-higher-order-functions.md).

## Notes

- Attributes are compile-time metadata.
- Their meaning is defined by the compiler and language features that consume them.
- Prefer using documented attributes and patterns from the language guides.
