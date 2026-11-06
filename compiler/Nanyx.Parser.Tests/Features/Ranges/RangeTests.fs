module ParserTests.Features.Ranges

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers
open System.IO

[<Fact>]
let ``a..b is a call to __range that binds looser than arithmetic`` () =
    match parseModule "def r = n - 1..n + 1" with
    | Result.Ok [ Def (ValueDef (_, "r", _, Kind (FunctionCall ("__range", None, [ Kind (BinaryOp ("-", _, _)); Kind (BinaryOp ("+", _, _)) ])))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``for with arms is a call to __for with a pattern lambda, and -> without an element`` () =
    match parseModule "def a = for 1..3 | i -> f(i)\ndef b = for xs -> g()" with
    | Result.Ok [ Def (ValueDef (_, "a", _, Kind (FunctionCall ("__for", None, [ Kind (FunctionCall ("__range", _, _)); Kind (Lambda ([ (element, None) ], Kind (Match ([ Kind (IdentifierExpr scrutinee) ], [ [ Kind (IdentifierPattern "i") ], Kind (FunctionCall ("f", _, _)) ])))) ]))))
                  Def (ValueDef (_, "b", _, Kind (FunctionCall ("__for", None, [ Kind (IdentifierExpr "xs"); Kind (Lambda ([ _ ], Kind (FunctionCall ("g", _, _)))) ])))) ] ->
        scrutinee |> should equal element
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``rec is a def whose value is wrapped in __rec`` () =
    match parseModule "def f = {\n  rec loop = { i -> loop(i) }\n  loop(1)\n}" with
    | Result.Ok [ Def (ValueDef (_, "f", _, Kind (Lambda ([], Kind (Block [ DefStatement (false, "loop", None, Kind (FunctionCall ("__rec", None, [ Kind (Lambda _) ]))); ExprStatement _ ]))))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``Parse range feature files`` () =
    let unsupported = set [ "test_simple_range_expr.nyx" ]

    parseFeatureNyxFiles "Ranges"
    |> Array.filter (fun (filePath, _) -> not (unsupported.Contains(Path.GetFileName(filePath))))
    |> Array.iter (fun (filePath, result) ->
        match result with
        | Result.Ok _ -> ()
        | Result.Error err -> failwith $"Parse failed for {filePath}: {err}"
    )
