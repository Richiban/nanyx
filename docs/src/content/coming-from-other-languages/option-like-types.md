---
title: "Why we avoid a single Option type"
description: "Using explicit tags instead of a generic Maybe"
order: 1
---

# Why we avoid a single Option type

Many languages use a null reference (`null`, `nil`, `None`, `undefined`). Nanyx avoids nulls and encourages explicit tag unions instead of relying on a single, universal Option/Maybe type. The goal is to make missing data precise and composable.

## Prefer results for failing operations

When an operation can fail, Nanyx conventions favor a result-like union with a specific error tag. That is more descriptive than a plain optional value, and it composes without conversion helpers.

```nanyx
def first: list(a) -> #some(a) | #err(#listWasEmpty) = ...
```

If all failing operations use result-like unions, you avoid bouncing between `Result` and `Option` representations.

## Optional record fields

Optional fields can be represented explicitly with tag unions or by using optional field syntax. This keeps intent clear and avoids a generic wrapper type where it adds little value.

## Use explicit unions for richer states

For data that is neither a simple failure nor a missing field, explicit tags are more informative than a generic `Option`:

```nanyx
artist: #loading | #loaded(Artist)

artist: #unspecified | #specified(Artist)
```

Both mean “you might not have an artist,” but the tags explain why and what to expect next.

## Easy evolution

Explicit unions make it easy to evolve the model over time. If you later need an error state, you can add it without invalidating the existing tags:

```nanyx
artist: #loading | #loaded(Artist) | #errored(LoadError)
```

Code that already handles `#loading` and `#loaded` keeps working, and the new case is explicit.

## Convention over a single type

Nanyx still supports option-like values via the `#some` convention and functions like `Option.map` that preserve other tags:

```nanyx
def Option.map
  : (#some(a) | rest), (a -> b) -> (#some(b) | rest)
  = { | #some(a), f -> #some(f(a))
      | other, _ -> other }
```

This gives you the ergonomics of options without forcing every absence into a single type.
