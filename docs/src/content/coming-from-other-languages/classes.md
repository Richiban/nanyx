---
title: "Classes"
description: "Object-style programming without inheritance"
order: 2
---

Nanyx does not have classes or inheritance, but you can model objects and encapsulated state using records and closures.

## Constructor-style records

```nanyx
type Point = { x: int, y: int ->
  ( x
    y
    move = { dx: int, dy: int ->
      Point(
        x = x + dx
        y = y + dy
      )
    }
  )
}
```

## Uppercase constructors

If a definition starts with an uppercase name, it generates a type with the same name:

```nanyx
type Counter = { count: int ->
  mut count = count

  ( increment = { count += 1 }
    decrement = { count -= 1 }
    count = { count }
  )
}
```

## Encapsulated state

```nanyx
type Person = { name, age ->
  mut age = age

  ( name = name
    age = { age }
    setAge = { newAge -> set age = newAge }
    toString = { "{name} is {age} years old" }
  )
}
```

This style keeps implementation details private while exposing a controlled API.
