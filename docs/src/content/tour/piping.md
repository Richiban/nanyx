---
title: "Piping"
description: "Readable left-to-right composition"
order: 9
---

Nanyx supports a pipe operator that sends the value on the left into the first argument of a function: `x \f(...)`.

# Why piping?

In any programming language, if the user wishes to call a series of functions on a value (passing the result of one function to the next), they can nest the function calls. The problem with nested function calls is that the functions execute from the inside out and must therefore be read backwards (from right to left), which can be hard to follow:

```nanyx
def getStreamFromInput: Request -> Stream(Byte) = ...
def getBytesFromStream: Stream(Byte) -> string = ...
def serialisePerson: string -> Person = ...

def personObj = getStreamFromInput(getBytesFromStream(serialisePerson(request)))
```

Piping makes sequences of function calls easy to readpby allowing the functions to be written in the order they are executed—and is the preferred style for expressions containing nested function calls:

```nanyx
def personObj = request
  \getStreamFromInput
  \getBytesFromStream
  \serialisePerson
```

# Piping with extra arguments

The piped value will be combined with any additional written arguments into a record to form the argument of the function. For example, `x \f(y)` is equivalent to `f(x, y)` and `x \f(y, z)` is equivalent to `f(x, y, z)` etc. This allows you to pipe into functions that take more than one argument and, unlike curried functions, the argument order remains the same as the non-piped version. 

```nanyx
def add = { x, y -> x + y }

def result = 1
  \add(2)
  \add(3)
```

## Piping with trailing lambdas

If the last argument of a function is a lambda, you can write it after the piped value without parentheses:

```nanyx
def result = [1, 2, 3]
  \map { + 1 }
```

The piping and trailing lambda syntax can be combined with extra arguments as well:

```nanyx
def result = [1, 2, 3] \fold(0) { + }
```

This is a relatively simply syntax transformation, which becomes

```nanyx
def result = fold([1, 2, 3], 0, { + })
```

# Qualified piping

If the function is [attached](../advanced/attached-definitions) to the type on the left-hand side of the `\`, the function does not need to be qualified with the module or type name:

```nanyx
-- In regular function synax we must qualify the function with the module name
def parts = String.split("hello world", " ")

-- With piping, we can omit the module name because we know the type of the first argument before we need to identify the function
def parts = "hello world"\split(" ")
```

Note that the function can still be qualified in a pipe expression if desired; it might also be required if the type of the first argument has not yet been determined by the time the function is identified:

```nanyx
def f = { x ->
  x\foo -- Error: cannot resolve function 'foo' because the type of 'x' is not yet known
  x\SomeType.foo -- OK: the function is qualified, so it can be resolved 
}
```