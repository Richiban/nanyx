module ParserTests.Features.Tags

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers
open System.IO

let private payloadOf source =
    match parseModule source with
    | Result.Ok [ Kind (Def (ValueDef (_, _, _, Kind (TagExpr (_, Some payload))))) ] -> payload.Kind
    | other -> failwith $"Expected a single def of a tag with a payload, got: {other}"

[<Fact>]
let ``A tag payload may name its fields without extra parentheses`` () =
    match payloadOf "def x = #some(y = z)" with
    | RecordExpr [ NamedField ("y", Kind (IdentifierExpr "z")) ] -> ()
    | other -> failwith $"Expected a record payload, got: {other}"

[<Fact>]
let ``A tag payload may mix positional and named fields`` () =
    match payloadOf "def x = #ok(a, y = z)" with
    | RecordExpr [ PositionalField (Kind (IdentifierExpr "a")); NamedField ("y", Kind (IdentifierExpr "z")) ] -> ()
    | other -> failwith $"Expected a record payload, got: {other}"

[<Fact>]
let ``A tag payload of several values is a tuple`` () =
    match payloadOf "def x = #pair(a, b)" with
    | TupleExpr [ Kind (IdentifierExpr "a"); Kind (IdentifierExpr "b") ] -> ()
    | other -> failwith $"Expected a tuple payload, got: {other}"

[<Fact>]
let ``Parse tag feature files`` () =
    parseFeatureNyxFiles "Tags"
    |> Array.iter (fun (filePath, result) ->
        match result with
        | Result.Ok _ -> ()
        | Result.Error err -> failwith $"Parse failed for {filePath}: {err}"
    )
