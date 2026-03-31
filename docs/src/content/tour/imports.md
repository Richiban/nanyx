---
title: Imports
description: "Organizing code with modules"
order: 5
---

## Importing packages

Importing packages installed from a package manager:

```nyx
-- Notice how the package name and module name are separated by a slash
import (
  web/http
  web/json.deserialization
)
```

## Importing File Paths

For quick scripts or local utilities, you can import directly from a file path. When importing a file path, use an alias to qualify the module name.

```nyx
-- If importing a file the module name must be qualified
import "./utils" as utils

def main = {
  utils.someHelper(...)
}
```

## Qualified Imports

Access module members with the module name:

```nyx
import nanyx/math as m

def result = m.sqrt(16)  -- 4.0
def pi = m.pi
```

## Selective Imports

Import specific items from a module by deconstructuring the module into the desired members:

```nyx
import nanyx/math as (sqrt, pi, cos)

def result = sqrt(16)  -- No need for Math. prefix
```

## Import Aliases

Give modules shorter names:

```nyx
import DataProcessing as DP

def result = DP.process(data)
```

## Exports

By default, all top-level definitions are exported:

```nyx
module utils

-- Exported (public)
def double: int -> int = { x -> x * 2 }

-- Also exported
def triple: int -> int = { x -> x * 3 }
```

## Public vs private Definitions

Use `export` to make a type or definition available to other modules.

```nyx
module Utils

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

## Module Structure

Organize related functionality:

```nyx
module collections.list

export def map: (list(a), (a -> b)) -> list(b) = { ... }
export def filter: (list(a), (a -> bool)) -> list(a) = { ... }
export def fold: (list(a), b, (b, a) -> b) -> b = { ... }
```

```nyx
module Collections.Map

def empty: map(k, v) = { ... }
def insert: (map(k, v), k, v) -> map(k, v) = { ... }
def lookup: (map(k, v), k) -> Option(v) = { ... }
```

## Nested Modules

Create hierarchies with dot notation:

```nyx
module users.validation

def validateEmail: string -> Result(Email, ValidationError) = { ... }
def validateAge: int -> Result(Age, ValidationError) = { ... }
```

## Re-exporting

Export items from other modules:

```nyx
module Collections

-- Re-export from sub-modules
export Collections.List (map, filter, fold)
export Collections.Map (empty, insert, lookup)
```

## Module-Level Constants

Define constants at module level:

```nyx
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

## Circular Dependencies

Avoid circular module dependencies. If module A imports B, then B cannot import A.

Instead, extract shared code to a third module:

```nyx
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

## Standard Library Modules

Nanyx's standard library appears as a package called `nanyx` and is organized into modules:

```nyx
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

## Example: User Management Module

```nyx
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

```nyx
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
