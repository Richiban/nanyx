---
title: "Modules and imports"
description: "Organizing code with modules and import patterns"
order: 12
---

Modules in Nanyx help organize code into logical units. They provide namespaces for definitions and are the primary unit of encapsulation, controlling visibility.

The common case is that there is a 1:1 relationship between files and modules, but Nanyx also supports declaring multiple modules in a single file or splitting a module across multiple files. The module system is flexible to accommodate different organizational styles.

# Module declaration

Every Nanyx file optionally starts with a module declaration.

```nanyx
module main

println("Hello from MyApp!")
```

If the module declaration is omitted, the module remains anonymous and can only be imported by file path.

```nanyx
-- No module name means this module can only be imported by file path

println("Hello from MyApp!")
```

It is possible to write a module declaration without giving it a name, but by itself this doesn't provide any benefits over omitting the declaration entirely and is only used in order to attach attributes or documentation to the module:

```nanyx
-- Here we use the module declaration syntax to attach the @entry attribute, even though we don't give the module a name
@entry module

println("Hello from MyApp!")
```

# Module naming

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

# Importing modules

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

## Importing modules by file path

For quick scripts or local utilities, you can import directly from a file path. When importing a file path, you must use an alias to give the module a name in your current scope.

```nanyx
-- If importing a file the module name must be qualified
import "./utils" as utils

utils.someHelper(...)
```

## Importing modules from other packages

When importing external packages (such as those installed from a package manager), the import includes the package name as a prefix:

```nanyx
-- Notice how the package name and module name are separated by a slash
-- We import the `http` and `json.deserialization` modules from the `nanyx.web` package
import (
  nanyx.web/http
  nanyx.web/json.deserialization
)
```

The standard library is available under the `nanyx` package and is available in all Nanyx programs; there's no need to install it:

```nanyx
module main

import nanyx/collections
...
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

# Exports

Types and definitions are private to a module by default. Use `export` to make them available to other modules:

```nanyx
module utils

-- Public
export def processData: Data -> Result = { data ->
  data \validate \transform
}

-- Private helpers
def validate: Data -> Data = { data ->
  ...
}

def transform: Data -> Data = { data ->
  ...
}
```

Modules themselves do not need to be exported; they are available for import by other modules as long as they contain at least one exported definition.

## Re-exporting

Export items from other modules:

```nanyx
module collections

-- Re-export from sub-modules
export collections.list (map, filter, fold)
export collections.map (empty, insert, lookup)
```

# Nested modules

You can declare module blocks inside a file. The block name is appended to the outer module name:

```nanyx
module myModule

-- The full name of this module is `myModule.functions`
module functions =
  export def f(x) -> x * 2
  export def g(x) -> x ** 2

  module helpers =
    def helper1() = ...
    def helper2() = ...
```

Create hierarchies with dot notation:

```nanyx
module users.validation

def validateEmail: string -> Result(Email, ValidationError) = { ... }
def validateAge: int -> Result(Age, ValidationError) = { ... }
```

# Partial modules

A module can be split across multiple files using the same module name in each file. This is useful for large modules or when you want to separate public API from implementation details.

To prevent accidental misuse, each module declaration must be decorated with the `partial` attribute:

```nanyx
-- utils.nanyx
@partial module utils

...
```

```nanyx
-- utils2.nanyx
@partial module utils

...
```

# Circular dependencies

Avoid circular module dependencies. If module A imports B, then B cannot import A.

Instead, extract shared code to a third module:

```nanyx
-- Bad: circular dependency
module a
import b  -- A imports B

module b  
import a  -- B imports A (circular!)
```

```nanyx
-- Good: extract shared code
module shared
-- Common definitions

module a
import shared

module b
import shared
```

# Modules in the standard library

Nanyx's standard library appears as a package called `nanyx` and is organized into modules:

```nanyx
import (
  nanyx/collections
  nanyx/map
  nanyx/set
  nanyx/string
  nanyx/math
  nanyx/option
  nanyx/result
)
```

# Example: User management module

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
      #some(id = id, name = name, email = email, role = #user)
  }

def validateEmail: Email -> Result(Email, ValidationError) = { email ->
  if email \contains("@") then
    #some(email)
  else #error(#invalidEmail)
}

def generateId: [Random] () -> UserId = {
  -- Implementation detail, not exported
  UserId(randomInt())
}
```

# Module Organization Best Practices

1. **One module per file**: Keep modules focused and manageable
2. **Clear naming**: Use descriptive names that reflect the module's purpose
3. **Hide implementation**: Mark helpers as private
4. **Cohesion**: Group related functionality together
5. **Minimal coupling**: Reduce dependencies between modules
6. **No circular deps**: Keep dependency graph acyclic
