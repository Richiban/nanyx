---
title: "Workflows"
description: "Builder-based workflows and custom keywords"
order: 1
---

Workflows are one of Nanyx's most distinctive features. They let libraries provide language-like constructs by rewriting a block into method calls on a builder value. This makes patterns like async/await, error chaining, and DSLs feel native.

# The builder shape

A workflow is just a unary higher order function (i.e. a function that takes a single argument that is, itself, a function) or a record with an `apply` function. That function will have some context attached that provides the body of the inner function with keyword methods such as `yield`, `await`, `try`, or `defer`.

```nanyx
someBuilder {
  -- block content
}
```

The block above desugars to either a function call or an `apply` call, whichever is appropriate:

```nanyx
someBuilder({
  -- block content
})
```

or

```nanyx
someBuilder.apply({
  -- block content
})
```

The simplest possible workflow can be illustrated with a builder that just provides a `yield` method that dumps the given value out to debug:

```nanyx
def debug = (
  yield = { value, continuation ->
    hostDebug(value)
    continuation()
  }
)

use debug in
  yield "Debug message 1"
  yield "Debug message 2"
```

The block content is rewritten to calls to the `yield` method on the `debug` builder, which in this case just prints the value and continues.

```nanyx
debug.yield("Debug message 1", {
  debug.yield("Debug message 2", {
    -- end of block
  })
})
```

# Yield and collection builders

The standard `seq` builder provides `yield`, letting you build sequences with a clean syntax:

```nanyx
def numbers = seq {
  yield 1
  yield 2
  yield 3
}
```

This desugars to:

```nanyx
seq.apply({ builder ->
  builder.yield(1)
  builder.yield(2)
  builder.yield(3)
})
```

Note that `yield` is special in that it can be used implicitly when the expression returns a value:

```nanyx
seq {
  1
  2
  3
}
```

# Async and custom keywords

Workflows can introduce custom keywords. For example, an async workflow can expose `await` by implementing a method tagged as a keyword.

```nanyx
def greetingAsync = async {
  def name = await getName()
  return "Hello, {name}"
}
```

That expands into calls on the async builder:

```nanyx
async.apply({ builder ->
  builder.await(getName(), { name ->
    builder.return("Hello, {name}")
  })
})
```

The language has built-in support for a number of common keywords, but you can define your own for any workflow. Custom keywords must end with the character `!` to avoid conflicts with normal method calls:

```nanyx
def retry = (
  $keywordKind(#bind)
  def retry! = { m, f ->
    ...
  }
)
```

## Common keywords

| Keyword | Description |
|---------|-------------|
| `yield`  | Produce a value in a collection builder |
| `await`  | Await an asynchronous operation in an async workflow |
| `try`    | Short-circuit on errors in a workflow that handles results |
| | _More coming soon..._ |

## Keyword kinds

| Kind | Description |
|---------|-------------|
| `#bind`  | The keyword takes a value and a continuation function. The workflow will call the continuation with the result of the keyword operation. |
| `#bindF` | Like bind, but a function returning the target value is passed instead of the value itself. This allows for workflows such as lazy evaluation and querying |
| | _More coming soon..._ |

# Workflow keywords

Builders can expose custom keywords by implementing special methods. Common examples include `yield`, `await`, `try`, and `defer`.

```nanyx
def async = (
  @customKeywordType(#bind)
  def await = { m, f ->
      ...
  }

  @customKeywordType(#bindF)
  def defer = { thunk, continuation ->
    continuation()
    thunk()
  }
)
```

```nanyx
def main = {
  async {
    defer println("This runs at the end of the block")
    println("This runs first")
  }
}
```

# Error and option chaining

A workflow can provide a `try` keyword to short-circuit on errors. This is useful for `Result` or `Option` chaining.

```nanyx
def getCustomer(id: int): Result(Customer, #customerNotFound) = ...
def getLastOrder(c: Customer): Result(Order, #orderNotFound) = ...

spec Result(Money, #customerNotFound | #orderNotFound)
def lastOrderTotal = proc {
  def customer = try getCustomer(5)
  def order = try getLastOrder(customer)
  return order.total
}
```

# Query and DSL workflows

Workflows are a natural fit for DSLs. A query builder can turn a block into a pipeline of filtering, joining, and projection steps.

```nanyx
def result = query {
  def customer = from customers
  def order = from orders
  join order.customerId == customer.customerId
  where order.total > 100
  select customer.name, order.total
}
```

Workflows are also how HTML-style builders or configuration DSLs are implemented.

# When to use workflows

- You want syntax that reads like a language feature, but is implemented in libraries.
- You need structured control flow around effects (async, retries, error handling).
- You want to provide a DSL for a narrow domain.
