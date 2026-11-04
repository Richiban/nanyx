module ParserTests.Features.If

open Xunit
open FsUnit.Xunit
open ParserTestHelpers
open System.IO

[<Fact>]
let ``Parse if feature files`` () =
    let unsupported = set [ "test_if_advanced.nyx" ]

    parseFeatureNyxFiles "If"
    |> Array.filter (fun (filePath, _) -> not (unsupported.Contains(Path.GetFileName(filePath))))
    |> Array.iter (fun (filePath, result) ->
        match result with
        | Result.Ok _ -> ()
        | Result.Error err -> failwith $"Parse failed for {filePath}: {err}"
    )

open Parser.Program

[<Fact>]
let ``If-matching without else is a match with a wildcard arm returning unit`` () =
    match parseModule "def f = if #some(v) = get(3) then v" with
    | Result.Ok [ Def (ValueDef (_, "f", _, Match ([ FunctionCall ("get", _, [ LiteralExpr (IntLit 3) ]) ],
                                                   [ ([ TagPattern ("some", Some (IdentifierPattern "v")) ], IdentifierExpr ("v", _));
                                                     ([ WildcardPattern ], UnitExpr) ]))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``If-matching with else puts the else body in the wildcard arm`` () =
    match parseModule "def f = if #some(v) = get(3) then v else 0" with
    | Result.Ok [ Def (ValueDef (_, "f", _, Match (_, [ ([ TagPattern ("some", Some (IdentifierPattern "v")) ], IdentifierExpr ("v", _));
                                                      ([ WildcardPattern ], LiteralExpr (IntLit 0)) ]))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``A plain if with an equality test is still an if expression`` () =
    match parseModule "def f = if x == 3 then 1 else 2" with
    | Result.Ok [ Def (ValueDef (_, "f", _, IfExpr (BinaryOp ("==", _, _), _, _))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"
