---
title: "Nominal and Protected Types"
description: "Nominal identities and opaque boundaries"
order: 3
---

# Nominal and Protected Types

Sometimes a type's name matters more than its structure. Nanyx supports nominal types using the `@` prefix. Nominal types have an identity that is distinct from their shape, which helps prevent accidental misuse when multiple values share a similar structure. They are very commonly used to wrap primitives--like strings and numbers--to create domain-specific types with invariants.

## Nominal Types

A nominal type cannot be constructed (or cloned) outside its home module. Export a constructor function to create values safely.

```nanyx
module user

export type @User = (
  id: UserId
  name: string
)

export def makeUser: (UserId, string) -> Result(@User, list(string)) = { id, name ->
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

### Why Nominal?

- Prevents mixing values that happen to share the same fields.
- Allows you to change the internal shape later without breaking callers.
- Makes invariants explicit through the constructor.

## Protected (Opaque) Types

Protected types hide their structure outside the module by using `private`. This is useful for wrapping primitives safely and enforcing validation.

```nanyx
module ids

export type @UserId = private string

export def UserId: string -> @UserId = { value ->
  if value.length == 0 then
    panic("UserId cannot be empty")
  else
    @UserId(value)
}
```

Outside the module, callers can pass around `@UserId` values but cannot construct or inspect the underlying string directly.

## Constructors and Accessors

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

export def Email.value: @Email -> string = { e ->
  -- expose a read-only view of the underlying string
  string(e)
}
```

## Choosing Between Them

- Use nominal types when you want identity and explicit construction but still want to expose the shape.
- Use protected types when you want a hard abstraction boundary and validation on every construction.
- Combine with contexts and workflows to make safe domain-specific APIs.
