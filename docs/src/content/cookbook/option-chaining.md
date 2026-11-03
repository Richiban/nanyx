---
title: Option chaining
description: Implement a short-circuiting workflow for option-like values
order: 4
---

Let's build our understanding of workflows by implementing a simple option-chaining workflow. We want a workflow that allows us to chain together computations that may return `#some(a) | #none`, short-circuiting if any step returns `#none`.

# Setup

```bash
nanyx new option_chaining
cd option_chaining
```

# The code

A workflow is a record of keyword methods. We start by declaring the shape of that record as a context, so that functions can require it:

```nanyx
context OptionChain = (
  try: (#some(a) | #none, a -> #some(b) | #none) -> #some(b) | #none
  return: a -> #some(a) | #none
  zero: #some(a) | #none
)
```

Then we provide the implementation:

```nanyx
@workflow -- This attribute will give you a helper function for free
def optionChain: OptionChain = (
  -- This is how we get the short-circuiting behavior for options.
  -- Notice how we don't use the continuation for the `#none` case, so
  -- the workflow ignores the rest of the block
  try = {
    | #some(value), cont -> cont(value)
    | #none, _ -> #none
  }

  -- `return` wraps the block's final value
  return = { value -> #some(value) }

  -- `zero` is the value of a block that produces nothing: an `if` with no
  -- `else`, or a block that falls off the end without a `return`
  zero = #none
)
```

The `@workflow` attribute generates a helper function named after the builder. If we didn't want to use the attribute, we could implement the helper ourselves. Here `typeof optionChain` is the type of the value `optionChain` (see [typeof](../tour/types#typeof)), so the lambda is required to run with `optionChain` loaded as a context:

```nanyx
def maybe: ([typeof optionChain] () -> #some(a) | #none) -> #some(a) | #none = { f ->
  use optionChain in f()
}
```

Either way, the workflow is used like this:

```nanyx
maybe {
  def a = try someComputation1()
  def b = try someComputation2()

  return a + b
}
```

If `someComputation1()` returns `#none`, the `try` keyword never calls its continuation, so `someComputation2()` is never run and the whole block evaluates to `#none`. Otherwise both values are bound and `return` produces `#some(a + b)`.

For the general model behind this, see [Workflows](../advanced/workflows).
