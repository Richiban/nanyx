module ParserTests.Features.FieldAccess

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers

[<Fact>]
let ``a member of a call's result is a member access, while C(T).f is a context access`` () =
    match parseModule "def a = openFile(path).contents\ndef b = ToString(a).toString(x)" with
    | Result.Ok [ Def (ValueDef (_, "a", _, MemberAccess (FunctionCall ("openFile", _, _), "contents", _)))
                  Def (ValueDef (_, "b", _, ContextMemberCall (TypeApply ("ToString", _), "toString", _))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``Parse field access feature files`` () =
    parseFeatureNyxFiles "FieldAccess"
    |> Array.iter (fun (filePath, result) ->
        result |> isOk |> should equal true
        match result with
        | Result.Error err -> failwith $"Parse failed for {filePath}: {err}"
        | _ -> ()
    )
