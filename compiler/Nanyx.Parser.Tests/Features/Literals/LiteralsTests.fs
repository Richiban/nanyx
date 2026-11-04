module ParserTests.Features.Literals

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers

[<Fact>]
let ``list spread desugars to concatenation`` () =
    match parseModule "def x = [1, ...xs, 2, 3, ...ys]" with
    | Result.Ok [ Def (ValueDef (_, "x", _, BinaryOp ("++", ListExpr [ LiteralExpr (IntLit 1) ],
                                                     BinaryOp ("++", IdentifierExpr ("xs", _),
                                                        BinaryOp ("++", ListExpr [ LiteralExpr (IntLit 2); LiteralExpr (IntLit 3) ],
                                                                        IdentifierExpr ("ys", _)))))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``a list of only a spread is the spread itself, and no spread is a plain list`` () =
    match parseModule "def x = [...xs]\ndef y = [1, 2]\ndef z = []" with
    | Result.Ok [ Def (ValueDef (_, "x", _, IdentifierExpr ("xs", _)))
                  Def (ValueDef (_, "y", _, ListExpr [ _; _ ]))
                  Def (ValueDef (_, "z", _, ListExpr [])) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``Parse literals.nyx file`` () =
    let result = parseTestFile "literals.nyx"

    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 6  // 1 module + 5 defs
        match module'.[0] with
        | ModuleDecl name -> name |> should equal "Literals"
        | _ -> failwith "Expected ModuleDecl"

        let defs = module' |> List.tail
        defs |> should haveLength 5
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``Parse interpolated string literal`` () =
    let source = "def msg = \"Hello {name}\""
    match parseModule source with
    | Result.Error err -> failwith $"Parse failed: {err}"
    | Result.Ok module' ->
        match module' with
        | [Def (ValueDef(_, "msg", _, InterpolatedString parts))] ->
            parts |> should haveLength 2
        | _ -> failwith "Expected interpolated string"

[<Fact>]
let ``Parse single-quoted string without interpolation`` () =
    let source = "def msg = 'Hello {name}'"
    match parseModule source with
    | Result.Error err -> failwith $"Parse failed: {err}"
    | Result.Ok module' ->
        match module' with
        | [Def (ValueDef(_, "msg", _, LiteralExpr (StringLit value)))] ->
            value |> should equal "Hello {name}"
        | _ -> failwith "Expected plain string literal"

[<Fact>]
let ``Parse multiline string literal`` () =
    let source = "def msg = \"Hello\nworld\""
    match parseModule source with
    | Result.Error err -> failwith $"Parse failed: {err}"
    | Result.Ok module' ->
        match module' with
        | [Def (ValueDef(_, "msg", _, LiteralExpr (StringLit value)))] ->
            value |> should equal "Hello\nworld"
        | _ -> failwith "Expected multiline string literal"

[<Fact>]
let ``Parse dotted def name`` () =
    let source = "def Person.fullName = { p -> p.firstName }"
    match parseModule source with
    | Result.Error err -> failwith $"Parse failed: {err}"
    | Result.Ok module' ->
        match module' with
        | [Def (ValueDef(_, name, _, _))] -> name |> should equal "Person.fullName"
        | _ -> failwith "Expected dotted def name"
