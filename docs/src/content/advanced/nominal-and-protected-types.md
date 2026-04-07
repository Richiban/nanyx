---
title: "Nominal and Protected Types"
description: "Nominal identities and opaque boundaries"
order: 3
---

Nanyx is—by default—a structurally-typed language, but sometimes a type's name matters more than its structure. To support these scenarios, Nanyx programs can define _nominal_ types using the `@` prefix. 

Nominal types have an identity that is distinct from their shape, which helps prevent accidental misuse when multiple values share a similar structure. They are very commonly used to wrap primitives—like strings and numbers—to create domain-specific types with invariants or validation.

## Nominal Types

A nominal type cannot be constructed (or cloned) outside its home module. Export a constructor function to create values safely.

```nanyx
module user

export type @User = (
  id: UserId
  name: string
)

export def @User.new: (UserId, string) -> Result(@User, list(string)) = { id, name ->
  memory {
    def errors = mut []
    if id <= 0 then errors += "ID must be positive"
    if name.length == 0 then errors += "Name cannot be empty"
    
    if errors.length > 0 then
      #error(errors)
    else
      #ok(@User(id = id, name = name))
  }
}
```

### Assignability

Nominal types are not assignable to each other, even if their structures are identical. They are, however assignable to their underlying shape for compatibility with existing code.

So, given the following definition of `@UserId`:

```nanyx
type @UserId = int
```

Then `@UserId` is assignable to `int` but `int` is not assignable to `@UserId`.

If you wish a nominal type to be completely opaque and not assignable to its underlying shape, you can use a protected type instead (see below).

## Protected (opaque) types

Protected types go further by hiding their structure completely outside their home module using the `private` keyword. This is useful for wrapping primitives safely and enforcing validation.

```nanyx
module ids

export type @UserId = private string

export def UserId.new: string -> Result(@UserId, string) = { value ->
  if value.length == 0 then
    #error("UserId cannot be empty")
  else
    #some(@UserId(value))
}
```

Outside the module, callers can pass around `@UserId` values but cannot construct or inspect the underlying string directly.

## Constructors and accessors

Because protected types hide representation, you typically provide helpers that convert in and out of the type boundary.

```nanyx
module email

export type @Email = private string

export def Email?: string -> Option(@Email) = { value ->
  if value \contains("@") then
    #some(@Email(value))
  else
    #none
}

-- expose a read-only view of the underlying string
export def Email.value: @Email -> string = { @Email(e) -> e }
```

## Choosing between them

- Use nominal types when you want identity and explicit construction but still want to expose the shape.
- Use protected types when you want a hard abstraction boundary and / or guarantees that changing the internal representation of a type will not affect external users.

Combine nominal and protected types with contexts and workflows to make safe domain-specific APIs.
