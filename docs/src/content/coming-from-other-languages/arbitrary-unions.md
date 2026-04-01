---
title: "Arbitrary unions"
description: "Why Nanyx doesn't support types like String | Int"
order: 4
---

A common question from new users is: "Why can't I write a type like `String | Int`?" Nanyx deliberately does not support arbitrary unions of unrelated types, and instead restricts unions to tagged variants (e.g. `#some(Int) | #none`). This is a conscious design choice rooted in type inference, readability, and long-term maintainability.

## Type inference

The primary reason is type inference. Nanyx uses a Hindley-Milner style type system, which relies on types being predictable and composable. Arbitrary unions like `String | Int` quickly introduce ambiguity: if a function receives such a value, what operations are valid on it? Without explicit tagging, the compiler cannot reliably determine what code is safe without requiring pervasive runtime checks or annotations. By contrast, tagged unions carry explicit structure, so pattern matching remains precise and inference remains tractable.

## Clarity at the call site

Arbitrary unions tend to erode clarity at the call site. A value of type `String | Int` gives no semantic meaning — what does it represent? Why those two types? Tagged unions force the developer to name the cases (`#name(String) | #age(Int)`), which makes the intent explicit and self-documenting. This leads to better APIs and more maintainable code.

## Pattern matching and exhaustiveness

There is also a strong influence from Nanyx's emphasis on pattern matching and exhaustiveness. Tagged unions integrate naturally with `match`, allowing the compiler to verify that all cases are handled. With arbitrary unions, this guarantee becomes much weaker, as the cases are structural rather than semantic.

## Avoiding subtyping complexity

Restricting unions to tags avoids a class of subtle design problems around subtyping and equivalence. In languages that allow arbitrary unions, questions like "is `A | B` the same as `B | A`?" or "how do unions interact with generics?" quickly become complex. Nanyx sidesteps this entirely by making unions always explicit and nominal via tags.

## Summary

While `String | Int` might seem convenient at first glance, it tends to trade short-term convenience for long-term complexity. Nanyx favours explicitness, inference simplicity, and clear domain modelling through tagged unions instead.
