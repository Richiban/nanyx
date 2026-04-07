---
title: "Modules and imports"
description: "Organizing code with modules and import patterns"
order: 12
---

Modules in Nanyx help organize code into logical units. They provide namespaces for definitions and control visibility.

## Module declaration

Every Nanyx file optionally starts with a module declaration. 

```nanyx
module main

println("Hello from MyApp!")
```

If the module declaration is omitted, the module remains anonymous and can only be imported by file path.

## Module naming

Module names should be camel-cased and reflect the purpose of the code contained within:

```nanyx
module userManagement

...
```

It is a commonly-used convention to use dots to create namespaced hierarchies of modules within a package:

```nanyx
module collections.list

...
```

## Importing modules

Import other modules to use their exports:

```nanyx
module main

-- Notice how we can import multiple modules in a single statement
import (
  userManagement
  dataProcessing as d
)

userManagement.createUser(...)
d.process(...)
```

The standard library is available under the `nanyx` package and is available in all Nanyx programs; there's no need to install it:

```nanyx
module main

import nanyx/collections

...
```

## Importing file paths

For quick scripts or local utilities, you can import directly from a file path. When importing a file path, you must use an alias to give the module a name in your current scope.

```nanyx
-- If importing a file the module name must be qualified
import "./utils" as utils

utils.someHelper(...)
```

## Importing packages

Importing external packages (such as those installed from a package manager):

```nanyx
-- Notice how the package name and module name are separated by a slash
import (
  webframework/http
  webframework/json.deserialization
)
```

## Qualified imports

Access module members with the module name:

```nanyx
import nanyx/math as m

def result = m.sqrt(16)  -- 4.0
def pi = m.pi
```

## Selective imports

Import specific items from a module using deconstruction syntax on the import:

```nanyx
import nanyx/math as (sqrt, pi, cos)

def result = sqrt(16)  -- No need for Math. prefix
```

It's even possible to both deconstruct and alias an import, although the need to do this is rare:

```nanyx
import nanyx/math as m & (sqrt, pi)

def a = sqrt(16)
def b = m.cos(0)
```

## Exports

Types and definitions are private to a module by default. Use `export` to make them available to other modules:

```nanyx
module utils

export def double: int -> int = { x -> x * 2 }

export def triple: int -> int = { x -> x * 3 }
```

You can also export a module itself:

```nanyx
export module myModule

export def message = "Hello world"
```

## Public vs private definitions

Use `export` to make a type or definition available to other modules.

```nanyx
module utils

-- Public
export def processData: Data -> Result = { data ->
  data \validate \transform
}

-- Private helper
def validate: Data -> Data = { data ->
  -- validation logic
}

def transform: Data -> Data = { data ->
  -- transformation logic
}
```

## Module structure

Organize related functionality:

```nanyx
module collections.list

export def map: (list(a), (a -> b)) -> list(b) = { ... }
export def filter: (list(a), (a -> bool)) -> list(a) = { ... }
export def fold: (list(a), b, (b, a) -> b) -> b = { ... }
```

```nanyx
module collections.map

def empty: map(k, v) = { ... }
def insert: (map(k, v), k, v) -> map(k, v) = { ... }
def lookup: (map(k, v), k) -> Option(v) = { ... }
```

## Nested modules

You can declare module blocks inside a file. The block name is appended to the outer module name:

```nanyx
module myModule

module functions =
  export def f(x) -> x * 2
  export def g(x) -> x ** 2
```

Create hierarchies with dot notation:

```nanyx
module users.validation

def validateEmail: string -> Result(Email, ValidationError) = { ... }
def validateAge: int -> Result(Age, ValidationError) = { ... }
```

## Re-exporting

Export items from other modules:

```nanyx
module Collections

-- Re-export from sub-modules
export Collections.List (map, filter, fold)
export Collections.Map (empty, insert, lookup)
```

## Module-level constants

Define constants at module level:

```nanyx
module config

def appName = "MyApp"
def version = "1.0.0"
def maxRetries = 3

def databaseConfig = (
  host = "localhost"
  port = 5432
  database = "myapp"
)
```

## Circular dependencies

Avoid circular module dependencies. If module A imports B, then B cannot import A.

Instead, extract shared code to a third module:

```nanyx
-- Bad: circular dependency
module A
import B  -- A imports B

module B  
import A  -- B imports A (circular!)

-- Good: extract shared code
module Shared
-- Common definitions

module A
import Shared

module B
import Shared
```

## Standard library modules

Nanyx's standard library appears as a package called `nanyx` and is organized into modules:

```nanyx
import (
  nanyx/list
  nanyx/map
  nanyx/set
  nanyx/string
  nanyx/math
  nanyx/option
  nanyx/result
)
```

## Example: User management module

```nanyx
module userManagement

export type User = (
  id: UserId
  name: string
  email: Email
  role: #admin | #user
)

export type ValidationError =
  | #invalidEmail
  | #nameTooShort
  | #emailTaken

export def createUser
  : [Random] (string, Email) -> Result(User, ValidationError) 
  = { name, email ->
    if name.length < 3 then
      #error(#nameTooShort)
    else
      def id = generateId()
      #ok(id = id, name = name, email = email, role = #user)
  }

def validateEmail: Email -> Result(Email, ValidationError) = { email ->
  if email \contains("@") then
    #ok(email)
  else #error(#invalidEmail)
}

def generateId: [Random] () -> UserId = {
  -- Implementation detail, not exported
  UserId(randomInt())
}
```

## Module Organization Best Practices

1. **One module per file**: Keep modules focused and manageable
2. **Clear naming**: Use descriptive names that reflect the module's purpose
3. **Hide implementation**: Mark helpers as private
4. **Cohesion**: Group related functionality together
5. **Minimal coupling**: Reduce dependencies between modules
6. **No circular deps**: Keep dependency graph acyclic

## Using Modules

```nanyx
module myApp

import (
  userManagement as users
  dataProcessing
)

def main = {
  def result = users.createUser("Alice", "alice@example.com")
  
  match result
    | #ok(user) ->
        user \dataProcessing.process \save
    | #error(err) ->
        err \logError
}
```
