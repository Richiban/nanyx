---
title: "Attached definitions"
description: "Methods and properties attached to types"
order: 8
---

Attached definitions let you add functions and values directly onto a type. This is how you define methods, operators, and static-like members without classes.

# Attaching a function

Use dot syntax to attach a definition to a type.

```nanyx
type Point = (x: int, y: int)

def Point.length = { p ->
  Math.sqrt(p.x ** 2 + p.y ** 2)
}
```

Attached functions are inferred exactly like any other function; attaching a definition to a type says nothing about the types of its parameters. In the example above, `p` is inferred to be a `Point` because the body accesses `p.x` and `p.y`.

Call it in a qualified way like any other function:

```nanyx
def p = Point(x = 3, y = 4)

def len = Point.length(p)
```

Or use it in a pipeline without needing to specify the type name:

```nanyx
def len = p \length
```

# Dot access

Dot syntax on a value resolves in two steps. `x.f` means the field `f` if the type of `x` has one; otherwise it means the attached definition `T.f(x)`, where `T` is the type of `x`. Any further arguments are passed after the receiver, so `x.f(args)` is `T.f(x, args)`.

The pipe operator resolves the same way, so the following three expressions are the same call:

```nanyx
def len1 = Point.length(p)
def len2 = p \length
def len3 = p.length
```

Prefer the dot form for a single access, and the pipe form for chains of calls (see [Piping](../tour/piping)).

# Attaching operators

Operators are just attached definitions with symbolic names:

```nanyx
export def Point.`+` = { a, b ->
  Point(x = a.x + b.x, y = a.y + b.y)
}
```

The built-in arithmetic and comparison operators on `int`, `float` and the other primitive types are attached definitions in exactly the same way; see [Operators](../tour/operators).

# Attached functions as typeclass defaults

Attached definitions can satisfy contexts or typeclass-style constraints. For example, defining `+` and `0` on a type can allow a `Sum` context to be synthesized.

```nanyx
def Point.`+` = { a, b -> Point(x = a.x + b.x, y = a.y + b.y) }

def Point.`0` = Point(x = 0, y = 0)
```

# Namespacing utilities

Attached definitions are also useful for namespacing helpers:

```nanyx
def Option.map
  : ((#some(a) | r), (a -> b)) -> (#some(b) | r)
  = { | #some(a), f -> #some(f(a))
      | other, _ -> other }
```

# When to use attached definitions

- You want method-style access in pipes.
- It feels like these definitions *belong* to the type.
- You want to provide operators or context defaults for a type.
