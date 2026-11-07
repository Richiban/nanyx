module ParserTests.Features.Functions

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers

[<Fact>]
let ``Parse function-calls.nyx file`` () =
    let result = parseTestFile "function-calls.nyx"

    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 7  // 1 module + 6 function call defs
        match module'.[0] with
        | Kind (ModuleDecl name) -> name |> should equal "FunctionCalls"
        | _ -> failwith "Expected ModuleDecl"

        let defs = module' |> List.tail
        for def in defs do
            match def with
            | Kind (Def (ValueDef (_, _, _, Kind (FunctionCall _)))) -> ()
            | _ -> failwith "Expected all definitions to contain function calls"
    | Result.Error err -> failwith $"Parse failed: {err}"
