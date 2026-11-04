module ParserTests.Features.Matches

open Xunit
open FsUnit.Xunit
open ParserTestHelpers
open System.IO

[<Fact>]
let ``Parse match feature files`` () =
    let unsupported = set [ "test_match_basic.nyx"; "test_match_binding.nyx"; "test_match_guard.nyx"; "test_match_lambda.nyx" ]

    parseFeatureNyxFiles "Matches"
    |> Array.filter (fun (filePath, _) -> not (unsupported.Contains(Path.GetFileName(filePath))))
    |> Array.iter (fun (filePath, result) ->
        match result with
        | Result.Ok _ -> ()
        | Result.Error err -> failwith $"Parse failed for {filePath}: {err}"
    )

open Parser.Program

[<Fact>]
let ``An or-pattern arm becomes one arm per alternative`` () =
    let source =
        "def f = { day ->\n" +
        "  match day\n" +
        "    | \"Sat\" | \"Sun\" -> \"weekend\"\n" +
        "    | _ -> \"weekday\"\n" +
        "}"
    match parseModule source with
    | Result.Ok [ Def (ValueDef (_, "f", _, Lambda (_, Block [ ExprStatement (Match (_, arms)) ]))) ] ->
        let patterns = arms |> List.map fst
        patterns |> should equal [ [ LiteralPattern (StringLit "Sat") ]; [ LiteralPattern (StringLit "Sun") ]; [ WildcardPattern ] ]
        // Both alternatives share the same body.
        snd arms.[0] |> should equal (snd arms.[1])
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``An or-pattern may put each alternative on its own line`` () =
    let source =
        "def f = { day ->\n" +
        "  match day\n" +
        "    | #sat\n" +
        "    | #sun -> 1\n" +
        "    | _ -> 0\n" +
        "}"
    match parseModule source with
    | Result.Ok [ Def (ValueDef (_, "f", _, Lambda (_, Block [ ExprStatement (Match (_, arms)) ]))) ] ->
        arms |> List.map fst |> should equal [ [ TagPattern ("sat", None) ]; [ TagPattern ("sun", None) ]; [ WildcardPattern ] ]
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``Or-patterns work in pattern lambdas and with several scrutinees`` () =
    match parseModule "def f = { | 0, _ | _, 0 -> \"axis\" | _, _ -> \"plane\" }" with
    | Result.Ok [ Def (ValueDef (_, "f", _, Lambda (args, Match (_, arms)))) ] ->
        args |> List.length |> should equal 2
        arms |> List.length |> should equal 3
        arms |> List.map (fst >> List.length) |> should equal [ 2; 2; 2 ]
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"
