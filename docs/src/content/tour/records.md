---
title: "Records and tuples"
description: "Structural records, tuples, and field rules"
order: 2.5
---

Records and tuples are a core way to model data in Nanyx; they allow you to group related values together with fields on a single object. 

Fields can be either _positional_ or _named_, meaning that you can mix and match named and positional fields in the same value, as long as all the positional fields come first.

Records and tuples are actually one unified feature in Nanyx; a tuple is just a special case of record where all the fields are positional.

## Record values

Create a record by listing its fields and values inside parentheses. For named fields, use `name = value`:

```nanyx
-- Record with named fields
def alice = (
  name = "Alice"
  age = 30
  email = "alice@example.com"
)
```

You can also use positional fields:

```nanyx
-- Record with positional fields (aka a tuple)
def point = (10, 20)
```

Because records and tuples are unified, both styles can be combined in one value:

```nanyx
-- Mixed record with both positional and named fields
def mixed = (1, 2, label = "origin", unit = "px")
```

## Record types

A record type uses the same shape as a record value, but with type annotations on fields:

```nanyx
def config: (host: string, port: int) = (
    host = "localhost"
    port = 8080
)
```

When you use the same structure in multiple places, define a named type alias:

```nanyx
type Person = (
  name: string
  age: int
  email: string
)
```

Then reference that alias in annotations:

```nanyx
def alice: Person = (
  name = "Alice"
  age = 30
  email = "alice@example.com"
)
```

## Structural typing

Record types are structural, meaning compatibility is based on shape rather than explicit inheritance. If two types have compatible fields, they can be used interchangeably where appropriate.

```nanyx
type Named = (name: string)
type Person = (name: string)

def john: Named = Person("John")
```

You can also compose record requirements with intersections:

```nanyx
type Person = Named & (age: int)
```

## Optional fields

Records can contain optional members when a field may be absent:

```nanyx
type Person = (name: string; petsName?: string)
```

Optional fields can also be modeled explicitly with tag unions if you need stricter control.

## Records and tuples

In Nanyx, tuples and records are one unified structure. A tuple is just a record whose fields are all positional.

This is why you can mix positional and named fields in the same value:

```nanyx
def mixed = (1, 2, label = "origin", unit = "px")
```

The one ordering rule is: all positional fields must come before any named fields.

```nanyx
-- Valid
def valid = (10, 20, x = 10, y = 20)

-- Invalid (named field before positional field)
-- def invalid = (x = 10, 20)
```

For the broader type-system overview, see [Types](./types.md).
