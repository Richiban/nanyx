---
title: "Imports"
description: "Organizing code with modules"
order: 13
---

## Importing packages

Importing packages installed from a package manager:

```nanyx
-- Notice how the package name and module name are separated by a slash
import (
  web/http
  web/json.deserialization
)
```

## Importing File Paths

For quick scripts or local utilities, you can import directly from a file path. When importing a file path, use an alias to qualify the module name.

```nanyx
-- If importing a file the module name must be qualified
import "./utils" as utils

def main = {
  utils.someHelper(...)
}
```

## Qualified Imports

Access module members with the module name:

```nanyx
import nanyx/math as m

def result = m.sqrt(16)  -- 4.0
def pi = m.pi
```

## Selective Imports

Import specific items from a module by deconstructuring the module into the desired members:

```nanyx
import nanyx/math as (sqrt, pi, cos)

def result = sqrt(16)  -- No need for Math. prefix
```

## Import Aliases

Give modules shorter names:

```nanyx
import dataProcessing as dp

def result = dp.process(data)
```

## Exports

By default, all definitions are accessible only in their own scope. Use `export` to make a type or definition available to other modules.

```nanyx
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

## Nested Modules

Create hierarchies with dot notation:

```nanyx
module users.validation

def validateEmail: string -> Result(Email, ValidationError) = { ... }
def validateAge: int -> Result(Age, ValidationError) = { ... }
```

## Re-exporting

When writing a package for publication, individual modules can be exported too. It is common to re-export items from other modules:

```nanyx
-- Export the collections module outside the package
export module collections

-- Re-export from sub-modules
export collections.list

-- Re-export specific items
export collections.map (empty, insert, lookup)
```

## Module-Level Constants

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

## Circular Dependencies

Avoid circular module dependencies. If module A imports B, then B cannot import A.

Instead, extract shared code to a third module:

```nanyx
-- Bad: circular dependency
module a
import b  -- A imports B

module b  
import a  -- B imports A (circular!)

-- Good: extract shared code
module shared
-- Common definitions

module a
import shared

module b
import shared
```

## Standard Library Modules

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

## Example: User Management Module

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
