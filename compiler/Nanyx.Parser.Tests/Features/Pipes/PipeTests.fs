module ParserTests.Features.Pipes

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers

[<Fact>]
let ``Parse piping.nyx file`` () =
    let result = parseTestFile "piping.nyx"

    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> List.length |> should be (greaterThan 5)
        match module'.[0] with
        | Kind (ModuleDecl name) -> name |> should equal "Piping"
        | _ -> failwith "Expected ModuleDecl"

        let hasPipe =
            module' |> List.exists (fun item ->
                match item with
                | Kind (Def (ValueDef (_, _, _, Kind (Pipe _)))) -> true
                | _ -> false
            )
        hasPipe |> should equal true
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``A pipe target may take named arguments`` () =
    match parseModule "def r = x \\f(a, y = z)" with
    | Result.Ok [ Kind (Def (ValueDef (_, _, _, Kind (Pipe (_, "f", _, [ args ]))))) ] ->
        match args.Kind with
        | RecordExpr [ PositionalField (Kind (IdentifierExpr "a")); NamedField ("y", Kind (IdentifierExpr "z")) ] -> ()
        | other -> failwith $"Expected record arguments, got: {other}"
    | other -> failwith $"Expected a single def of a pipe, got: {other}"

let private callArgumentsOf source =
    match parseModule source with
    | Result.Ok [ Kind (Def (ValueDef (_, _, _, Kind (Pipe (value, _, _, args))))) ] -> pipeCallArguments value args |> List.map _.Kind
    | other -> failwith $"Expected a single def of a pipe, got: {other}"

[<Fact>]
let ``A piped value goes in front of several written arguments`` () =
    match callArgumentsOf "def r = x \\f(a, b) { y -> y }" with
    | [ IdentifierExpr "x"; IdentifierExpr "a"; IdentifierExpr "b"; Lambda _ ] -> ()
    | other -> failwith $"Expected x, a, b and the lambda, got: {other}"

[<Fact>]
let ``A piped value goes in front of named arguments, in the same record`` () =
    match callArgumentsOf "def r = x \\f(a, y = z) { w -> w }" with
    | [ RecordExpr [ PositionalField (Kind (IdentifierExpr "x"))
                     PositionalField (Kind (IdentifierExpr "a"))
                     PositionalField (Kind (Lambda _))
                     NamedField ("y", Kind (IdentifierExpr "z")) ] ] -> ()
    | other -> failwith $"Expected one record of x, a, the lambda and y, got: {other}"
