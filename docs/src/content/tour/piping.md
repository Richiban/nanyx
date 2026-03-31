---
title: "Piping"
description: "Readable left-to-right composition"
order: 9
---

Nanyx supports a pipe operator that sends the left value into the first argument of a function: `x \f(...)`. This makes pipelines easy to read and compose.

```nanyx
def getStreamFromInput: Request -> Stream(Byte) = ...
def getBytesFromStream: Stream(Byte) -> string = ...
def serialisePerson: string -> Person = ...

def personObj = request
  \getStreamFromInput
  \getBytesFromStream
  \serialisePerson
```

## Piping with extra arguments

The piped value fills the first parameter. Other parameters are supplied normally:

```nanyx
def add = { x, y -> x + y }

def result = 1
  \add(2)
  \add(3)
```

## Qualified piping

If the function lives on the same module as the value type, you can omit the module qualifier:

```nanyx
def result = [1, 2, 3] \List.map { + 1 }

def result = [1, 2, 3] \map { + 1 }
```
