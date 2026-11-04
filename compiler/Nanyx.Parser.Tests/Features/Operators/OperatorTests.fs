module ParserTests.Features.Operators

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers

[<Fact>]
let ``Parse binary-operators.nyx file`` () =
    let result = parseTestFile "binary-operators.nyx"

    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> List.length |> should be (greaterThan 10)
        match module'.[0] with
        | ModuleDecl name -> name |> should equal "BinaryOperators"
        | _ -> failwith "Expected ModuleDecl"

        let hasBinaryOp op module' =
            module' |> List.exists (fun item ->
                match item with
                | Def (ValueDef (_, _, _, BinaryOp (operator, _, _))) -> operator = op
                | _ -> false
            )

        module' |> hasBinaryOp "+" |> should equal true
        module' |> hasBinaryOp "-" |> should equal true
        module' |> hasBinaryOp "*" |> should equal true
        module' |> hasBinaryOp "/" |> should equal true
        module' |> hasBinaryOp "==" |> should equal true
        module' |> hasBinaryOp "!=" |> should equal true
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``++ is concatenation, looser than + and tighter than ==`` () =
    match parseModule "def x = a + b ++ c == d" with
    | Result.Ok [ Def (ValueDef (_, "x", _, BinaryOp ("==", BinaryOp ("++", BinaryOp ("+", _, _), _), _))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``++ associates to the right`` () =
    match parseModule "def x = a ++ b ++ c" with
    | Result.Ok [ Def (ValueDef (_, "x", _, BinaryOp ("++", IdentifierExpr ("a", _), BinaryOp ("++", _, _)))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``and binds tighter than or and looser than comparison`` () =
    match parseModule "def x = a == b and c or d" with
    | Result.Ok [ Def (ValueDef (_, "x", _, BinaryOp ("or", BinaryOp ("and", BinaryOp ("==", _, _), _), IdentifierExpr ("d", _)))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``not is a prefix keyword that binds tighter than infix operators`` () =
    match parseModule "def x = not a == b" with
    | Result.Ok [ Def (ValueDef (_, "x", _, BinaryOp ("==", FunctionCall ("not", _, [ IdentifierExpr ("a", _) ]), _))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``keyword operators do not swallow identifiers that start with them`` () =
    match parseModule "def x = order and android or nothing" with
    | Result.Ok [ Def (ValueDef (_, "x", _, BinaryOp ("or", BinaryOp ("and", IdentifierExpr ("order", _), IdentifierExpr ("android", _)), IdentifierExpr ("nothing", _)))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``&& and || are aliases for and and or`` () =
    match parseModule "def x = a && b || c" with
    | Result.Ok [ Def (ValueDef (_, "x", _, BinaryOp ("or", BinaryOp ("and", _, _), _))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``++ shorthand lambda is the two-argument concatenation`` () =
    match parseModule "def f = { ++ }" with
    | Result.Ok [ Def (ValueDef (_, "f", _, Lambda ([ ("x", None); ("y", None) ], BinaryOp ("++", _, _)))) ] -> ()
    | Result.Ok other -> failwith $"Unexpected parse: %A{other}"
    | Result.Error err -> failwith $"Parse failed: {err}"
