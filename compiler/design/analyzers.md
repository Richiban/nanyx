# Analyzers: compiler design

> **Status: draft.** This is the compiler-side design for analyzers. The user-facing model is described in
> [Analyzers](../../docs/src/content/advanced/analyzers.md) and
> [Official style analyzer](../../docs/src/content/standard-tools/official-analyzer.md); this document is about
> what the compiler has to provide so that model can be built without reworking the front end later.

## Summary

Analyzers inspect a checked module and report diagnostics that are not compile errors: unused bindings,
unreachable branches, style rules. The docs promise that they are written in Nanyx, configured per project, and
show up in the editor next to compiler errors.

Most of what analyzers need is not an analyzer API. It is the substrate under it: exact source spans, a
structured diagnostic, and a semantic model that relates every name to its declaration. Those are cheap to add
while the compiler is small and expensive to add later, because every phase and every test touches them. So the
plan is to build the substrate first, prove it with a handful of analyzers written in F# inside the compiler, and
only then design the public contract that Nanyx-authored analyzers will be written against.

## Goals

- Analyzer diagnostics are positioned exactly (start and end), in the right file, and can carry fixes.
- Every diagnostic, compiler or analyzer, has a stable code, so projects can configure, promote or suppress it.
- Analyzers see the program as the user wrote it, with types and resolved names.
- An analyzer can never break compilation: a crashing analyzer produces a diagnostic, not a failed build.
- The compiler itself emits only errors (see [No warnings](../../docs/src/content/foundations/principles.md)).

## Non-goals (for now)

- The public, versioned API for analyzers written in Nanyx. It is the last step, not the first.
- Package-manifest configuration. The `.nyxpack` manifest does not exist yet; configuration is passed in as a
  compiler option until it does.
- Running analyzers on code that does not type-check (see [Code with errors](#code-with-errors)).

## Where the compiler is today

- **Positions are partial and mostly recovered after the fact.** `SourceRange` is `int * int` (a start position
  only), and only `IdentifierExpr`, `FunctionCall`, `Pipe` and `MemberAccess` carry one. Most diagnostics are
  positioned by `annotateDiagnosticsWithSource` (`Library.fs`), which searches the source for the first
  identifier quoted in the message, or by `tryParseLineColumn`, which parses `line:col` out of message text. A
  diagnostic about the second `x` in a file lands on the first.
- **Diagnostics from imported modules are positioned in the wrong file.** They are annotated against the root
  module's source, because a diagnostic carries no file.
- **Diagnostics have no identity.** `Diagnostic` is severity, message and an optional position. There is no code
  to configure or suppress, no end position, no related locations, and nowhere to put a fix.
- **The compiler has a warning severity it never uses.** `WarningSeverity` exists in `Diagnostics.fs` and the
  language server maps it, but nothing emits it.
- **The type checker sees desugared code.** `desugar` turns workflow binds and `return` into calls to the
  keyword function and `pure` before inference, and `applyAttributes` replaces `@entry` with a synthesized call.
  The synthesized nodes have no position and no link back to the syntax they came from.
- **Name resolution is recorded** ([#98](https://github.com/nanyx-lang/nanyx/issues/98)). Each binding in
  `TypeEnv` carries the symbol it declares, so inference records which symbol every identifier, call, pipe target
  and qualified name resolved to (`TypedModule.Symbols`, `References`, and `ImportReferences` for the import a
  name came through). Values, parameters, pattern variables and imports are symbols; type names and context
  members are not yet, because type expressions have no nodes. Declarations have no nodes either, so the parser
  keeps their spans beside the tree (`DeclaredNames`) until [#96](https://github.com/nanyx-lang/nanyx/issues/96)
  gives them their own.
- **The typed tree is a placeholder.** `TypedExpr` wraps the untyped `Expression` with a type and some optional
  children, and is turned back into syntax (`moduleFromTypedItems`) for the backend.

## Design

### 1. Spans on every node

Every syntax node carries a `SourceSpan`:

```fsharp
type SourceSpan =
    { File: string       // the path the module was parsed from ("" for in-memory source)
      Start: int         // character offset, inclusive
      End: int           // character offset, exclusive; trailing whitespace is not included
      StartLine: int     // zero-based
      StartColumn: int } // zero-based
```

Offsets make slicing and text edits trivial, and they are what FParsec's `Position.Index` gives. The start's line
and column are kept as well, so code without the source text (inference, diagnostics) can report a position; the
end's line and column come from the source when needed. Offsets are into the source with line endings normalised
to `\n`.

The AST moves from "some cases have an optional range" to "every node has a span". The least invasive shape is a
wrapper record around the existing unions:

```fsharp
type Expression = { Kind: ExpressionKind; Span: SourceSpan; Id: NodeId }
and ExpressionKind =
    | LiteralExpr of Literal
    | IdentifierExpr of Identifier
    ...
```

and the same for `Pattern`, `Statement`, `TypeExpr` and top-level items. This is one mechanical refactor of the
parser, inference and the WASM backend; it can be done one node family at a time (expressions first, since that
is where most diagnostics land). The `SourceRange option` fields on individual cases go away.

`NodeId` is an integer, unique within the process, assigned when a node is created. It is what side tables (types, resolved symbols)
are keyed by, so analysis results do not need a parallel copy of the tree.

Once every node has a span, `annotateDiagnosticsWithSource` and `tryParseLineColumn` are deleted: a diagnostic
is created with the span of the node it is about.

### 2. Structured diagnostics

The diagnostic follows the error anatomy in [Principles](../../docs/src/content/foundations/principles.md#human-readable-errors):
a code, a short title, a detailed message, suggestions and fixes.

```fsharp
type Severity =
    | Critical   // compilation cannot proceed
    | Error
    | Warning
    | Info
    | Hint

type TextEdit = { Span: SourceSpan; NewText: string }

type Fix = { Title: string; Edits: TextEdit list }

type Diagnostic =
    { Code: string                              // "NYX0001", or "<analyzer>/<rule>" for analyzer diagnostics
      Severity: Severity
      Title: string                             // short and generic: "Type mismatch"
      Message: string                           // detailed, with the actual names
      Span: SourceSpan option                   // None only for whole-compilation problems
      Related: (SourceSpan * string) list       // "first defined here", "expected because of this annotation"
      Suggestions: string list
      Fixes: Fix list }
```

Rules:

- The compiler emits only `Critical` and `Error`. `Warning`, `Info` and `Hint` exist for analyzers.
- Compiler codes are `NYX` plus four digits, allocated in a single table in `Diagnostics.fs` so they are stable
  and documentable. Analyzer codes are `<analyzer>/<rule>` (`default/unusedBindings`), matching the manifest
  keys in the docs.
- The language server maps `Span` to the LSP range, `Code` to `code`, `Related` to `relatedInformation` and
  `Fixes` to code actions.

Introducing codes for existing errors can be incremental: give every current error a code when its constructor
is changed to take a span.

### 3. The semantic model

Analyzers, hover and go-to-definition all ask the same questions: what is the type of this node, and what does
this name refer to. The semantic model answers them for one module:

```fsharp
type SymbolKind =
    | TopLevelValue | LocalValue | Parameter | PatternVariable
    | ImportedModule | TypeName | ContextMember

type Symbol =
    { Id: SymbolId
      Name: string
      Kind: SymbolKind
      Declaration: SourceSpan
      IsExported: bool }

type SemanticModel =
    { Syntax: Module                          // the module as written: before desugaring, attributes included
      Types: Map<NodeId, Ty>                  // resolved after unification
      Symbols: Map<SymbolId, Symbol>
      References: Map<NodeId, SymbolId>       // every identifier, call, pipe target, member access and qualified name
      Lowered: Map<NodeId, NodeId list> }     // surface node -> desugared nodes it produced
```

From this, "unused" is "a symbol with no references", and the language server's go-to-definition is
`References` followed by `Symbol.Declaration`.

How it is produced:

- **Name resolution is recorded, not just performed.** Inference already looks every name up; it records the
  `SymbolId` it found against the node. Imports become `ImportedModule` symbols, and a qualified access such as
  `m.sqrt` records a reference to both the import and the imported definition.
- **Desugaring keeps provenance.** Every node `desugar` and `applyAttributes` synthesize gets the span (and
  originating `NodeId`) of the syntax it replaced, and `Lowered` records the mapping. Analyzers walk `Syntax`; the
  type of a workflow bind is found through `Lowered`. This is much cheaper than teaching inference to check
  surface syntax directly, and it can be replaced by that later without changing what analyzers see.
- **Import inlining stays where it is.** `CheckedModule` already separates the module's own items from the ones
  inlined for the backend; analyzers read the semantic model of the module's own items, so inlining does not hide
  anything once references to imports are recorded.

Whether `TypedExpr` survives alongside the side tables, or is replaced by them, is an implementation choice for
the first issue that touches it. Side tables keyed by `NodeId` avoid keeping two trees in step.

### 4. The analyzer host (internal)

Before any public contract, analyzers are F# values compiled into the compiler:

```fsharp
type Rule =
    { Id: string                    // "unusedBindings"
      Title: string
      DefaultSeverity: Severity
      Description: string }

type Report =
    { Rule: string
      Span: SourceSpan
      Message: string
      Related: (SourceSpan * string) list
      Fixes: Fix list }

type Analyzer =
    { Name: string                  // "default"
      Rules: Rule list
      Analyze: SemanticModel -> Report list }
```

The host:

- runs after a module type-checks, once per user module (not standard-library modules);
- looks up each report's severity in the configuration (rule default, overridden per project, `none` drops it),
  and stamps the code `<analyzer>/<rule>`;
- catches an analyzer exception and turns it into one diagnostic naming the analyzer, so a broken analyzer never
  breaks the build;
- returns analyzer diagnostics in `CompileResult.Diagnostics` alongside compiler ones, so `nanyx check`,
  `nanyx run` and the language server need no special handling.

Configuration is a compiler option (`Map<string, Severity option>` keyed by code) until the package manifest
exists; then the manifest is parsed into the same value. A rule promoted to `error` fails `nanyx run`/build like a
compiler error does.

The first analyzer is the official one from the docs, built rule by rule. The first rules are chosen to exercise
different parts of the model:

| Rule | Needs |
|------|-------|
| Unused local, argument and pattern variable | symbols and references |
| Unused import | import symbols, qualified references |
| Unreachable match branch | types and the pattern-space code behind the exhaustiveness check |
| Exported definition without an annotation | the surface tree (annotations as written) |
| Unused return value | types of expression statements |

### 5. The public contract (later)

Once the internal analyzers have shaken out the semantic model, the public contract can be designed with
evidence rather than guesses. The direction so far:

- Analyzers are Nanyx packages, compiled to WebAssembly and run at compile time on the embedded Wasmtime engine
  the compiler already ships.
- They see the semantic model through query functions (type of a node, symbol of a reference, references to a
  symbol, children of a node) rather than a serialized copy of the whole tree. Queries keep the compiler's internal
  representation free to change, and avoid copying large trees across the WASM boundary.
- The contract is versioned from day one, and an analyzer declares the version it targets.

## Code with errors

Today any error stops compilation before a typed module exists, so analyzers can only run on modules with no
errors. That is the right first step and keeps analyzers simple. Its cost is in the editor: analyzer diagnostics
disappear while the file has a type error, and reappear when it is fixed.

Running analyzers on code with errors needs parser error recovery and an inference that keeps going after an
error, which the language server wants for its own reasons. It is out of scope here, but the semantic model
should not assume every node has a type: `Types` is a map, and a missing entry means "unknown".

## Plan

1. **Foundations.** Spans on every node; structured diagnostics with codes; diagnostics carry their file.
2. **Semantic model.** Record name resolution as symbols and references; keep desugaring provenance.
3. **Host and first rules.** The internal analyzer host with configuration through compiler options, then the
   rules in the table above, one at a time.
4. **Public analyzers.** Design the contract for Nanyx-authored analyzers, based on what steps 1 to 3 taught us.

Steps 1 and 2 are useful on their own: the language server needs exact ranges and symbols for hover and
go-to-definition regardless of analyzers.

## Open questions

- **Suppression in source.** An attribute (`@allow(default/unusedBindings)`) on a definition, a comment form, or
  only the convention that a `_`-prefixed name is intentionally unused? The docs already use `_` patterns.
- **Compiler error codes.** One flat `NYX0001` sequence, or ranges per phase (parse, resolve, type)?
- **Where analyzers run in the CLI.** Always, or only for `check` and build, with `run` skipping them for speed?
- **Analyzers across modules.** "Exported but never used anywhere in the project" needs every module's semantic
  model at once; per-module analysis is enough for the first rules.
- **Fix application.** Fixes are text edits; does the CLI get a `--fix`, or are fixes editor-only at first?
