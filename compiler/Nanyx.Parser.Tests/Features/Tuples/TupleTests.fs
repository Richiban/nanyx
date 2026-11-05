module ParserTests.Features.Tuples

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers

[<Fact>]
let ``def (x, y) desugars into a whole-value binding and one def per element`` () =
    match parseModule "def (x, _, y) = point" with
    | Result.Ok [ Def (ValueDef (_, temp, None, Kind (IdentifierExpr "point")))
                  Def (ValueDef (_, "x", None, Kind (MemberAccess (Kind (IdentifierExpr temp1), "0", None))))
                  Def (ValueDef (_, "y", None, Kind (MemberAccess (Kind (IdentifierExpr temp2), "2", None)))) ] ->
        temp1 |> should equal temp
        temp2 |> should equal temp
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``def with a nested tuple pattern destructures through member access chains`` () =
    match parseModule "def main = {\n  def (a, (b, c)) = v\n  a\n}" with
    | Result.Ok [ Def (ValueDef (_, "main", None, Kind (Lambda ([], Kind (Block [ DefStatement (_, _, None, Kind (IdentifierExpr "v"))
                                                                                  DefStatement (_, "a", None, Kind (MemberAccess (_, "0", None)))
                                                                                  DefStatement (_, "b", None, Kind (MemberAccess (Kind (MemberAccess (_, "1", None)), "0", None)))
                                                                                  DefStatement (_, "c", None, Kind (MemberAccess (Kind (MemberAccess (_, "1", None)), "1", None)))
                                                                                  ExprStatement (Kind (IdentifierExpr "a")) ]))))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``def cannot destructure with a literal in the pattern`` () =
    match parseModule "def (1, y) = point" with
    | Result.Error _ -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"

[<Fact>]
let ``Parse tuple feature files`` () =
    parseFeatureNyxFiles "Tuples"
    |> Array.iter (fun (filePath, result) ->
        result |> isOk |> should equal true
        match result with
        | Result.Error err -> failwith $"Parse failed for {filePath}: {err}"
        | _ -> ()
    )
