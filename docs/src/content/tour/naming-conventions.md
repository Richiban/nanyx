---
title: "Naming conventions"
description: "Recommended casing and identifiers"
order: 15
---

Nanyx uses camel case for values, functions, modules and tags, and Pascal case for types.

| Kind | Case | Examples |
|------|------|----------|
| Values and functions | camelCase | `message`, `sayHello`, `userCount` |
| Modules | camelCase | `userManagement`, `collections.list` |
| Types and contexts | PascalCase | `Greeter`, `Console`, `$UserId` |
| Tags | camelCase after `#` | `#ok`, `#notFound` |
| Custom pattern tags | PascalCase after `#` | `#Hsl`, `#Even` |

```nanyx
-- Value

def message = "Hello world"

-- Function

def sayHello = { println("Hello!") }

-- Type

type Greeter = (greet: () -> ())
```

Because a record type alias is callable as a constructor (see [Constructing records from a type alias](./records#constructing-records-from-a-type-alias)), a Pascal-cased name in expression position is almost always such a constructor: `Point(x = 1, y = 2)`.

Custom patterns use Pascal case after the `#` so that they are visually distinct from ordinary tags; see [Custom patterns](../advanced/custom-patterns).
