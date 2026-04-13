---
title: "Piping"
description: "Readable left-to-right composition"
order: 9
---

Nanyx supports a pipe operator that sends the value on the left into the first argument of a function: `x \f(...)`.

The problem with nested function calls is that they must be read from the inside out, which can be hard to follow:

```nanyx
def getStreamFromInput: Request -> Stream(Byte) = ...
def getBytesFromStream: Stream(Byte) -> string = ...
def serialisePerson: string -> Person = ...

def personObj = getStreamFromInput(getBytesFromStream(serialisePerson(request)))
```

Piping makes sequences of function calls easy to read—since functions are written in the order they are executed—and is the preferred style for expressions containing nested function calls:

```nanyx
def personObj = request
  \getStreamFromInput
  \getBytesFromStream
  \serialisePerson
```

# Piping with extra arguments

The piped value fills the first parameter. Other parameters are supplied normally:

```nanyx
def add = { x, y -> x + y }

def result = 1
  \add(2)
  \add(3)
```

# Qualified piping

If the function is [attached](../advanced/attached-definitions) to the type on the left-hand side of the `\`, you can omit the module qualifier:

```nanyx
-- The two lines below are equivalent
def result = [1, 2, 3] \List.map { + 1 }
def result = [1, 2, 3] \map { + 1 }
```
