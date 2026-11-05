module ParserTests.Features.Types

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers

[<Fact>]
let ``Parse type definition feature files`` () =
    parseFeatureNyxFiles "Types"
    |> Array.iter (fun (filePath, result) ->
        result |> isOk |> should equal true
        match result with
        | Result.Error err -> failwith $"Parse failed for {filePath}: {err}"
        | _ -> ()
    )

[<Fact>]
let ``def annotation can start an indented continuation line`` () =
    let source =
        "def mapOption\n" +
        "  : (#some(a) | r), (a -> b) -> (#some(b) | r)\n" +
        "  = { | #some(a), f -> #some(f(a))\n" +
        "      | other, _ -> other }\n"
    let optionOf payload = TypeUnion [ TypeTag ("some", Some (TypeVar payload)); TypeVar "r" ]
    let expected = TypeFunc (TypeTuple [ optionOf "a"; TypeFunc (TypeVar "a", TypeVar "b") ], optionOf "b")
    match parseModule source with
    | Result.Ok [ Def (ValueDef (_, "mapOption", Some annotation, Kind (Lambda _))) ] -> annotation |> should equal expected
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``bare parameters in a def annotation bind tighter than the arrow`` () =
    let annotationOf source =
        match parseModule source with
        | Result.Ok [ Def (ValueDef (_, _, Some annotation, _)) ] -> annotation
        | other -> failwith $"Unexpected parse: %A{other}"
    let expected = TypeFunc (TypeTuple [ TypeName "int"; TypeName "int" ], TypeName "int")
    annotationOf "def add: int, int -> int = { x, y -> x + y }" |> should equal expected
    annotationOf "def add: (int, int) -> int = { x, y -> x + y }" |> should equal expected

[<Fact>]
let ``lambda parameters still split at commas`` () =
    match parseModule "def f = { x: int, y -> x + y }" with
    | Result.Ok [ Def (ValueDef (_, "f", None, Kind (Lambda ([ ("x", Some (TypeName "int")); ("y", None) ], _)))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"
