---
title: "Workflows"
description: "Builder-based workflows and custom keywords"
order: 1
---

Workflows are one of Nanyx's most distinctive features. They let libraries provide language-like constructs by rewriting a block into method calls on a builder value. This makes patterns like async/await, error chaining, and DSLs feel native.

## The builder shape

A workflow is just a value with an `apply` function and (optionally) keyword methods such as `yield`, `await`, `try`, or `use`.

```nanyx
someBuilder {
  -- block content
}
```

The block above desugars to an `apply` call:

```nanyx
someBuilder.apply({ builder ->
  -- block content
})
```

## Yield and collection builders

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

Builders are also the preferred way to initialize collections such as maps:

```nanyx
def someMap = map {
  1 => "one"
  2 => "two"
}
```

## Async and custom keywords

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

## Workflow keywords

Builders can expose custom keywords by implementing special methods. Common examples include `yield`, `await`, `try`, and `use`.

```nanyx
def async = (
  @customKeywordType(#bind)
  def await(m, f) ->
    ...
)
```

## Error and option chaining

A workflow can provide a `try` keyword to short-circuit on errors. This is useful for `Result` or `Option` chaining.

```nanyx
def getCustomer(id: int): Result(Customer, #customerNotFound) = ...
def getLastOrder(c: Customer): Result(Order, #orderNotFound) = ...

spec Result(Money, #customerNotFound | #orderNotFound)
def lastOrderTotal = handle {
  def customer = try getCustomer(5)
  def order = try getLastOrder(customer)
  return order.total
}
```

## Query and DSL workflows

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

## When to use workflows

- You want syntax that reads like a language feature, but is implemented in libraries.
- You need structured control flow around effects (async, retries, error handling).
- You want to provide a DSL for a narrow domain.
