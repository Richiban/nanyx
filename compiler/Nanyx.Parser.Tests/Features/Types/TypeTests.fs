module ParserTests.Features.Types

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers

// Type expressions to compare annotations with. Equality ignores where a type was written, so these have no span.
let private ty kind = TypeExpr.synthetic kind
let private named name = ty (TypeName name)
let private var name = ty (TypeVar name)
let private func input output = ty (TypeFunc (input, output))
let private tuple items = ty (TypeTuple items)
let private tag name payload = ty (TypeTag (name, payload))

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
    let optionOf payload = ty (TypeUnion [ tag "some" (Some (var payload)); var "r" ])
    let expected = func (tuple [ optionOf "a"; func (var "a") (var "b") ]) (optionOf "b")
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
    let expected = func (tuple [ named "int"; named "int" ]) (named "int")
    annotationOf "def add: int, int -> int = { x, y -> x + y }" |> should equal expected
    annotationOf "def add: (int, int) -> int = { x, y -> x + y }" |> should equal expected

[<Fact>]
let ``comma binds tighter than the arrow inside brackets too`` () =
    let annotationOf source =
        match parseModule source with
        | Result.Ok [ Def (ValueDef (_, _, Some annotation, _)) ] -> annotation
        | other -> failwith $"Unexpected parse: %A{other}"
    let pair = tuple [ named "int"; var "a" ]
    // In brackets, `int, a -> b` is still a function of two parameters.
    annotationOf "def f: (int, a -> b) -> b = { g -> g(1, 2) }"
    |> should equal (func (func pair (var "b")) (var "b"))
    // A function that is an item has brackets of its own, wherever items are separated by commas.
    annotationOf "def p: (int, (a -> b)) = (1, { x -> x })"
    |> should equal (tuple [ named "int"; func (var "a") (var "b") ])
    annotationOf "def m: map(string, (a -> b)) = m" |> should equal (ty (TypeApply ("map", [ named "string"; func (var "a") (var "b") ])))
    annotationOf "def m: map(string, a -> b) = m" |> should equal (ty (TypeApply ("map", [ func (tuple [ named "string"; var "a" ]) (var "b") ])))
    annotationOf "def t: #pair(int, a) = t" |> should equal (tag "pair" (Some pair))
    annotationOf "def t: #apply(int, a -> b) = t" |> should equal (tag "apply" (Some (func pair (var "b"))))
    annotationOf "def t: #field(name: int) = t" |> should equal (tag "field" (Some (ty (TypeRecord [ TypeField ("name", false, false, Some (named "int"), None) ]))))
    // A union binds tighter than the arrow, so it needs no brackets on either side of one.
    let union = ty (TypeUnion [ tag "some" (Some (named "int")); tag "none" None ])
    annotationOf "def u: #some(int) | #none -> #some(int) | #none = u" |> should equal (func union union)
    // A record field's and a lambda parameter's commas are their own, so a function there brackets its parameters.
    annotationOf "def r: (combine: (a, a) -> a) = r"
    |> should equal (ty (TypeRecord [ TypeField ("combine", false, false, Some (func (tuple [ var "a"; var "a" ]) (var "a")), None) ]))

[<Fact>]
let ``lambda parameters still split at commas`` () =
    match parseModule "def f = { x: int, y -> x + y }" with
    | Result.Ok [ Def (ValueDef (_, "f", None, Kind (Lambda ([ ("x", Some (Kind (TypeName "int"))); ("y", None) ], _)))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"
