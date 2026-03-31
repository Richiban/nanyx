---
title: "Files and projects"
description: "How Nanyx runs single files and multi-file projects"
order: 4
---

You can run a single Nanyx file directly, or you can organize code as a multi-file project. The compiler supports both workflows.

## Single-file scripts

A single file can be executed on its own. It does not need module or members declarations; you can write imports and top-level statements directly.

```nanyx
import (
  nanyx/file
  nanyx/string
)

match file.read("input.txt")
  | #ok(content) ->
    def words = content
      \string.split(" ")
      \list.filter { != "" }
    println("Word count: {list.length(words)}")
  | #error(err) ->
    println("Error: {err}")
```

You can run this file with `nanyx run filename.nyx`. The compiler treats the file as an implicit module and executes top-level statements. This is great for quick scripts, experiments, and learning.

## Importing by file path

Files can import each other by file path. 

```nanyx
-- utils.nyx
def someHelper = { ... }
```

```nanyx
-- main.nyx
import "./utils" as utils

def main = {
  utils.someHelper(...)
}

main()
```

This works well for small scripts, but there is one important rule: a file with top-level statements is an entrypoint, and entrypoints cannot be imported. If you try, the compiler will error.

That means shared files should avoid top-level statements and only define values, functions, types, and modules.

## Project files and module imports

For larger projects, introduce a project file. In that mode, modules are imported by their declared names instead of by file paths. This keeps imports stable as the project grows and clarifies ownership of modules.

As a rule of thumb:

- Use single files for quick scripts or experiments.
- Use file-path imports for small, ad-hoc multi-file code.
- Use a project file and module imports for longer-lived codebases.
