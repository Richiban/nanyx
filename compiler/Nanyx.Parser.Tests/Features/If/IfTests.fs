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
let ``multi-branch if desugars to nested ifs ending in the else arm`` () =
    match parseModule "def x = if | a -> 1 | b -> 2 | else -> 3" with
    | Result.Ok [ Def (ValueDef (_, "x", _, IfExpr (IdentifierExpr ("a", _), LiteralExpr (IntLit 1),
                                                    IfExpr (IdentifierExpr ("b", _), LiteralExpr (IntLit 2), LiteralExpr (IntLit 3))))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``multi-branch if arms may test patterns with is, and a missing else is unit`` () =
    let source =
        "def x = { s ->\n" +
        "  if | int.parse(s) is #some(i) -> i\n" +
        "     | s == \"\" ->\n" +
        "       0\n" +
        "}\n"
    match parseModule source with
    | Result.Ok [ Def (ValueDef (_, "x", _, Lambda (_, Block [ ExprStatement (Match ([ FunctionCall _ ],
                                                               [ [ TagPattern ("some", Some (IdentifierPattern "i")) ], IdentifierExpr ("i", _)
                                                                 [ WildcardPattern ], IfExpr (BinaryOp ("==", _, _), _, UnitExpr) ])) ]))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``multi-branch if inside a match arm stops at its else arm`` () =
    let source =
        "def x = match n\n" +
        "  | 0 -> if | a -> 1 | else -> 2\n" +
        "  | _ -> 3\n"
    match parseModule source with
    | Result.Ok [ Def (ValueDef (_, "x", _, Match (_, [ [ LiteralPattern (IntLit 0) ], IfExpr _
                                                        [ WildcardPattern ], LiteralExpr (IntLit 3) ]))) ] -> ()
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
