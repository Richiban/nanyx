---
title: Simple HTTP Server
description: Build a basic web server
order: 3
---

Build a basic HTTP server with Nanyx using the `http` package.

# Setup

```bash
nanyx new my_server
cd my_server
nanyx install nanyx.http
```

# The code

```nanyx
module main

import nanyx.http/server

def handler: Request -> Response = { req ->
  match req.path
    | "/" -> Response.text(200, "Welcome to Nanyx!")
    | #Path('/hello/{name}')(name) -> Response.text(200, "Hello, {name}!")
    | _ -> Response.text(404, "Not Found")
}

@entry
def main = {
  println("Server running on http://localhost:3000")
  server.start(handler, port = 3000)
}
```

The route template is written as a single-quoted (raw) string so that its `{name}` placeholder is passed to the `#Path` pattern as-is rather than being interpolated. `#Path` is a [custom pattern with arguments](../advanced/custom-patterns#patterns-with-arguments): the template goes in the first parenthesis group, and the captured parameters are bound by the second.

# Run it

```bash
nanyx run
# Server running on http://localhost:3000
```

# Key concepts

- **Pattern matching on routes** — clean, readable routing without a framework
- **Custom patterns** — extract path parameters with a `#Path` template pattern
- **Immutable request/response** — functional approach to HTTP handling
