# Language server and editor: workstream

> **Status: outline.** This page frames the language server workstream: what it covers, where it is today and what
> comes next. Sections marked _to do_ are placeholders to fill in as the work is designed. Work is tracked under the
> `lsp` label.

## Summary

The editor experience for Nanyx: the language server built into the `nanyx` binary (`nanyx lsp`) and the VS Code
extension that launches it. The server calls the compiler as a library, so most of what it can offer depends on
what the compiler keeps: exact spans, resolved names and types.

## Scope

| Area | Where |
|------|-------|
| Language server | `compiler/Nanyx.LanguageServer` (C#, OmniSharp LSP) and its tests |
| VS Code extension | `extension/`: finding or downloading `nanyx`, starting the server, commands, settings |
| Syntax highlighting | `extension/syntaxes/` (TextMate grammar) |

## Where it is today

- **Diagnostics only.** The server does full text sync and publishes diagnostics on open and change. A
  diagnostic with a position is shown from there to the end of the identifier; one without is shown at the start
  of the file. See [The language server](../README.md#the-language-server).
- **No hover, go-to-definition or completion.** Each is one handler in `Handlers/`; they wait on the compiler
  keeping a span for every node ([#96](https://github.com/nanyx-lang/nanyx/issues/96)) and recording what each name refers to ([#98](https://github.com/nanyx-lang/nanyx/issues/98)).
- **A parse error hides the rest of the file**, because the parser has no error recovery.
- **The extension** highlights syntax, finds `nanyx` (setting, `PATH`, or a downloaded release) and offers
  *Download the compiler* and *Restart the language server*.

## Goals

- Errors appear as you type, at the right range, in the right file.
- Hover shows the inferred type; go-to-definition and find-references work for every name.
- Completion offers names in scope and the members of a record or imported module.
- Fixes from structured diagnostics ([#97](https://github.com/nanyx-lang/nanyx/issues/97)) appear as code actions.

## Plan

_To do._ Candidate items, not yet filed, in rough dependency order:

1. **Exact ranges.** Map diagnostic spans to LSP ranges once [#96](https://github.com/nanyx-lang/nanyx/issues/96) and [#97](https://github.com/nanyx-lang/nanyx/issues/97) land; report `code` and
   `relatedInformation`.
2. **Hover.** Types from the semantic model ([#98](https://github.com/nanyx-lang/nanyx/issues/98)).
3. **Go-to-definition and find-references.** From the recorded symbols and references ([#98](https://github.com/nanyx-lang/nanyx/issues/98)).
4. **Document symbols.** The outline view, from top-level definitions.
5. **Completion.** Names in scope, then record members and module exports.
6. **Code actions.** From diagnostic fixes.
7. **Diagnostics in imported files.** Publish diagnostics for the file they belong to, not the file being edited.

## Open questions

- Incremental re-checking: is compiling the whole document on every change fast enough as programs grow?
- Should semantic tokens replace or supplement the TextMate grammar?
- Editors beyond VS Code: is `nanyx lsp` plus documented client setup enough?
