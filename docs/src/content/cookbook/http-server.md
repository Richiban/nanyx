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
nanyx install nanyx.http
```

## The Code

```nanyx
module main

import nanyx.http/server

def handler: Request -> Response = { req ->
  match req.path
    | "/" -> Response.text(200, "Welcome to Nanyx!")
    | "/hello/{name}" -> Response.text(200, "Hello, {name}!")
    | _ -> Response.text(404, "Not Found")
}

println("Server running on http://localhost:3000")
server.start(handler, port = 3000)
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
