---
title: "Build a CLI Tool"
description: "Create a command-line application"
order: 1
---

# Build a CLI Tool

Let's build a simple CLI tool that counts words in a file.

## Project Setup

```bash
nanyx new word_counter
cd word_counter
```

## The Code

```nanyx
import (
  nanyx/file
  nanyx/string
  nanyx/list
)

match file.read("input.txt")
  | #ok(content) ->
    def words = content
      \string.split(" ")
      \list.filter { != "" }
    
    def count = list.length(words)
    println("Word count: {count}")
  | #error(err) ->
    println("Error reading file: {err}")

```

## Run It

```bash
echo "hello world from nanyx" > input.txt
nanyx run
# Word count: 4
```

## Key Concepts

- **File I/O** — the `file` module returns result types for safe error handling
- **Pipelines** — chain transformations with `\` for readable data processing
- **Pattern matching** — handle success and failure cases explicitly
