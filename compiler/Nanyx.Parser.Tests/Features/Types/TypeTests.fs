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
let ``comma binds tighter than the arrow inside brackets too`` () =
    let annotationOf source =
        match parseModule source with
        | Result.Ok [ Def (ValueDef (_, _, Some annotation, _)) ] -> annotation
        | other -> failwith $"Unexpected parse: %A{other}"
    let pair = TypeTuple [ TypeName "int"; TypeVar "a" ]
    // In brackets, `int, a -> b` is still a function of two parameters.
    annotationOf "def f: (int, a -> b) -> b = { g -> g(1, 2) }"
    |> should equal (TypeFunc (TypeFunc (pair, TypeVar "b"), TypeVar "b"))
    // A function that is an item has brackets of its own, wherever items are separated by commas.
    annotationOf "def p: (int, (a -> b)) = (1, { x -> x })"
    |> should equal (TypeTuple [ TypeName "int"; TypeFunc (TypeVar "a", TypeVar "b") ])
    annotationOf "def m: map(string, (a -> b)) = m" |> should equal (TypeApply ("map", [ TypeName "string"; TypeFunc (TypeVar "a", TypeVar "b") ]))
    annotationOf "def m: map(string, a -> b) = m" |> should equal (TypeApply ("map", [ TypeFunc (TypeTuple [ TypeName "string"; TypeVar "a" ], TypeVar "b") ]))
    annotationOf "def t: #pair(int, a) = t" |> should equal (TypeTag ("pair", Some pair))
    annotationOf "def t: #apply(int, a -> b) = t" |> should equal (TypeTag ("apply", Some (TypeFunc (pair, TypeVar "b"))))
    annotationOf "def t: #field(name: int) = t" |> should equal (TypeTag ("field", Some (TypeRecord [ TypeField ("name", false, false, Some (TypeName "int"), None) ])))
    // A union binds tighter than the arrow, so it needs no brackets on either side of one.
    let union = TypeUnion [ TypeTag ("some", Some (TypeName "int")); TypeTag ("none", None) ]
    annotationOf "def u: #some(int) | #none -> #some(int) | #none = u" |> should equal (TypeFunc (union, union))
    // A record field's and a lambda parameter's commas are their own, so a function there brackets its parameters.
    annotationOf "def r: (combine: (a, a) -> a) = r"
    |> should equal (TypeRecord [ TypeField ("combine", false, false, Some (TypeFunc (TypeTuple [ TypeVar "a"; TypeVar "a" ], TypeVar "a")), None) ])

[<Fact>]
let ``lambda parameters still split at commas`` () =
    match parseModule "def f = { x: int, y -> x + y }" with
    | Result.Ok [ Def (ValueDef (_, "f", None, Kind (Lambda ([ ("x", Some (TypeName "int")); ("y", None) ], _)))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``typeof names a value as a type`` () =
    let annotationOf source =
        match parseModule source with
        | Result.Ok [ Def (ValueDef (_, _, Some annotation, _)) ] -> annotation
        | other -> failwith $"Unexpected parse: %A{other}"
    let value name = Expression.synthetic (IdentifierExpr name)
    let defaults = TypeOf (value "defaults")
    annotationOf "def o: typeof defaults = d" |> should equal defaults
    // Brackets only group the value.
    annotationOf "def o: typeof(defaults) = d" |> should equal defaults
    annotationOf "def o: typeof ( defaults ) = d" |> should equal defaults
    // A path of fields names the value of a field.
    annotationOf "def p: typeof config.server.port = p"
    |> should equal (TypeOf (Expression.synthetic (MemberAccess (Expression.synthetic (MemberAccess (value "config", "server", None)), "port", None))))
    // It is a type like any other, and binds as tightly as one.
    annotationOf "def o: typeof x? = o" |> should equal (TypeOptional (TypeOf (value "x")))
    annotationOf "def f: typeof x -> typeof y = f" |> should equal (TypeFunc (TypeOf (value "x"), TypeOf (value "y")))
    annotationOf "def l: list(typeof x) = l" |> should equal (TypeApply ("list", [ TypeOf (value "x") ]))

[<Fact>]
let ``typeof is a keyword only on its own`` () =
    match parseModule "def t: typeofThing = t" with
    | Result.Ok [ Def (ValueDef (_, _, Some (TypeName "typeofThing"), _)) ] -> ()
    | other -> failwith $"Unexpected parse: %A{other}"

[<Fact>]
let ``typeof in a module block names the block's own value`` () =
    let source =
        "module config =\n" +
        "  def defaults = (port = 80)\n" +
        "  def custom: typeof defaults = (port = 8080)\n"
    match parseModule source with
    | Result.Ok items ->
        let annotation =
            items
            |> List.pick (function
                | Def (ValueDef (_, "config.custom", Some annotation, _)) -> Some annotation
                | _ -> None)
        annotation |> should equal (TypeOf (Expression.synthetic (IdentifierExpr "config.defaults")))
    | Result.Error err -> failwith $"Parse failed: {err}"