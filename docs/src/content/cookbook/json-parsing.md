---
title: "Parse JSON Data"
description: "Working with JSON in Nanyx"
order: 2
---

# Parse JSON Data

Nanyx provides a `json` module for encoding and decoding JSON.

## Decoding JSON

```nanyx
import json

type User = (name: string, age: int)

def decodeUser: string -> #ok(User) | #error(json.DecodeError) = { data ->
  json.decode(data, json.record(
    name = json.field("name", json.string)
    age = json.field("age", json.int)
  ))
}

def main = {
  def data = "{\"name\": \"Alice\", \"age\": 30}"
  
  match decodeUser(data)
    | #ok(user) -> println("Hello, {user.name}!")
    | #error(_) -> println("Invalid JSON")
}
```

## Encoding JSON

```nanyx
def jsonString = json.toString(
  json.object([
    ("name", json.string("Alice"))
    ("age", json.int(30))
    ("active", json.bool(true))
  ])
)
-- {"name":"Alice","age":30,"active":true}
```

## Working with Arrays

```nanyx
def usersJson = json.toString(
  json.array(users \map { user ->
    json.object([
      ("name", json.string(user.name))
      ("age", json.int(user.age))
    ])
  })
)
```
