---
title: "Error handling"
description: "Working with Result and Option types"
order: 16
---

Nanyx distinguishes between errors (returned values) and exceptions (thrown, but not caught). The language encourages explicit errors via tag unions so callers must handle failure cases.

Nanyx uses tag unions for error handling — no exceptions!

# Result types

In Nanyx, functions that can fail return a result type using tag unions:

```nanyx
-- A function that might fail
def getCustomer: CustomerId -> #some(Customer) | #error(#notFound | #databaseError)
  = { id -> ... }
```

Handle the result with pattern matching:

```nanyx
match getCustomer(someId)
  | #some(customer) -> println("Found: {customer.name}")
  | #error(#notFound) -> println("Customer not found")
  | #error(#databaseError) -> println("Database error")
```

# The `handle` and `try` keywords

Use `handle` with `try` to short-circuit on errors:

```nanyx
def result = proc {
  def customer = try getCustomer("someid")
  def latestOrder = try getLatestOrder(customer)
  return latestOrder.total
}
-- `result` has type `#some(int) | #error(#notFound | #databaseError | #noOrders)`
```

If any step returns an error, the function immediately returns that error.

# Option types

For values that might not exist, use the `#some` / `#none` pattern:

```nanyx
def findUser: int -> #some(User) | #none = { id ->
  match id
    | 1 -> #some(User("Alice"))
    | _ -> #none
}
```

# The `except` keyword

The `except` keyword is similar to `match` but doesn't require exhaustive handling. Unhandled values are returned as-is:

```nanyx
def g = {
  def f: () -> #some(int) | #error(#notFound) = ...
  
  def result = f() except #error(#notFound) -> return 0
  
  -- result has type `int` (the error case was handled)
  result
}
```

# Chaining with pipelines

```nanyx
input
  \parseInt
  \result.map { * 2 }
  \result.unwrap(default = 0)
```

> **Philosophy:** By making errors explicit in the type system, Nanyx ensures you always handle failure cases. No more unexpected crashes!
