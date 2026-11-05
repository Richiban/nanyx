module ParserTests.Features.Lambdas

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers

[<Fact>]
let ``Parse lambdas.nyx file`` () =
    let result = parseTestFile "lambdas.nyx"

    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 7  // 1 module + 6 lambda defs
        match module'.[0] with
        | ModuleDecl name -> name |> should equal "Lambdas"
        | _ -> failwith "Expected ModuleDecl"

        let defs = module' |> List.tail
        for def in defs do
            match def with
            | Def (ValueDef (_, _, _, Lambda _)) -> ()
            | _ -> failwith "Expected all definitions to contain lambdas"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``Parse shorthand-lambdas.nyx file`` () =
    let result = parseTestFile "shorthand-lambdas.nyx"

    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 15  // 1 module + 14 shorthand lambda defs
        match module'.[0] with
        | ModuleDecl name -> name |> should equal "ShorthandLambdas"
        | _ -> failwith "Expected ModuleDecl"

        let defs = module' |> List.tail
        for def in defs do
            match def with
            | Def (ValueDef (_, _, _, Lambda _)) -> ()
            | Def (ValueDef (_, _, _, FunctionCall (_, _, args))) ->
                let rec hasLambda arg =
                    match arg with
                    | Lambda _ -> true
                    | TupleExpr exprs -> exprs |> List.exists hasLambda
                    | _ -> false
                let foundLambda = args |> List.exists hasLambda
                foundLambda |> should equal true
            | _ -> failwith "Expected lambda or function call with lambda"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``an empty block is a lambda returning unit`` () =
    match parseModule "def main = {\n}\ndef noop = { }" with
    | Result.Ok [ Def (ValueDef (_, "main", None, Lambda ([], UnitExpr))); Def (ValueDef (_, "noop", None, Lambda ([], UnitExpr))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"
