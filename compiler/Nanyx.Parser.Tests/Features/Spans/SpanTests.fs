module ParserTests.Features.Spans

open System.IO
open Xunit
open FsUnit.Xunit
open Parser.Program

let private parse (source: string) =
    match parseModuleFile "t.nyx" source with
    | Result.Ok module' -> module'
    | Result.Error err -> failwith $"Parse failed: {err}"

/// The value of the top-level `def name = ...` in `module'`.
let private valueOf name (module': Module) =
    module'
    |> List.tryPick (function
        | Def (ValueDef (_, n, _, value)) when n = name -> Some value
        | _ -> None)
    |> Option.defaultWith (fun () -> failwith $"No def {name} in %A{module'}")

/// The source text a span covers.
let private textOf (source: string) (span: SourceSpan) = source.Substring(span.Start, span.End - span.Start)

let private someSpan (span: SourceSpan option) =
    match span with
    | Some span -> span
    | None -> failwith "Expected a span"

[<Fact>]
let ``an identifier spans its name`` () =
    let source = "def x = someValue"
    let value = parse source |> valueOf "x"
    textOf source value.Span |> should equal "someValue"
    value.Span.File |> should equal "t.nyx"
    value.Span.Start |> should equal 8
    value.Span.StartLine |> should equal 0
    value.Span.StartColumn |> should equal 8

[<Fact>]
let ``a call spans from its name to the closing paren, and its arguments span the tuple inside`` () =
    let source = "def x = foo(1, bar)"
    let value = parse source |> valueOf "x"
    textOf source value.Span |> should equal "foo(1, bar)"
    match value.Kind with
    | FunctionCall ("foo", nameSpan, [ args ]) ->
        textOf source (someSpan nameSpan) |> should equal "foo"
        textOf source args.Span |> should equal "1, bar"
        match args.Kind with
        | TupleExpr [ one; bar ] ->
            textOf source one.Span |> should equal "1"
            textOf source bar.Span |> should equal "bar"
            bar.Span.StartColumn |> should equal 15
        | other -> failwith $"Expected a tuple of two arguments, got %A{other}"
    | other -> failwith $"Expected a call to foo, got %A{other}"

[<Fact>]
let ``trailing whitespace is not part of a span`` () =
    let source = "def x = foo(1)   \ndef y = a + b   \ndef z = 2"
    let module' = parse source
    textOf source (valueOf "x" module').Span |> should equal "foo(1)"
    textOf source (valueOf "y" module').Span |> should equal "a + b"
    // At the end of the file too (the parser trims the source, so the span stays within it).
    let atEnd = "def x = foo(1)   "
    textOf atEnd (parse atEnd |> valueOf "x").Span |> should equal "foo(1)"

[<Fact>]
let ``a binary operator chain spans from its first operand to its last`` () =
    let source = "def x = a + b * c - d"
    let value = parse source |> valueOf "x"
    textOf source value.Span |> should equal "a + b * c - d"
    match value.Kind with
    | BinaryOp ("-", left, right) ->
        textOf source left.Span |> should equal "a + b * c"
        textOf source right.Span |> should equal "d"
        match left.Kind with
        | BinaryOp ("+", _, product) -> textOf source product.Span |> should equal "b * c"
        | other -> failwith $"Expected an addition, got %A{other}"
    | other -> failwith $"Expected a subtraction, got %A{other}"

[<Fact>]
let ``not spans from the keyword to its operand`` () =
    let source = "def x = not d\ndef y = not   a == b"
    let module' = parse source
    let x = valueOf "x" module'
    textOf source x.Span |> should equal "not d"
    x.Span.StartColumn |> should equal 8
    let y = valueOf "y" module'
    match y.Kind with
    | BinaryOp ("==", notA, _) ->
        textOf source notA.Span |> should equal "not   a"
        notA.Span.StartLine |> should equal 1
        notA.Span.StartColumn |> should equal 8
    | other -> failwith $"Expected an equality, got %A{other}"

[<Fact>]
let ``member access spans the whole chain, and its field span is the field name`` () =
    let source = "def x = a.b.c"
    let value = parse source |> valueOf "x"
    textOf source value.Span |> should equal "a.b.c"
    match value.Kind with
    | MemberAccess (inner, "c", fieldSpan) ->
        textOf source (someSpan fieldSpan) |> should equal "c"
        textOf source inner.Span |> should equal "a.b"
        match inner.Kind with
        | MemberAccess (a, "b", innerFieldSpan) ->
            textOf source (someSpan innerFieldSpan) |> should equal "b"
            textOf source a.Span |> should equal "a"
        | other -> failwith $"Expected a.b, got %A{other}"
    | other -> failwith $"Expected a member access, got %A{other}"

[<Fact>]
let ``a pipe chain spans from its input to its last target, and a lambda spans its braces`` () =
    let source = "def x = xs \\map { x -> x + 1 } \\sum"
    let value = parse source |> valueOf "x"
    textOf source value.Span |> should equal "xs \\map { x -> x + 1 } \\sum"
    match value.Kind with
    | Pipe (inner, "sum", sumSpan, []) ->
        textOf source (someSpan sumSpan) |> should equal "sum"
        textOf source inner.Span |> should equal "xs \\map { x -> x + 1 }"
        match inner.Kind with
        | Pipe (xs, "map", mapSpan, [ lambda ]) ->
            textOf source (someSpan mapSpan) |> should equal "map"
            textOf source xs.Span |> should equal "xs"
            textOf source lambda.Span |> should equal "{ x -> x + 1 }"
            match lambda.Kind with
            | Lambda ([ ("x", None) ], body) -> textOf source body.Span |> should equal "x + 1"
            | other -> failwith $"Expected a lambda, got %A{other}"
        | other -> failwith $"Expected the map pipe, got %A{other}"
    | other -> failwith $"Expected the sum pipe, got %A{other}"

[<Fact>]
let ``an if spans from the if keyword to its last branch`` () =
    let source = "def x = if a then 1 else 2"
    let value = parse source |> valueOf "x"
    textOf source value.Span |> should equal "if a then 1 else 2"
    value.Span.StartColumn |> should equal 8
    match value.Kind with
    | IfExpr (cond, thenExpr, elseExpr) ->
        textOf source cond.Span |> should equal "a"
        textOf source thenExpr.Span |> should equal "1"
        textOf source elseExpr.Span |> should equal "2"
    | other -> failwith $"Expected an if, got %A{other}"

[<Fact>]
let ``spans in a multi-line lambda have the lines and columns they were written at`` () =
    let source =
        "def f = { x ->\n" +
        "  def y = x + 1\n" +
        "  y * 2\n" +
        "}"
    let value = parse source |> valueOf "f"
    textOf source value.Span |> should equal source.[8..]
    value.Span.StartLine |> should equal 0
    value.Span.StartColumn |> should equal 8
    match value.Kind with
    | Lambda ([ ("x", None) ], Kind (Block [ DefStatement (_, "y", None, yValue); ExprStatement result ])) ->
        textOf source yValue.Span |> should equal "x + 1"
        yValue.Span.StartLine |> should equal 1
        yValue.Span.StartColumn |> should equal 10
        textOf source result.Span |> should equal "y * 2"
        result.Span.StartLine |> should equal 2
        result.Span.StartColumn |> should equal 2
    | other -> failwith $"Expected a lambda with a two-statement block, got %A{other}"

/// Every expression in a module, including those inside patterns and types.
let rec private expressionsOfKind (kind: ExpressionKind) : Expression list =
    match kind with
    | LiteralExpr _
    | UnitExpr
    | IdentifierExpr _
    | ContextMemberAccess _ -> []
    | FunctionCall (_, _, args) -> args |> List.collect expressions
    | Lambda (parameters, body) ->
        (parameters |> List.collect (snd >> Option.toList >> List.collect expressionsOfType)) @ expressions body
    | BinaryOp (_, left, right) -> expressions left @ expressions right
    | Pipe (value, _, _, args) -> expressions value @ (args |> List.collect expressions)
    | Block statements -> statements |> List.collect expressionsOfStatement
    | Match (values, arms) ->
        (values |> List.collect expressions)
        @ (arms |> List.collect (fun (patterns, body) -> (patterns |> List.collect expressionsOfPattern) @ expressions body))
    | TupleExpr items
    | ListExpr items -> items |> List.collect expressions
    | RecordExpr fields ->
        fields |> List.collect (function
            | NamedField (_, value)
            | PositionalField value -> expressions value)
    | TagExpr (_, payload) -> payload |> Option.toList |> List.collect expressions
    | IfExpr (cond, ifTrue, ifFalse) -> expressions cond @ expressions ifTrue @ expressions ifFalse
    | MemberAccess (value, _, _) -> expressions value
    | ContextMemberCall (_, _, args) -> args |> List.collect expressions
    | UseIn (binding, body) -> expressionsOfUse binding @ expressions body
    | WorkflowBindExpr (_, value)
    | WorkflowReturnExpr value -> expressions value
    | InterpolatedString parts ->
        parts |> List.collect (function
            | StringText _ -> []
            | StringExpr value -> expressions value)

and private expressions (expr: Expression) : Expression list = expr :: expressionsOfKind expr.Kind

and private expressionsOfStatement (statement: Statement) =
    match statement with
    | DefStatement (_, _, typeOpt, value) ->
        (typeOpt |> Option.toList |> List.collect expressionsOfType) @ expressions value
    | TypeDefStatement (_, _, parameters, typeExpr) ->
        (parameters |> List.collect (snd >> Option.toList >> List.collect expressionsOfType)) @ expressionsOfType typeExpr
    | ImportStatement _ -> []
    | UseStatement binding -> expressionsOfUse binding
    | ExprStatement value -> expressions value

and private expressionsOfUse (binding: UseBinding) =
    match binding with
    | UseValue value -> expressions value
    | UseContextInstance (typeExpr, value) -> expressionsOfType typeExpr @ expressions value

and private expressionsOfPattern (pattern: Pattern) =
    match pattern with
    | LiteralPattern _
    | IdentifierPattern _
    | WildcardPattern
    | ElsePattern -> []
    | TuplePattern patterns
    | RecordPattern (_, patterns) -> patterns |> List.collect expressionsOfPattern
    | RecordMemberPattern members -> members |> List.collect (snd >> expressionsOfPattern)
    | ListPattern (patterns, rest) -> (patterns @ Option.toList rest) |> List.collect expressionsOfPattern
    | ListSplatMiddle (before, after) -> (before @ after) |> List.collect expressionsOfPattern
    | RangePattern (low, high) -> expressions low @ expressions high
    | GuardPattern (_, value) -> expressions value
    | TagPattern (_, payload) -> payload |> Option.toList |> List.collect expressionsOfPattern

and private expressionsOfType (typeExpr: TypeExpr) =
    match typeExpr with
    | TypeName _
    | TypeVar _
    | TypeUnit -> []
    | TypeTuple types
    | TypeUnion types
    | TypeIntersection types
    | TypeContext types
    | TypeApply (_, types) -> types |> List.collect expressionsOfType
    | TypeRecord fields ->
        fields |> List.collect (function
            | TypeField (_, _, _, fieldType, defaultValue) ->
                (fieldType |> Option.toList |> List.collect expressionsOfType)
                @ (defaultValue |> Option.toList |> List.collect expressions)
            | TypeMember (_, memberType) -> memberType |> Option.toList |> List.collect expressionsOfType)
    | TypeFunc (input, output) -> expressionsOfType input @ expressionsOfType output
    | TypeTag (_, payload) -> payload |> Option.toList |> List.collect expressionsOfType
    | TypeOptional inner -> expressionsOfType inner
    | TypeWithContext (contexts, inner) -> (contexts |> List.collect expressionsOfType) @ expressionsOfType inner
    | TypeConstraint (_, constrained, value) -> expressionsOfType constrained @ expressions value
    | TypeWhere (constrained, value) -> expressionsOfType constrained @ expressions value

let rec private expressionsOfItem (item: TopLevelItem) =
    match item with
    | ModuleDecl _
    | Import _
    | Attribute _ -> []
    | ModuleBlock (_, items) -> items |> List.collect expressionsOfItem
    | Def (ValueDef (_, _, typeOpt, value)) -> (typeOpt |> Option.toList |> List.collect expressionsOfType) @ expressions value
    | Def (TypeDef (_, _, parameters, typeExpr)) ->
        (parameters |> List.collect (snd >> Option.toList >> List.collect expressionsOfType)) @ expressionsOfType typeExpr
    | Expr value -> expressions value

/// Checks that every expression parsed from `source` has a span within it that starts and ends on a
/// non-whitespace character, whose line and column agree with its offset. Returns how many it checked.
let private checkAllSpans (file: string) (rawSource: string) =
    // Spans are offsets into the source as the parser normalises it.
    let source = rawSource.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd()
    let module' =
        match parseModuleFile file rawSource with
        | Result.Ok module' -> module'
        | Result.Error err -> failwith $"Parse failed for {file}: {err}"
    let all = module' |> List.collect expressionsOfItem
    for expr in all do
        let span = expr.Span
        let describe () = $"{file}: %A{span} of %A{expr.Kind}"
        if SourceSpan.isNone span then failwith $"No span: {describe ()}"
        if span.File <> file then failwith $"Wrong file: {describe ()}"
        if span.Start < 0 || span.End > source.Length || span.End <= span.Start then
            failwith $"Span outside the source or empty: {describe ()}"
        if System.Char.IsWhiteSpace source.[span.Start] || System.Char.IsWhiteSpace source.[span.End - 1] then
            failwith $"Span starts or ends with whitespace ('{textOf source span}'): {describe ()}"
        let before = source.Substring(0, span.Start)
        let line = before |> Seq.filter ((=) '\n') |> Seq.length
        let column = span.Start - (before.LastIndexOf('\n') + 1)
        if span.StartLine <> line || span.StartColumn <> column then
            failwith $"Line/column {span.StartLine}:{span.StartColumn} should be {line}:{column}: {describe ()}"
    all.Length

[<Fact>]
let ``every expression in the comprehensive sample has a real span within the source`` () =
    // The parser gives the nodes it makes up when desugaring (shorthand lambda parameters, `else ()`, `__range`,
    // `__with`, `__for`, destructuring temporaries, ...) the span of the construct they came from, so every node
    // has one.
    let path = Path.Combine(__SOURCE_DIRECTORY__, "..", "Integration", "comprehensive.nyx")
    checkAllSpans path (File.ReadAllText path) |> should be (greaterThan 20)

[<Fact>]
let ``every expression in every parseable feature file has a real span within the source`` () =
    let featureRoot = Path.Combine(__SOURCE_DIRECTORY__, "..")
    let checkedFiles =
        Directory.GetFiles(featureRoot, "*.nyx", SearchOption.AllDirectories)
        |> Array.filter (fun path -> parseModuleFile path (File.ReadAllText path) |> Result.isOk)
    checkedFiles.Length |> should be (greaterThan 50)
    for path in checkedFiles do
        checkAllSpans path (File.ReadAllText path) |> ignore
