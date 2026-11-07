module ParserTests.Features.Blocks

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers

[<Fact>]
let ``Parse block with two literals`` () =
    let result = parseTestFile "test_block_two_literals.nyx"

    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Kind (Def (ValueDef (_, name, _, Kind (Block statements)))) ->
            name |> should equal "test"
            statements |> should haveLength 2
            match statements.[0] with
            | Kind (ExprStatement (Kind (LiteralExpr (IntLit 1)))) -> ()
            | _ -> failwith "Expected first statement to be literal 1"
            match statements.[1] with
            | Kind (ExprStatement (Kind (LiteralExpr (IntLit 2)))) -> ()
            | _ -> failwith "Expected second statement to be literal 2"
        | _ -> failwith "Expected a block with two literal statements"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``Parse sample-block.nyx file`` () =
    let result = parseTestFile "sample-block.nyx"

    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> List.length |> should be (greaterThan 2)
        match module'.[0] with
        | Kind (ModuleDecl name) -> name |> should equal "BlockTest"
        | _ -> failwith "Expected ModuleDecl"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``Parse sibling blocks`` () =
    let result = parseTestFile "test_block_sibling_blocks.nyx"

    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 2
        match module'.[0], module'.[1] with
        | Kind (Def (ValueDef (_, firstName, _, Kind (Block firstStatements)))), Kind (Def (ValueDef (_, secondName, _, Kind (Block secondStatements)))) ->
            firstName |> should equal "first"
            secondName |> should equal "second"
            firstStatements |> should haveLength 2
            secondStatements |> should haveLength 2
        | _ -> failwith "Expected two sibling block definitions"
    | Result.Error err -> failwith $"Parse failed: {err}"
