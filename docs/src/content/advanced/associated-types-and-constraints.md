---
title: "Associated types and constraints"
description: "Ergonomic generic APIs with context capabilities"
order: 12
---

Nanyx contexts can model typeclass-style capabilities. For generic APIs, you can pair constraints with associated-type members to avoid noisy signatures.

## Why associated types?

A common pain point with generic constraints is having to carry both container type and element type everywhere:

```nanyx
def iterStrings: [Iter(c, a)] c -> () = { items -> ... }
```

Associated types let you constrain just the container type and derive related types from the context:

```nanyx
context Iter(c) = (
  elem: type
  next: c -> #some(elem, c) | #done
)
```

## Constraining by associated type

Once `elem` is an associated type on `Iter(c)`, functions can constrain it directly:

```nanyx
def iterStrings: [Iter(c): (elem = string)] c -> () = { items ->
  for items | item -> dbg(item)
}
```

This keeps signatures compact while preserving full type safety.

## Layering collection capabilities

You can build a hierarchy of constraints by composing contexts:

```nanyx
context Iter(c) = (
  elem: type
  next: c -> #some(elem, c) | #done
)

context Sized(c) = (
  length: c -> int
)

context Indexed(c) = (
  elem: type
  get: c, int -> elem
)
```

Then require only what each function needs:

```nanyx
def countItems: [Sized(c)] c -> int = { items ->
  length(items)
}

def headOr: [Iter(c)] (c, Iter(c).elem) -> Iter(c).elem = { items, fallback ->
  match next(items)
  | #some(value, _) -> value
  | #done -> fallback
}
```

## Practical guidance

- Prefer capability-focused contexts (`Iter`, `Sized`, `Indexed`) over concrete collection constraints.
- Keep associated types on the context that owns the operation.
- Avoid over-constraining: require only the operations a function actually uses.
- Start with simple constraints in public APIs and add richer ones as needed.

For fundamentals, see [Contexts](./contexts). For collection overview, see [Collections](../tour/collections).
