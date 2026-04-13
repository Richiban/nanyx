---
title: "Units and dimensions"
description: "Compile-time units for numbers"
order: 7
---

Nanyx supports units of measure for numeric types, allowing you to express time, sizes, and rates safely.

```nanyx
def lookback = 30days

def timeout = 3000ms

def maxFilesize = 50MB
```

This page defines the design direction for units, with two goals:

1. Unit-safe arithmetic with automatic unit derivation (for example `m / s -> m/s`)
2. Ergonomic value construction at call sites (for example `delay(500ms)`)

# Two concepts: dimensions and units

Nanyx should model **dimensions** and **units** separately:

- **Dimension**: the physical kind used by type checking (`Time`, `Length`, `Mass`, `Length/Time`)
- **Unit**: a named scale for a dimension (`ms`, `s`, `min`, `km`)

Type checking operates on dimensions. Units are syntax and conversion metadata for constructing and displaying values.

# Defining units

Units are always numeric in Nanyx. Unit declarations do not specify an underlying primitive type.

```nanyx
dimension Time
dimension Bytes

@baseUnit unit(s) Seconds: Time
unit(ms) Milliseconds: Time = 0.001s
unit(min) Minutes: Time = 60s
unit(hr) Hours: Time = 60min
unit(d) Days: Time = 24hr

@baseUnit unit(B) Byte: Bytes
unit(KB) Kilobyte: Bytes = 1024B
unit(MB) Megabyte: Bytes = 1024KB
```

## Base vs derived units

A dimension can define one or more base units.

- A unit marked with `@baseUnit` is a base unit.
- A unit declared with `=` is a derived unit.
- `=` never defines a base unit; it always means “defined in terms of other units”.

Every derived unit must resolve through a finite chain to at least one base unit of the same dimension.

```nanyx
dimension Time

@baseUnit unit(s) Seconds: Time
@baseUnit unit(tick) Tick: Time

unit(ms) Milliseconds: Time = 0.001s
unit(min) Minutes: Time = 60s
```

Cycles are invalid and must be rejected by the compiler:

```nanyx
unit(a) A: Time = 2b
unit(b) B: Time = 3a
-- error: cyclic derived-unit definition (A -> B -> A)
```

## Design model

Each measured numeric type carries a compile-time _dimension vector_.

- `number` is dimensionless (`1`)
- Base dimensions are independent (for example `Length`, `Time`, `Mass`)
- Units map to a scale within one dimension

Conceptually:

- `number(Length)` means a quantity of length
- `number(Time)` means a quantity of time
- `number(Length/Time)` is speed
- `number(Mass*Length/Time^2)` is force

The compiler stores normalized dimensions (sorted bases + integer exponents), so equivalent forms unify:

- `Length/Time` == `Length*Time^-1`
- `(Length/Time)*Time` == `Length`

# Derived units

```nanyx
def transferRate = 50(MB / s)
```

# Type rules (F#-style behavior)

Dimensions participate in type inference and unification, not runtime values.

For arithmetic operators on measured numeric values:

- `+` and `-` require identical dimensions
- `*` adds exponents (`d1 * d2`)
- `/` subtracts exponents (`d1 / d2`)
- `pow(x, n)` multiplies exponents by `n` (for integer `n`)

Examples:

```nanyx
def d = 100m
def t = 9.58s
def v = d / t      -- inferred: number(Length/Time)
def a = v / t      -- inferred: number(Length/Time^2)
```

Addition mismatch is rejected:

```nanyx
def bad = 10m + 2s
-- error: cannot add number(Length) and number(Time)
```

## Generic constraints over dimensions

Functions can stay generic over dimensions:

```nanyx
def square: number(d) -> number(d^2) = { x -> x * x }
def avgSpeed: (number(Length), number(Time)) -> number(Length/Time) = { dist, time -> dist / time }
```

This keeps APIs reusable while preserving dimension correctness.

# Conversion rules

Unit declarations define scale factors between units of the same dimension.

```nanyx
@baseUnit unit(s) Seconds: Time
unit(ms) Milliseconds: Time = 0.001s
unit(min) Minutes: Time = 60s
```

Conversions are compile-time checked and only allowed within equivalent dimensions:

```nanyx
def timeout: number(Time) = 500ms
def twoMinutes = 120s as min     -- explicit conversion syntax (proposed)
```

Invalid conversion is rejected:

```nanyx
def impossible = 1m as s
```

# Ergonomics for `delay(500ms)`

Use unit suffix literals as typed numeric constructors.

- `500ms` parses as numeric literal `500` with unit suffix `ms`
- Type is inferred as `number(Time)` (carrying source unit metadata for conversions/formatting)
- Works anywhere a numeric expression works

```nanyx
type Duration = number(Time)

def delay: Duration -> () = { d -> ... }

delay(500ms)      -- ok
delay(0.5s)       -- ok
delay(2min)       -- ok
delay(42)         -- error: expected Duration, found dimensionless number
```

# Function parameters: dimensions vs units

Both are useful, but they should mean different things.

- **Default**: functions accept a dimension (`number(Time)`, `number(Length/Time)`, etc.)
- **Optional strict mode**: functions can request an exact unit when protocol/interop requires it (for example milliseconds only)

Dimension-first API (recommended):

```nanyx
def delay: number(Time) -> () = { d -> ... }

delay(500ms)
delay(0.5s)
delay(2min)
```

Unit-specific API (proposed constraint form):

```nanyx
def setTimeoutRaw: number(Time) where unit = ms -> () = { d -> ... }

setTimeoutRaw(500ms)   -- ok
setTimeoutRaw(1s)      -- error (or require explicit `as ms`)
```

Recommendation: keep most APIs dimension-based, and reserve unit-specific parameters for boundaries (wire formats, hardware APIs, legacy contracts).

## Why this satisfies both goals

- Goal 1: unit algebra happens in inference/unification, so results derive automatically
- Goal 2: suffix literals construct measured values directly at call sites

# Runtime model

Dimensions and units are erased after type checking.

- Runtime carries only the underlying numeric representation
- No runtime penalty for unit checking
- Optional debug metadata can be emitted by tooling, not required by execution

# Recommended implementation phases

1. **Core type algebra**: represent normalized dimension vectors and unit unification
2. **Operator typing**: enforce `+/-` compatibility and `*//` exponent arithmetic
3. **Unit declarations + conversion table**: resolve aliases/scales to base dimensions
4. **Suffix literals**: parse and type unit-suffixed numeric literals
5. **Diagnostics**: friendly errors and suggested conversions
