module ParserTests.Features.If

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers
open System.IO

[<Fact>]
let ``if-matching desugars to a match with a unit wildcard arm`` () =
    match parseModule "def x = if get(3) is #some(v) then v" with
    | Result.Ok [ Def (ValueDef (_, "x", _, Match ([ FunctionCall ("get", _, _) ],
                                                   [ [ TagPattern ("some", Some (IdentifierPattern "v")) ], IdentifierExpr ("v", _)
                                                     [ WildcardPattern ], UnitExpr ]))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``if-matching with else uses the else as the wildcard arm`` () =
    match parseModule "def x = if p is (a, 0) then a else 0" with
    | Result.Ok [ Def (ValueDef (_, "x", _, Match ([ IdentifierExpr ("p", _) ],
                                                   [ [ TuplePattern [ IdentifierPattern "a"; LiteralPattern (IntLit 0) ] ], IdentifierExpr ("a", _)
                                                     [ WildcardPattern ], LiteralExpr (IntLit 0) ]))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``a condition without is stays a plain if, and is must end the keyword`` () =
    match parseModule "def x = if a == b then 1 else 2" with
    | Result.Ok [ Def (ValueDef (_, "x", _, IfExpr (BinaryOp ("==", _, _), _, _))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"
    match parseModule "def y = if island then 1 else 2" with
    | Result.Ok [ Def (ValueDef (_, "y", _, IfExpr (IdentifierExpr ("island", _), _, _))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

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
