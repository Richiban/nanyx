---
title: "Attached definitions"
description: "Methods and properties attached to types"
order: 8
---

Attached definitions let you add functions and values directly onto a type. This is how you define methods, operators, and static-like members without classes.

## Attaching a method

Use dot syntax to attach a definition to a type. Inside the body, `this` refers to the instance:

```nanyx
type Point = (x: int, y: int)

export def Point.length = {
  Math.sqrt(this.x ** 2 + this.y ** 2)
}
```

Call it like a method:

```nanyx
def p = Point(x = 3, y = 4)

def len = p.length
```

## Attaching operators

Operators are just attached definitions with symbolic names:

```nanyx
export def Point.`+` = { a, b ->
  Point(x = a.x + b.x, y = a.y + b.y)
}
```

## Attached functions as typeclass defaults

Attached definitions can satisfy contexts or typeclass-style constraints. For example, defining `+` and `0` on a type can allow a `Sum` context to be synthesized.

```nanyx
def Point.`+` = { a, b -> Point(x = a.x + b.x, y = a.y + b.y) }

def Point.`0` = Point(x = 0, y = 0)
```

## Namespacing utilities

Attached definitions are also useful for namespacing helpers:

```nanyx
def Option.map
  : (#some(a) | rest), (a -> b) -> (#some(b) | rest)
  = { | #some(a), f -> #some(f(a))
      | other, _ -> other }
```

## When to use attached definitions

- You want method-style access without classes.
- You want to group helpers under a type name.
- You want to provide operators or context defaults for a type.
