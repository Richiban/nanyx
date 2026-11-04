module ParserTests.Features.Attributes

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers

[<Fact>]
let ``Parse attribute feature files`` () =
    parseFeatureNyxFiles "Attributes"
    |> Array.iter (fun (filePath, result) ->
        match result with
        | Result.Ok _ -> ()
        | Result.Error err -> failwith $"Parse failed for {filePath}: {err}")

[<Fact>]
let ``An attribute line precedes the definition it annotates`` () =
    match parseModule "@entry\ndef main = { 1 }" with
    | Result.Ok [ Attribute "entry"; Def (ValueDef(false, "main", None, _)) ] -> ()
    | other -> failwith $"Unexpected parse result: %A{other}"

[<Fact>]
let ``An attribute needs a name`` () =
    parseModule "@\ndef main = { 1 }" |> isError |> should equal true
