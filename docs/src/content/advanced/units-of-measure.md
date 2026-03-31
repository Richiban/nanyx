---
title: "Units of measure"
description: "Compile-time units for numbers"
order: 7
---

# Units of measure

Nanyx supports units of measure for numeric types, allowing you to express time, sizes, and rates safely.

```nanyx
def lookback = 30days

def timeout = 3000ms

def maxFilesize = 50MB
```

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

## Derived units

```nanyx
def transferRate = 50(MB / s)
```
