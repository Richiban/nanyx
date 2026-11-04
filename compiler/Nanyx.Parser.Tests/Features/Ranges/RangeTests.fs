module ParserTests.Features.Ranges

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers
open System.IO

[<Fact>]
let ``a..b is a call to __range that binds looser than arithmetic`` () =
    match parseModule "def r = n - 1..n + 1" with
    | Result.Ok [ Def (ValueDef (_, "r", _, FunctionCall ("__range", None, [ BinaryOp ("-", _, _); BinaryOp ("+", _, _) ]))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``for with arms is a call to __for with a pattern lambda, and -> without an element`` () =
    match parseModule "def a = for 1..3 | i -> f(i)\ndef b = for xs -> g()" with
    | Result.Ok [ Def (ValueDef (_, "a", _, FunctionCall ("__for", None, [ FunctionCall ("__range", _, _); Lambda ([ (element, None) ], Match ([ IdentifierExpr (scrutinee, _) ], [ [ IdentifierPattern "i" ], FunctionCall ("f", _, _) ])) ])))
                  Def (ValueDef (_, "b", _, FunctionCall ("__for", None, [ IdentifierExpr ("xs", _); Lambda ([ _ ], FunctionCall ("g", _, _)) ]))) ] ->
        scrutinee |> should equal element
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``rec is a def whose value is wrapped in __rec`` () =
    match parseModule "def f = {\n  rec loop = { i -> loop(i) }\n  loop(1)\n}" with
    | Result.Ok [ Def (ValueDef (_, "f", _, Lambda ([], Block [ DefStatement (false, "loop", None, FunctionCall ("__rec", None, [ Lambda _ ])); ExprStatement _ ]))) ] -> ()
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
