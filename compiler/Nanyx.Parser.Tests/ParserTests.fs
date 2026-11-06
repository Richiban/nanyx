module ParserTests.Core

open Xunit
open FsUnit.Xunit
open Parser.Program
open ParserTestHelpers

// Literal Tests
[<Fact>]
let ``Parse string literal`` () =
    let result = parseModule "def message = \"Hello world\""
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (LiteralExpr (StringLit value)))) ->
            name |> should equal "message"
            value |> should equal "Hello world"
        | _ -> failwith "Expected Def with StringLit"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse integer literal`` () =
    let result = parseModule "def count = 42"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (LiteralExpr (IntLit value)))) ->
            name |> should equal "count"
            value |> should equal 42
        | _ -> failwith "Expected Def with IntLit"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse float literal`` () =
    let result = parseModule "def pi = 3.14159"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (LiteralExpr (FloatLit value)))) ->
            name |> should equal "pi"
            value |> should equal 3.14159
        | _ -> failwith "Expected Def with FloatLit"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse boolean true`` () =
    let result = parseModule "def flag = true"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (LiteralExpr (BoolLit value)))) ->
            name |> should equal "flag"
            value |> should equal true
        | _ -> failwith "Expected Def with BoolLit"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse boolean false`` () =
    let result = parseModule "def flag = false"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (LiteralExpr (BoolLit value)))) ->
            name |> should equal "flag"
            value |> should equal false
        | _ -> failwith "Expected Def with BoolLit"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

// Module Tests
[<Fact>]
let ``Parse module declaration`` () =
    let result = parseModule "module Test"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | ModuleDecl name ->
            name |> should equal "Test"
        | _ -> failwith "Expected ModuleDecl"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse module with definition`` () =
    let input = "module Test\ndef message = \"Hello\""
    let result = parseModule input
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 2
        match module'.[0] with
        | ModuleDecl _ -> ()
        | _ -> failwith "Expected ModuleDecl"
        match module'.[1] with
        | Def _ -> ()
        | _ -> failwith "Expected Def"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse ws-sensitive mid-file module block`` () =
    let input =
        "module Root\n" +
        "module Inner =\n" +
        "  type Person = (firstName: string)\n" +
        "  def Person.toString = { p -> \"person\" }\n" +
        "  def value = 1"
    let result = parseModule input

    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 5
        match module'.[0] with
        | ModuleDecl "Root" -> ()
        | _ -> failwith "Expected ModuleDecl Root"
        match module'.[1] with
        | ModuleDecl "Root.Inner" -> ()
        | _ -> failwith "Expected ModuleDecl Root.Inner"
        match module'.[2] with
        | Def (TypeDef(name, _, _, _)) -> name |> should equal "Root.Inner.Person"
        | _ -> failwith "Expected qualified type def"
        match module'.[3] with
        | Def (ValueDef(_, name, _, _)) -> name |> should equal "Root.Inner.Person.toString"
        | _ -> failwith "Expected qualified attached def"
        match module'.[4] with
        | Def (ValueDef(_, name, _, _)) -> name |> should equal "Root.Inner.value"
        | _ -> failwith "Expected qualified value def"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse multiple definitions`` () =
    let input = "def x = 1\ndef y = 2\ndef z = 3"
    let result = parseModule input
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 3
        for item in module' do
            match item with
            | Def _ -> ()
            | _ -> failwith "Expected all Def"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse definition with newline before equals`` () =
    let input = "def x\n  = 1"
    let result = parseModule input

    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (LiteralExpr (IntLit value)))) ->
            name |> should equal "x"
            value |> should equal 1
        | _ -> failwith "Expected Def with IntLit"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse typed definition with newline before equals`` () =
    let input = "def f: string -> ()\n  = { s -> dbg(s) }"
    let result = parseModule input

    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, Some _, Kind (Lambda _))) ->
            name |> should equal "f"
        | _ -> failwith "Expected typed Def with Lambda"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse context-typed definition with newline before equals`` () =
    let input = "def f: [Print] string -> ()\n  = { s -> dbg(s) }"
    let result = parseModule input

    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, Some _, Kind (Lambda _))) ->
            name |> should equal "f"
        | _ -> failwith "Expected context-typed Def with Lambda"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

// Identifier Tests
[<Fact>]
let ``Parse identifier expression`` () =
    let result = parseModule "def x = someValue"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (IdentifierExpr value))) ->
            name |> should equal "x"
            value |> should equal "someValue"
        | _ -> failwith "Expected Def with IdentifierExpr"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Identifier can start with underscore`` () =
    let result = parseModule "def _private = 42"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, _)) ->
            name |> should equal "_private"
        | _ -> failwith "Expected Def"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Identifier can contain digits`` () =
    let result = parseModule "def value123 = 999"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, _)) ->
            name |> should equal "value123"
        | _ -> failwith "Expected Def"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

// Member Access Tests
[<Fact>]
let ``Parse simple member access`` () =
    let result = parseModule "def x = point.x"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (MemberAccess(Kind (IdentifierExpr objName), fieldName, _)))) ->
            name |> should equal "x"
            objName |> should equal "point"
            fieldName |> should equal "x"
        | _ -> failwith "Expected Def with MemberAccess"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse nested member access`` () =
    let result = parseModule "def x = outer.inner.value"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (MemberAccess(Kind (MemberAccess(Kind (IdentifierExpr outer), inner, _)), value, _)))) ->
            name |> should equal "x"
            outer |> should equal "outer"
            inner |> should equal "inner"
            value |> should equal "value"
        | _ -> failwith "Expected Def with nested MemberAccess"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse member access in binary operation`` () =
    let result = parseModule "def sum = point.x + point.y"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (BinaryOp("+", Kind (MemberAccess(Kind (IdentifierExpr p1), f1, _)), Kind (MemberAccess(Kind (IdentifierExpr p2), f2, _)))))) ->
            name |> should equal "sum"
            p1 |> should equal "point"
            f1 |> should equal "x"
            p2 |> should equal "point"
            f2 |> should equal "y"
        | _ -> failwith "Expected Def with BinaryOp containing MemberAccess"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

// Whitespace Tests
[<Fact>]
let ``Parse with leading whitespace`` () =
    let result = parseModule "   def x = 1"
    result |> isOk |> should equal true

[<Fact>]
let ``Parse with trailing whitespace`` () =
    let result = parseModule "def x = 1   "
    result |> isOk |> should equal true

[<Fact>]
let ``Parse with extra whitespace around equals`` () =
    let result = parseModule "def x    =    1"
    result |> isOk |> should equal true

[<Fact>]
let ``Parse empty input`` () =
    let result = parseModule ""
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should be Empty
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse only whitespace`` () =
    let result = parseModule "   \n  \n  "
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should be Empty
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

// Error Tests
[<Fact>]
let ``Error on missing equals`` () =
    let result = parseModule "def x 42"
    result |> isError |> should equal true

[<Fact>]
let ``Error on missing value`` () =
    let result = parseModule "def x ="
    result |> isError |> should equal true

[<Fact>]
let ``Error on missing identifier`` () =
    let result = parseModule "def = 42"
    result |> isError |> should equal true

[<Fact>]
let ``Error on unterminated string`` () =
    let result = parseModule "def x = \"unterminated"
    result |> isError |> should equal true

// Complex Tests
[<Fact>]
let ``Parse multiline module`` () =
    let input = """module MyModule

def x = 1
def y = 2
def message = "Hello"
def flag = true"""
    
    let result = parseModule input
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 5  // 1 module decl + 4 defs
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse with blank lines`` () =
    let input = """module Test

def x = 1

def y = 2"""
    
    let result = parseModule input
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 3
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse sample nyx file`` () =
    let input = """module Test

def message = "Hello world"
def x = 42
def pi = 3.14"""
    
    let result = parseModule input
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 4
        match module'.[0] with
        | ModuleDecl name -> name |> should equal "Test"
        | _ -> failwith "Expected ModuleDecl"
    | Result.Error err ->
        failwith $"Parse failed: {err}"

// Function Call Tests
[<Fact>]
let ``Parse function call with no arguments`` () =
    let result = parseModule "def x = foo()"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (FunctionCall (funcName, _, args)))) ->
            name |> should equal "x"
            funcName |> should equal "foo"
            args |> should haveLength 0
        | _ -> failwith "Expected Def with FunctionCall"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse function call with one argument`` () =
    let result = parseModule "def x = println(\"Hello\")"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (FunctionCall (funcName, _, args)))) ->
            name |> should equal "x"
            funcName |> should equal "println"
            args |> should haveLength 1
            match args.[0].Kind with
            | LiteralExpr (StringLit value) -> value |> should equal "Hello"
            | _ -> failwith "Expected string literal argument"
        | _ -> failwith "Expected Def with FunctionCall"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse function call with multiple arguments`` () =
    let result = parseModule "def result = add(1, 2)"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (FunctionCall (funcName, _, args)))) ->
            name |> should equal "result"
            funcName |> should equal "add"
            // With comma operator, f(x, y) creates a single tuple argument
            args |> should haveLength 1
            match args.[0].Kind with
            | TupleExpr [Kind (LiteralExpr (IntLit v1)); Kind (LiteralExpr (IntLit v2))] -> 
                v1 |> should equal 1
                v2 |> should equal 2
            | _ -> failwith "Expected tuple of int literal arguments"
        | _ -> failwith "Expected Def with FunctionCall"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse function call with identifier argument`` () =
    let result = parseModule "def y = foo(x)"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (FunctionCall (funcName, _, args)))) ->
            name |> should equal "y"
            funcName |> should equal "foo"
            args |> should haveLength 1
            match args.[0].Kind with
            | IdentifierExpr id -> id |> should equal "x"
            | _ -> failwith "Expected identifier argument"
        | _ -> failwith "Expected Def with FunctionCall"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse nested function calls`` () =
    let result = parseModule "def x = outer(inner(42))"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (FunctionCall (funcName, _, args)))) ->
            name |> should equal "x"
            funcName |> should equal "outer"
            args |> should haveLength 1
            match args.[0].Kind with
            | FunctionCall (innerName, _, innerArgs) ->
                innerName |> should equal "inner"
                innerArgs |> should haveLength 1
                match innerArgs.[0].Kind with
                | LiteralExpr (IntLit v) -> v |> should equal 42
                | _ -> failwith "Expected int literal in inner call"
            | _ -> failwith "Expected nested function call"
        | _ -> failwith "Expected Def with FunctionCall"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

// Lambda Expression Tests
[<Fact>]
let ``Parse lambda with no parameters`` () =
    let result = parseModule "def f = { 42 }"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (Lambda (parameters, body)))) ->
            name |> should equal "f"
            parameters |> should haveLength 0
            match body.Kind with
            | LiteralExpr (IntLit v) -> v |> should equal 42
            | _ -> failwith "Expected int literal in lambda body"
        | _ -> failwith "Expected Def with Lambda"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse lambda with one parameter`` () =
    let result = parseModule "def f = { x -> x }"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (Lambda (parameters, body)))) ->
            name |> should equal "f"
            parameters |> should haveLength 1
            parameters.[0] |> fst |> should equal "x"
            match body.Kind with
            | IdentifierExpr id -> id |> should equal "x"
            | _ -> failwith "Expected identifier in lambda body"
        | _ -> failwith "Expected Def with Lambda"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse lambda with multiple parameters`` () =
    let result = parseModule "def add = { x, y -> 42 }"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (Lambda (parameters, body)))) ->
            name |> should equal "add"
            parameters |> should haveLength 2
            parameters.[0] |> fst |> should equal "x"
            parameters.[1] |> fst |> should equal "y"
        | _ -> failwith "Expected Def with Lambda"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse lambda with function call in body`` () =
    let result = parseModule "def f = { x -> println(x) }"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (Lambda (parameters, body)))) ->
            name |> should equal "f"
            parameters |> should haveLength 1
            parameters.[0] |> fst |> should equal "x"
            match body.Kind with
            | FunctionCall (funcName, _, args) ->
                funcName |> should equal "println"
                args |> should haveLength 1
            | _ -> failwith "Expected function call in lambda body"
        | _ -> failwith "Expected Def with Lambda"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse lambda as function argument`` () =
    let result = parseModule "def x = map({ x -> x }, items)"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (FunctionCall (funcName, _, args)))) ->
            name |> should equal "x"
            funcName |> should equal "map"
            // With comma operator, f(x, y) creates a single tuple argument
            args |> should haveLength 1
            match args.[0].Kind with
            | TupleExpr [Kind (Lambda (parameters, _)); Kind (IdentifierExpr "items")] ->
                parameters |> should haveLength 1
                parameters.[0] |> fst |> should equal "x"
            | _ -> failwith "Expected tuple with lambda and identifier"
        | _ -> failwith "Expected Def with FunctionCall"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

// Shorthand Lambda Tests
[<Fact>]
let ``Parse shorthand lambda with binary operator`` () =
    let result = parseModule "def multiply = { * }"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (Lambda (parameters, body)))) ->
            name |> should equal "multiply"
            parameters |> should haveLength 2
            parameters.[0] |> fst |> should equal "x"
            parameters.[1] |> fst |> should equal "y"
            match body.Kind with
            | BinaryOp (op, Kind (IdentifierExpr left), Kind (IdentifierExpr right)) ->
                op |> should equal "*"
                left |> should equal "x"
                right |> should equal "y"
            | _ -> failwith "Expected binary operation in shorthand lambda body"
        | _ -> failwith "Expected Def with Lambda"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse shorthand lambda with unary operation`` () =
    let result = parseModule "def double = { * 2 }"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (Lambda (parameters, body)))) ->
            name |> should equal "double"
            parameters |> should haveLength 1
            parameters.[0] |> fst |> should equal "x"
            match body.Kind with
            | BinaryOp (op, Kind (IdentifierExpr left), Kind (LiteralExpr (IntLit right))) ->
                op |> should equal "*"
                left |> should equal "x"
                right |> should equal 2
            | _ -> failwith "Expected binary operation with literal in shorthand lambda body"
        | _ -> failwith "Expected Def with Lambda"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse shorthand lambda with property access`` () =
    let result = parseModule "def getName = { .name }"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (Lambda (parameters, body)))) ->
            name |> should equal "getName"
            parameters |> should haveLength 1
            parameters.[0] |> fst |> should equal "x"
            match body.Kind with
            | MemberAccess (Kind (IdentifierExpr obj), field, _) ->
                obj |> should equal "x"
                field |> should equal "name"
            | _ -> failwith "Expected member access in shorthand property lambda body"
        | _ -> failwith "Expected Def with Lambda"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse shorthand lambda with addition`` () =
    let result = parseModule "def addFive = { + 5 }"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (Lambda (parameters, body)))) ->
            name |> should equal "addFive"
            parameters |> should haveLength 1
            parameters.[0] |> fst |> should equal "x"
            match body.Kind with
            | BinaryOp (op, Kind (IdentifierExpr left), Kind (LiteralExpr (IntLit right))) ->
                op |> should equal "+"
                left |> should equal "x"
                right |> should equal 5
            | _ -> failwith "Expected addition operation in shorthand lambda body"
        | _ -> failwith "Expected Def with Lambda"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse shorthand lambda as function argument`` () =
    let result = parseModule "def doubled = map({ * 2 }, numbers)"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (FunctionCall (funcName, _, args)))) ->
            name |> should equal "doubled"
            funcName |> should equal "map"
            // With comma operator, f(x, y) creates a single tuple argument
            args |> should haveLength 1
            match args.[0].Kind with
            | TupleExpr [Kind (Lambda (parameters, body)); Kind (IdentifierExpr "numbers")] ->
                parameters |> should haveLength 1
                parameters.[0] |> fst |> should equal "x"
                match body.Kind with
                | BinaryOp (op, Kind (IdentifierExpr left), Kind (LiteralExpr (IntLit right))) ->
                    op |> should equal "*"
                    left |> should equal "x"
                    right |> should equal 2
                | _ -> failwith "Expected multiplication in lambda"
            | _ -> failwith "Expected shorthand lambda as first argument"
        | _ -> failwith "Expected Def with FunctionCall"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

// Pipe Operator Tests
[<Fact>]
let ``Parse simple pipe`` () =
    let result = parseModule "def result = x \\f"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (Pipe (expr, funcName, _, args)))) ->
            name |> should equal "result"
            match expr.Kind with
            | IdentifierExpr id -> id |> should equal "x"
            | _ -> failwith "Expected identifier in pipe left side"
            funcName |> should equal "f"
            args |> should haveLength 0
        | _ -> failwith "Expected Def with Pipe"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse pipe with arguments`` () =
    let result = parseModule "def result = x \\f(y, z)"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (Pipe (expr, funcName, _, args)))) ->
            name |> should equal "result"
            match expr.Kind with
            | IdentifierExpr id -> id |> should equal "x"
            | _ -> failwith "Expected identifier"
            funcName |> should equal "f"
            // With comma operator, \f(x, y) creates a single tuple argument
            args |> should haveLength 1
            match args.[0].Kind with
            | TupleExpr [Kind (IdentifierExpr y); Kind (IdentifierExpr z)] ->
                y |> should equal "y"
                z |> should equal "z"
            | _ -> failwith "Expected tuple of identifiers as arguments"
        | _ -> failwith "Expected Def with Pipe"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse chained pipes`` () =
    let result = parseModule "def result = x \\f \\g \\h"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (Pipe (Kind (Pipe (Kind (Pipe (expr, f, _, _)), g, _, _)), h, _, _)))) ->
            name |> should equal "result"
            match expr.Kind with
            | IdentifierExpr id -> id |> should equal "x"
            | _ -> failwith "Expected identifier"
            f |> should equal "f"
            g |> should equal "g"
            h |> should equal "h"
        | _ -> failwith "Expected nested Pipe"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse pipe with literal`` () =
    let result = parseModule "def result = 5 \\double"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (Pipe (expr, funcName, _, args)))) ->
            name |> should equal "result"
            match expr.Kind with
            | LiteralExpr (IntLit n) -> n |> should equal 5
            | _ -> failwith "Expected int literal"
            funcName |> should equal "double"
            args |> should haveLength 0
        | _ -> failwith "Expected Def with Pipe"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse pipe with lambda argument`` () =
    let result = parseModule "def result = items \\map({ x -> x * 2 })"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (Pipe (expr, funcName, _, args)))) ->
            name |> should equal "result"
            match expr.Kind with
            | IdentifierExpr id -> id |> should equal "items"
            | _ -> failwith "Expected identifier"
            funcName |> should equal "map"
            args |> should haveLength 1
            match args.[0].Kind with
            | Lambda (parameters, _) ->
                parameters |> should haveLength 1
                parameters.[0] |> fst |> should equal "x"
            | _ -> failwith "Expected lambda argument"
        | _ -> failwith "Expected Def with Pipe"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

// Binary Operator Tests
[<Fact>]
let ``Parse addition`` () =
    let result = parseModule "def sum = 1 + 2"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (BinaryOp (op, left, right)))) ->
            name |> should equal "sum"
            op |> should equal "+"
            match left.Kind, right.Kind with
            | LiteralExpr (IntLit v1), LiteralExpr (IntLit v2) ->
                v1 |> should equal 1
                v2 |> should equal 2
            | _ -> failwith "Expected int literals"
        | _ -> failwith "Expected Def with BinaryOp"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse multiplication`` () =
    let result = parseModule "def product = 3 * 4"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        match module'.[0] with
        | Def (ValueDef (_, _, _, Kind (BinaryOp (op, _, _)))) ->
            op |> should equal "*"
        | _ -> failwith "Expected Def with BinaryOp"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse operator precedence - multiplication before addition`` () =
    let result = parseModule "def x = 1 + 2 * 3"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        match module'.[0] with
        | Def (ValueDef (_, _, _, Kind (BinaryOp ("+", Kind (LiteralExpr (IntLit 1)), Kind (BinaryOp ("*", Kind (LiteralExpr (IntLit 2)), Kind (LiteralExpr (IntLit 3)))))))) ->
            () // Correct precedence: 1 + (2 * 3)
        | _ -> failwith "Expected correct precedence: 1 + (2 * 3)"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse parenthesized expression`` () =
    let result = parseModule "def x = (1 + 2) * 3"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        match module'.[0] with
        | Def (ValueDef (_, _, _, Kind (BinaryOp ("*", Kind (BinaryOp ("+", Kind (LiteralExpr (IntLit 1)), Kind (LiteralExpr (IntLit 2)))), Kind (LiteralExpr (IntLit 3)))))) ->
            () // Correct: (1 + 2) * 3
        | _ -> failwith "Expected correct precedence: (1 + 2) * 3"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse comparison operators`` () =
    let result = parseModule "def x = 5 > 3"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        match module'.[0] with
        | Def (ValueDef (_, _, _, Kind (BinaryOp (">", Kind (LiteralExpr (IntLit 5)), Kind (LiteralExpr (IntLit 3)))))) ->
            ()
        | _ -> failwith "Expected comparison operator"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

[<Fact>]
let ``Parse equality operators`` () =
    let result = parseModule "def x = a == b"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        match module'.[0] with
        | Def (ValueDef (_, _, _, Kind (BinaryOp ("==", Kind (IdentifierExpr "a"), Kind (IdentifierExpr "b"))))) ->
            ()
        | _ -> failwith "Expected equality operator"
    | Result.Error err -> failwith $"Parse should succeed, got: {err}"

// File-based Integration Tests
[<Fact>]
let ``Parse literals.nyx file`` () =
    let result = parseTestFile "literals.nyx"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 6  // 1 module + 5 defs
        match module'.[0] with
        | ModuleDecl name -> name |> should equal "Literals"
        | _ -> failwith "Expected ModuleDecl"
        
        // Check that we have string, int, float, and bool literals
        let defs = module' |> List.tail
        defs |> should haveLength 5
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``Parse function-calls.nyx file`` () =
    let result = parseTestFile "function-calls.nyx"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 7  // 1 module + 6 function call defs
        match module'.[0] with
        | ModuleDecl name -> name |> should equal "FunctionCalls"
        | _ -> failwith "Expected ModuleDecl"
        
        // Verify all are function calls
        let defs = module' |> List.tail
        for def in defs do
            match def with
            | Def (ValueDef (_, _, _, Kind (FunctionCall _))) -> ()
            | _ -> failwith "Expected all definitions to contain function calls"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``Parse lambdas.nyx file`` () =
    let result = parseTestFile "lambdas.nyx"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 7  // 1 module + 6 lambda defs
        match module'.[0] with
        | ModuleDecl name -> name |> should equal "Lambdas"
        | _ -> failwith "Expected ModuleDecl"
        
        // Verify all are lambdas
        let defs = module' |> List.tail
        for def in defs do
            match def with
            | Def (ValueDef (_, _, _, Kind (Lambda _))) -> ()
            | _ -> failwith "Expected all definitions to contain lambdas"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``Parse shorthand-lambdas.nyx file`` () =
    let result = parseTestFile "shorthand-lambdas.nyx"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 15  // 1 module + 14 shorthand lambda defs
        match module'.[0] with
        | ModuleDecl name -> name |> should equal "ShorthandLambdas"
        | _ -> failwith "Expected ModuleDecl"
        
        // Verify all are lambdas (some within function calls)
        let defs = module' |> List.tail
        for def in defs do
            match def with
            | Def (ValueDef (_, _, _, Kind (Lambda _))) -> ()
            | Def (ValueDef (_, _, _, Kind (FunctionCall (_, _, args)))) ->
                // With comma operator, args may be wrapped in TupleExpr
                let rec hasLambda arg = 
                    match arg.Kind with
                    | Lambda _ -> true 
                    | TupleExpr exprs -> exprs |> List.exists hasLambda
                    | _ -> false
                let foundLambda = args |> List.exists hasLambda
                foundLambda |> should equal true
            | _ -> failwith "Expected lambda or function call with lambda"
    | Result.Error err -> failwith $"Parse failed: {err}"

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
        
        // Check for specific operators
        let hasBinaryOp op module' =
            module' |> List.exists (fun item ->
                match item with
                | Def (ValueDef (_, _, _, Kind (BinaryOp (operator, _, _)))) -> operator = op
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
let ``Parse piping.nyx file`` () =
    let result = parseTestFile "piping.nyx"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> List.length |> should be (greaterThan 5)
        match module'.[0] with
        | ModuleDecl name -> name |> should equal "Piping"
        | _ -> failwith "Expected ModuleDecl"
        
        // Verify we have some pipe expressions
        let hasPipe = module' |> List.exists (fun item ->
            match item with
            | Def (ValueDef (_, _, _, Kind (Pipe _))) -> true
            | _ -> false
        )
        hasPipe |> should equal true
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``Parse block with two literals`` () =
    let result = parseTestFile "test_block_two_literals.nyx"

    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> should haveLength 1
        match module'.[0] with
        | Def (ValueDef (_, name, _, Kind (Block statements))) ->
            name |> should equal "test"
            statements |> should haveLength 2
            match statements.[0] with
            | Kind (ExprStatement (Kind (LiteralExpr (IntLit 1)))) -> ()
            | _ -> failwith "Expected first statement to be literal 1"
            match statements.[1] with
            | Kind (ExprStatement (Kind (LiteralExpr (IntLit 2)))) -> ()
            | _ -> failwith "Expected second statement to be literal 2"
        | _ -> failwith "Expected a block with two literal statements"
    | Result.Error err -> failwith $"Parse failed: {err}"

[<Fact>]
let ``Parse comprehensive.nyx file`` () =
    let result = parseTestFile "comprehensive.nyx"
    
    result |> isOk |> should equal true
    match result with
    | Result.Ok module' ->
        module' |> List.length |> should be (greaterThan 5)
        match module'.[0] with
        | ModuleDecl name -> name |> should equal "Comprehensive"
        | _ -> failwith "Expected ModuleDecl"
        
        // Should have a mix of literals, function calls, lambdas, and binary ops
        let hasLiteral = module' |> List.exists (fun item ->
            match item with
            | Def (ValueDef (_, _, _, Kind (LiteralExpr _))) -> true
            | _ -> false
        )
        
        let hasFunctionCall = module' |> List.exists (fun item ->
            match item with
            | Def (ValueDef (_, _, _, Kind (FunctionCall _))) -> true
            | _ -> false
        )
        
        let hasLambda = module' |> List.exists (fun item ->
            match item with
            | Def (ValueDef (_, _, _, Kind (Lambda _))) -> true
            | _ -> false
        )
        
        hasLiteral |> should equal true
        hasFunctionCall |> should equal true
        hasLambda |> should equal true
    | Result.Error err -> failwith $"Parse failed: {err}"
