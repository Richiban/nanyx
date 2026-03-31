---
title: "Simple HTTP Server"
description: "Build a basic web server"
order: 3
---

# Simple HTTP Server

Build a basic HTTP server with Nanyx using the `http` package.

## Setup

```bash
nanyx new my_server
cd my_server
nanyx add http
```

## The Code

```nanyx
module main

import http/server
import http/request
import http/response

def main = {
  def handler: request.Request -> response.Response = { req ->
    match request.path(req)
      | "/" -> response.text(200, "Welcome to Nanyx!")
      | "/hello/{name}" -> response.text(200, "Hello, {name}!")
      | _ -> response.text(404, "Not Found")
  }

  println("Server running on http://localhost:3000")
  server.start(handler, port = 3000)
}
```

## Run It

```bash
nanyx run
# Server running on http://localhost:3000
```

## Key Concepts

- **Pattern matching on routes** — clean, readable routing without a framework
- **String interpolation** — extract path parameters with `{name}`
- **Immutable request/response** — functional approach to HTTP handling
