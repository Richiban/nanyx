---
title: "Hello, World!"
description: "Write your first Nanyx program"
order: 3
---

# Hello, World!

Let's create your first Nanyx project and run it.

## Create a Project

```bash
nanyx new hello_world
cd hello_world
```

This creates a new project with the following structure:

```
hello_world/
├── nanyx.toml
├── src/
│   └── hello_world.nyx
└── test/
    └── hello_world_test.nyx
```

## Your First Program

Open `src/hello_world.nyx` and you'll see:

```nanyx
module main

def main = {
  println("Hello, World!")
}
```

## Run It

```bash
nanyx run
# Hello, World!
```

## Understanding the Code

- `module main` — declares the module name
- `def main` — the entry point function
- `println(...)` — prints a line to standard output

## What's Next?

You're ready to explore the language! Head to the [Language Tour](/docs/tour/variables) to learn Nanyx step by step.
