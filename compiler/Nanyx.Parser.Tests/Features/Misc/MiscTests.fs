module ParserTests.Features.Misc

open Xunit
open FsUnit.Xunit
open ParserTestHelpers
open Parser.Program

[<Fact>]
let ``a workflow keyword applied to a value is reported where it is written`` () =
    let source =
        "def parse = { s ->\n"
        + "  proc {\n"
        + "    return Version(\n"
        + "      major = try parseNumber(s)\n"
        + "    )\n"
        + "  }\n"
        + "}\n"
    match parseModule source with
    | Result.Error err ->
        err |> should haveSubstring "Ln: 4 Col: 15"
        err |> should haveSubstring "`try` is applied to a value without parentheses"
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"

[<Fact>]
let ``a workflow keyword is not split into a name and a statement of its own`` () =
    match parseModule "def f = proc {\n  def a = try g(1)\n  return a\n}\n" with
    | Result.Error err -> err |> should haveSubstring "Ln: 2 Col: 11"
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"

[<Fact>]
let ``a name followed by a keyword operator is not a workflow keyword`` () =
    match parseModule "def f = { a, b -> if a and b then a else b }\n" with
    | Result.Ok _ -> ()
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``outside a lambda two names in a row are not taken for a workflow keyword`` () =
    match parseModule "pattern string -> int = { s -> 1 }\n" with
    | Result.Error err -> err |> should not' (haveSubstring "workflow keyword")
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"

[<Fact>]
let ``Parse misc feature files`` () =
    parseFeatureNyxFiles "Misc"
    |> Array.iter (fun (filePath, result) ->
        result |> isOk |> should equal true
        match result with
        | Result.Error err -> failwith $"Parse failed for {filePath}: {err}"
        | _ -> ()
    )
