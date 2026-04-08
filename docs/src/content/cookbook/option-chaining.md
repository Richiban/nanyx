---
title: Option chaining
description: Create a command-line application
order: 4
---

Let's build our understanding of workflows by implementing a simple `Option` chaining workflow. We want a workflow that allows us to chain together computations that may return `Option` values, short-circuiting if any step returns `None`.

## Setup

```bash
nanyx new option_chaining
cd option_chaining
```

## The code

```nanyx
@workflow -- This attribute will give you a helper function for free
def optionChain = (
  -- This is how we get the short-circuiting behavior for options.
  -- Notice how we don't use the continuation for the `#none` case, so 
  -- the workflow ignores the rest of the function
  def try = {
    | #some(value), cont -> cont(value)
    | #none, _ -> #none
  }

  def unit = #none
)

-- If we didn't want to use the workflow attribute, we could implement the helper function ourselves:
def maybe: ([typeof optionChain] () -> #some(a) | #none) -> #some(a) | #none = { f ->
  use optionChain in f()
}

maybe {
  def a = try someComputation1()
  def b = try someComputation2()

  return a + b
}
```
