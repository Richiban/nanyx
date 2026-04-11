---
title: "Units of measure"
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

## Defining units

```nanyx
unit(t) ticks of int

@baseUnit unit(s) Seconds of number = 10_000_000ticks
unit(min) Minutes of number = 60s
unit(hr) Hours of number = 60min
unit(d) Days of number = 24hr

unit B(int) = 8b

unit(B) Bytes = int
```

### Design model

Each numeric type can carry a _dimension vector_ at compile time.

- `number` is dimensionless (`1`)
- Base units introduce independent dimensions (for example `m`, `s`, `kg`)
- Derived units are aliases that reduce to powers of base dimensions

Conceptually:

- `m` is `m^1`
- `s` is `s^1`
- `m/s` is `m^1 * s^-1`
- `N` (newton) can be declared as `kg*m/s^2`

The compiler stores normalized dimensions (sorted bases + integer exponents), so equivalent forms unify:

- `m/s` == `m*s^-1`
- `(m/s)*s` == `m`

## Derived units

```nanyx
def transferRate = 50(MB / s)
```

## Type rules (F#-style behavior)

Units participate in type inference and unification, not runtime values.

For arithmetic operators on measured numeric values:

- `+` and `-` require identical units
- `*` adds exponents (`u1 * u2`)
- `/` subtracts exponents (`u1 / u2`)
- `pow(x, n)` multiplies exponents by `n` (for integer `n`)

Examples:

```nanyx
def d = 100m
def t = 9.58s
def v = d / t      -- inferred: number<m/s>
def a = v / t      -- inferred: number<m/s^2>
```

Addition mismatch is rejected:

```nanyx
def bad = 10m + 2s
-- error: cannot add number<m> and number<s>
```

### Generic constraints over units

Functions can stay generic over unit dimensions:

```nanyx
def square: number<u> -> number<u^2> = { x -> x * x }
def avgSpeed: (number<d>, number<t>) -> number<d/t> = { dist, time -> dist / time }
```

This keeps APIs reusable while preserving dimension correctness.

## Conversion rules

Unit declarations define scale factors between aliases of the same dimension.

```nanyx
@baseUnit unit(s) Seconds of number
unit(ms) Milliseconds of number = 0.001s
unit(min) Minutes of number = 60s
```

Conversions are compile-time checked and only allowed within equivalent dimensions:

```nanyx
def timeout: number<s> = 500ms   -- implicit normalize to seconds if needed
def twoMinutes = 120s as min     -- explicit conversion syntax (proposed)
```

Invalid conversion is rejected:

```nanyx
def impossible = 1m as s
```

## Ergonomics for `delay(500ms)`

Use unit suffix literals as typed numeric constructors.

- `500ms` parses as numeric literal `500` with unit suffix `ms`
- Type is inferred as `number<ms>` (normalized to base dimension internally)
- Works anywhere a numeric expression works

```nanyx
type Duration = number<s>

def delay: Duration -> () = { d -> ... }

delay(500ms)      -- ok
delay(0.5s)       -- ok
delay(2min)       -- ok
delay(42)         -- error: expected Duration, found dimensionless number
```

### Why this satisfies both goals

- Goal 1: unit algebra happens in inference/unification, so results derive automatically
- Goal 2: suffix literals construct measured values directly at call sites

## Runtime model

Units are erased after type checking.

- Runtime carries only the underlying numeric representation
- No runtime penalty for unit checking
- Optional debug metadata can be emitted by tooling, not required by execution

## Recommended implementation phases

1. **Core type algebra**: represent normalized dimension vectors and unit unification
2. **Operator typing**: enforce `+/-` compatibility and `*//` exponent arithmetic
3. **Unit declarations + conversion table**: resolve aliases/scales to base dimensions
4. **Suffix literals**: parse and type unit-suffixed numeric literals
5. **Diagnostics**: friendly errors and suggested conversions
