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
        | ModuleDecl name -> name |> should equal "Piping"
        | _ -> failwith "Expected ModuleDecl"

        let hasPipe =
            module' |> List.exists (fun item ->
                match item with
                | Def (ValueDef (_, _, _, Kind (Pipe _))) -> true
                | _ -> false
            )
        hasPipe |> should equal true
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``A pipe target may take named arguments`` () =
    match parseModule "def r = x \\f(a, y = z)" with
    | Result.Ok [ Def (ValueDef (_, _, _, Kind (Pipe (_, "f", _, [ args ])))) ] ->
        match args.Kind with
        | RecordExpr [ PositionalField (Kind (IdentifierExpr "a")); NamedField ("y", Kind (IdentifierExpr "z")) ] -> ()
        | other -> failwith $"Expected record arguments, got: {other}"
    | other -> failwith $"Expected a single def of a pipe, got: {other}"
