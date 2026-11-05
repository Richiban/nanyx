module ParserTests.Features.Records

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers

[<Fact>]
let ``record update with is a call to __with on the record and the updated fields`` () =
    match parseModule "def older = alice with age = 31, name = \"Al\"" with
    | Result.Ok [ Def (ValueDef (_, "older", _, Kind (FunctionCall ("__with", None, [ Kind (IdentifierExpr "alice"); Kind (RecordExpr [ NamedField ("age", Kind (LiteralExpr (IntLit 31))); NamedField ("name", Kind (LiteralExpr (StringLit "Al"))) ]) ])))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``with applies to a member access and does not swallow identifiers starting with with`` () =
    match parseModule "def p = team.lead with age = 1\ndef q = without" with
    | Result.Ok [ Def (ValueDef (_, "p", _, Kind (FunctionCall ("__with", None, [ Kind (MemberAccess (Kind (IdentifierExpr "team"), "lead", _)); Kind (RecordExpr [ _ ]) ]))))
                  Def (ValueDef (_, "q", _, Kind (IdentifierExpr "without"))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``Parse record feature files`` () =
    parseFeatureNyxFiles "Records"
    |> Array.iter (fun (filePath, result) ->
        result |> isOk |> should equal true
        match result with
        | Result.Error err -> failwith $"Parse failed for {filePath}: {err}"
        | _ -> ()
    )
