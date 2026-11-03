# Nanyx VS Code Extension

This extension provides editor support for the Nanyx language, including syntax highlighting and compiler-driven diagnostics via a language server.

## Features

- Syntax highlighting for `.nyx` files.
- Typechecking diagnostics via the Nanyx language server (powered by `Nanyx.Compiler.Cli`).
- Command palette action: **Nanyx: Typecheck Current File**.

## Settings

- `nanyx.typecheckOnSave`: Run the compiler on save/open to surface diagnostics (default: `true`).
- `nanyx.compilerProject`: Path (relative to workspace root) to `Nanyx.Compiler.Cli.fsproj`.
- `nanyx.compilerDotnetPath`: Path to the `dotnet` executable (default: `dotnet`).

## How it works

The language server executes `dotnet run --project <Nanyx.Compiler.Cli.fsproj> -- <file>` and parses `[error]` and `[warning]` lines from stdout to populate VS Code diagnostics.

## Troubleshooting

- If you see **"Nanyx compiler project not found"**, update `nanyx.compilerProject` to match your workspace layout.
- If `dotnet` is not on your PATH, set `nanyx.compilerDotnetPath` to the full path.
