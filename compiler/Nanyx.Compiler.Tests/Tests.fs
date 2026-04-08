module Nanyx.Compiler.Tests

open System
open System.IO
open Xunit
open NyxCompiler
open Parser.Program

let private withTempDir (action: string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    try
        action dir
    finally
        if Directory.Exists dir then
            Directory.Delete(dir, true)

[<Fact>]
let ``Compile returns typed module for valid source`` () =
    let source = "def message = \"Hello\""
    let result = Compiler.compile source
    Assert.Equal(CompilationPhase.Typed, result.Phase)
    Assert.True(result.Diagnostics.IsEmpty)
    Assert.True(result.Typed.IsSome)

[<Fact>]
let ``Typecheck infers literal types`` () =
    let source = "def message = \"Hello\""
    let result = Compiler.compile source
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "string", typed.Types.["message"])

[<Fact>]
let ``Typecheck interpolated string`` () =
    let source =
        "def name = \"Ada\"\n" +
        "def msg = \"Hello {name}\""
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "string", typed.Types.["msg"])

[<Fact>]
let ``Import brings members into scope unqualified`` () =
    withTempDir (fun dir ->
        let modulePath = Path.Combine(dir, "mod.nyx")
        let mainPath = Path.Combine(dir, "main.nyx")
        File.WriteAllText(modulePath, "module mod\nexport def foo = 1")
        File.WriteAllText(mainPath, "module main\nimport \"mod.nyx\"\ndef value = foo")
        let result = Compiler.compileFile mainPath
        Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
        let typed = result.Typed.Value
        Assert.Equal(TyPrimitive "int", typed.Types.["value"]))

[<Fact>]
let ``Import with alias is qualified`` () =
    withTempDir (fun dir ->
        let modulePath = Path.Combine(dir, "mod.nyx")
        let mainPath = Path.Combine(dir, "main.nyx")
        File.WriteAllText(modulePath, "module mod\nexport def foo = 1")
        File.WriteAllText(mainPath, "module main\nimport \"mod.nyx\" as m1\ndef value = m1.foo")
        let result = Compiler.compileFile mainPath
        Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
        let typed = result.Typed.Value
        Assert.Equal(TyPrimitive "int", typed.Types.["value"]))

[<Fact>]
let ``Import with alias does not bring unqualified names`` () =
    withTempDir (fun dir ->
        let modulePath = Path.Combine(dir, "mod.nyx")
        let mainPath = Path.Combine(dir, "main.nyx")
        File.WriteAllText(modulePath, "module mod\nexport def foo = 1")
        File.WriteAllText(mainPath, "module main\nimport \"mod.nyx\" as m1\ndef value = foo")
        let result = Compiler.compileFile mainPath
        Assert.False(result.Diagnostics.IsEmpty)
        let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
        Assert.Contains("Unknown identifier", message))

[<Fact>]
let ``Match rejects non-exhaustive tag unions`` () =
    let source =
        "def value: #some(int) | #none = #none\n" +
        "def result = match value\n" +
        "| #some(v) -> v\n"
    let result = Compiler.compile source
    Assert.False(result.Diagnostics.IsEmpty)
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("Non-exhaustive match", message)

[<Fact>]
let ``Match accepts exhaustive tag unions`` () =
    let source =
        "def value: #some(int) | #none = #none\n" +
        "def result = match value\n" +
        "| #some(v) -> v\n" +
        "| #none -> 0\n"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)

[<Fact>]
let ``Match rejects non-exhaustive list patterns`` () =
    let source =
        "def values = [1, 2]\n" +
        "def result = match values\n" +
        "| [x, y] -> x\n"
    let result = Compiler.compile source
    Assert.False(result.Diagnostics.IsEmpty)
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("Non-exhaustive match", message)

[<Fact>]
let ``Match accepts list catch-all`` () =
    let source =
        "def values = [1, 2]\n" +
        "def result = match values\n" +
        "| [x, y] -> x\n" +
        "| _ -> 0\n"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)

[<Fact>]
let ``Match accepts exhaustive list patterns`` () =
    let source =
        "def values = [1, 2]\n" +
        "def result = match values\n" +
        "| [] -> 0\n" +
        "| [head, ...tail] -> head\n"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)

[<Fact>]
let ``Typecheck list type application`` () =
    let source =
        "def sum: list(int) -> int = { xs -> 0 }\n" +
        "def values: list(string, int) = []"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")

[<Fact>]
let ``Typecheck dbg returns its argument`` () =
    let source =
        "def value = dbg(1)\n" +
        "def text = dbg(\"hi\")"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "int", typed.Types.["value"])
    Assert.Equal(TyPrimitive "string", typed.Types.["text"])

[<Fact>]
let ``Typecheck self recursive function`` () =
    let source =
        "def fact = { n -> if n == 0 then 1 else n * fact(n - 1) }\n" +
        "def result = fact(3)"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "int", typed.Types.["result"])

[<Fact>]
let ``Match rejects non-exhaustive record patterns`` () =
    let source =
        "def point = (x = 1, y = 2)\n" +
        "def result = match point\n" +
        "| (x = 1) -> 1\n"
    let result = Compiler.compile source
    Assert.False(result.Diagnostics.IsEmpty)
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("Non-exhaustive match", message)

[<Fact>]
let ``Match accepts record catch-all`` () =
    let source =
        "def point = (x = 1, y = 2)\n" +
        "def result = match point\n" +
        "| (x = 1) -> 1\n" +
        "| _ -> 0\n"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)

[<Fact>]
let ``Match rejects incompatible record patterns`` () =
    let source =
        "def point = (x = 1)\n" +
        "def result = match point\n" +
        "| (x = 1) -> 1\n" +
        "| \"oops\" -> 0\n" +
        "| _ -> 0\n"
    let result = Compiler.compile source
    Assert.False(result.Diagnostics.IsEmpty)
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("Type mismatch", message)

[<Fact>]
let ``Match rejects non-exhaustive tag unions with multiple scrutinees`` () =
    let source =
        "def a: #left | #right = #left\n" +
        "def b: #up | #down = #up\n" +
        "def result = match a, b\n" +
        "| #left, #up -> 1\n" +
        "| #right, #up -> 2\n"
    let result = Compiler.compile source
    Assert.False(result.Diagnostics.IsEmpty)
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("Non-exhaustive match", message)

[<Fact>]
let ``Typecheck dotted def name`` () =
    let source =
        "type Person = (firstName: string)\n" +
        "def Person.fullName: Person -> string = { p -> p.firstName }"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    match typed.Types.["Person.fullName"] with
    | TyFunc(TyRecord fields, TyPrimitive "string") when fields.ContainsKey("firstName") ->
        Assert.True(true)
    | other -> Assert.True(false, $"Unexpected dotted def type: %A{other}")

[<Fact>]
let ``Typecheck ws-sensitive module block`` () =
    let source =
        "module Root\n" +
        "module Inner =\n" +
        "  def x = 1\n" +
        "  def y = x"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "int", typed.Types.["Root.Inner.x"])
    Assert.Equal(TyPrimitive "int", typed.Types.["Root.Inner.y"])

[<Fact>]
let ``Typecheck infers attached receiver type for unannotated lambda`` () =
    let source =
        "type Person = (firstName: string)\n" +
        "def Person.toString = { p -> \"person\" }"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    let typed = result.Typed.Value
    match typed.Types.["Person.toString"] with
    | TyFunc(TyRecord fields, TyPrimitive "string") when fields.ContainsKey("firstName") ->
        Assert.True(true)
    | other -> Assert.True(false, $"Unexpected inferred attached def type: %A{other}")

[<Fact>]
let ``Typecheck infers nominal attached receiver type for unannotated lambda`` () =
    let source =
        "type $Email = string\n" +
        "def $Email.toString = { e -> \"ok\" }"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    let typed = result.Typed.Value
    match typed.Types.["$Email.toString"] with
    | TyFunc(TyNominal(name, _, _), TyPrimitive "string") when name = "$Email" ->
        Assert.True(true)
    | other -> Assert.True(false, $"Unexpected inferred nominal attached def type: %A{other}")

[<Fact>]
let ``Typecheck infers nominal attached receiver as argument in body`` () =
    let source =
        "type $Email = string\n" +
        "def $Email.toDisplay = { e -> e }"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    let typed = result.Typed.Value
    match typed.Types.["$Email.toDisplay"] with
    | TyFunc(TyNominal(nameIn, _, _), TyNominal(nameOut, _, _)) when nameIn = "$Email" && nameOut = "$Email" ->
        Assert.True(true)
    | other -> Assert.True(false, $"Unexpected inferred nominal identity attached def type: %A{other}")

[<Fact>]
let ``Typecheck rejects attached def with missing type`` () =
    let source = "def A.a = { s -> s }"
    let result = Compiler.compile source
    Assert.False(result.Diagnostics.IsEmpty)
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("Unknown attached type", message)

[<Fact>]
let ``Typecheck pipe resolves attached functions`` () =
    let source =
        "type Person = (firstName: string surname: string)\n" +
        "def Person.fullName: Person -> string = { p -> p.firstName }\n" +
        "def p: Person = (firstName = \"\", surname = \"\")\n" +
        "def n = p \\ Person.fullName\n" +
        "def n2 = p \\ fullName"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "string", typed.Types.["n"])
    Assert.Equal(TyPrimitive "string", typed.Types.["n2"])

[<Fact>]
let ``Typecheck pipe resolves attached functions on nominals`` () =
    let source =
        "type $Email = string\n" +
        "def $Email.toString: $Email -> string = { e -> \"\" }\n" +
        "def $Email.format: $Email -> string = { e -> \"\" }\n" +
        "def email: $Email = \"a@b.com\"\n" +
        "def s = email \\ toString\n" +
        "def s2 = email \\ format"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "string", typed.Types.["s"])
    Assert.Equal(TyPrimitive "string", typed.Types.["s2"])

[<Fact>]
let ``Typecheck supports polymorphic let`` () =
    let source =
        "def id = { x -> x }\n" +
        "def a = id(1)\n" +
        "def b = id(\"hi\")"
    let result = Compiler.compile source
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "int", typed.Types.["a"])
    Assert.Equal(TyPrimitive "string", typed.Types.["b"])

[<Fact>]
let ``Typecheck expands generic type aliases in annotations`` () =
    let source =
        "type Async(a) = #done(a) | #pending(a)\n" +
        "def fromResult: a -> Async(a) = { r -> #done(r) }\n" +
        "def value = fromResult(5)"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")

[<Fact>]
let ``Typecheck uses tuple input for multi-arg functions`` () =
    let source =
        "def f = { x, y -> x }\n" +
        "def result = f(1, 2)"
    let result = Compiler.compile source
    let typed = result.Typed.Value
    match typed.Types.["f"] with
    | TyFunc(TyRecord fields, TyVar _) when fields.ContainsKey("item1") && fields.ContainsKey("item2") ->
        Assert.True(true)
    | other -> Assert.True(false, $"Unexpected function type: %A{other}")
    Assert.Equal(TyPrimitive "int", typed.Types.["result"])

[<Fact>]
let ``Typecheck uses unit input for zero-arg functions`` () =
    let source =
        "def f = { 1 }\n" +
        "def result = f()"
    let result = Compiler.compile source
    let typed = result.Typed.Value
    Assert.Equal(TyFunc(TyPrimitive "unit", TyPrimitive "int"), typed.Types.["f"])
    Assert.Equal(TyPrimitive "int", typed.Types.["result"])

[<Fact>]
let ``Typecheck allows context members with annotation`` () =
    let source =
        "context Console = (println: string -> ())\n" +
        "def sayHello: [Console] () -> () = { println(\"hi\") }"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)

[<Fact>]
let ``Typecheck rejects missing context members`` () =
    let source =
        "context Console = (println: string -> ())\n" +
        "def sayHello = { println(\"hi\") }"
    let result = Compiler.compile source
    Assert.False(result.Diagnostics.IsEmpty)
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("Unknown", message)

[<Fact>]
let ``Typecheck allows use statement to bring members into scope`` () =
    let source =
        "context Console = (println: string -> string)\n" +
        "def main = {\n" +
        "  use Console(println = { msg -> msg })\n" +
        "  println(\"hi\")\n" +
        "}"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)

[<Fact>]
let ``Typecheck allows use-in expression`` () =
    let source =
        "type Ctx = (value: int)\n" +
        "def result = use Ctx(value = 1) in value"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "int", typed.Types.["result"])

[<Fact>]
let ``Typecheck supports unit literal`` () =
    let source = "def f = { () }"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    Assert.Equal(TyFunc(TyPrimitive "unit", TyPrimitive "unit"), typed.Types.["f"])

[<Fact>]
let ``Typecheck rejects missing context on call`` () =
    let source =
        "context Console = (println: string -> ())\n" +
        "def g: [Console] () -> () = { println(\"hi\") }\n" +
        "def h = { g() }"
    let result = Compiler.compile source
    Assert.False(result.Diagnostics.IsEmpty)
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("Missing context", message)

[<Fact>]
let ``Typecheck supports record constructor syntax`` () =
    let source =
        "type Person = (firstName: string surname: string)\n" +
        "def p = Person(firstName = \"Joe\", surname = \"Bloggs\")"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    match typed.Types.["p"] with
    | TyRecord fields ->
        Assert.Equal(TyPrimitive "string", fields.["firstName"])
        Assert.Equal(TyPrimitive "string", fields.["surname"])
    | other -> Assert.True(false, $"Unexpected constructor type: %A{other}")

[<Fact>]
let ``Typecheck supports mixed positional and named records`` () =
    let source = "def value = (1, 2, z = 3)"
    let result = Compiler.compile source
    let typed = result.Typed.Value
    match typed.Types.["value"] with
    | TyRecord fields when fields.ContainsKey("item1") && fields.ContainsKey("item2") && fields.ContainsKey("z") ->
        Assert.Equal(TyPrimitive "int", fields.["item1"])
        Assert.Equal(TyPrimitive "int", fields.["item2"])
        Assert.Equal(TyPrimitive "int", fields.["z"])
    | other -> Assert.True(false, $"Unexpected mixed record type: %A{other}")

[<Fact>]
let ``Typecheck allows tuple args variable for function call`` () =
    let source =
        "def f = { x, y -> \"\" }\n" +
        "def args = 4, 5\n" +
        "def result = f(args)"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "string", typed.Types.["result"])

[<Fact>]
let ``Typecheck reports mismatch for missing tuple args`` () =
    let source =
        "def f = { x, y -> \"\" }\n" +
        "def result = f(4)"
    let result = Compiler.compile source
    Assert.False(result.Diagnostics.IsEmpty)
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("Type mismatch", message)
    Assert.Contains("(", message)
    Assert.Contains("vs int", message)

[<Fact>]
let ``Typecheck allows record width subtyping`` () =
    let source =
        "def f: (a: int) -> int = { r -> 1 }\n" +
        "def args = (a = 1, b = 2)\n" +
        "def result = f(args)"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "int", typed.Types.["result"])

[<Fact>]
let ``Typecheck treats union order as assignable`` () =
    let source =
        "def f: (#some(int) | #notFound) -> int = { x -> 1 }\n" +
        "def g: (#notFound | #some(int)) -> int = f\n" +
        "def result = g(#some(1))"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "int", typed.Types.["result"])

[<Fact>]
let ``Typecheck allows wider unions in assignment`` () =
    let source =
        "def f: (#some(int) | #notFound) -> int = { x -> 1 }\n" +
        "def g: (#some(int) | #notFound | #error(string)) -> int = f\n" +
        "def result = g(#notFound)"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "int", typed.Types.["result"])

[<Fact>]
let ``Typecheck supports tag union rest parameter`` () =
    let source =
        "def mapOption: ((#some(a) | r), a -> b) -> #some(b) | r = { o, f ->\n" +
        "  match o\n" +
        "  | #some(v) -> #some(f(v))\n" +
        "  | other -> other\n" +
        "}\n" +
        "def result = mapOption(#some(1), { x -> x })"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    match typed.Types.["result"] with
    | TyUnion [ TyTag("some", Some (TyPrimitive "int")); TyVar _ ] -> Assert.True(true)
    | other -> Assert.True(false, $"Unexpected rest union result type: %A{other}")

[<Fact>]
let ``Typecheck requires catch-all for tag union rest`` () =
    let source =
        "def mapOption: ((#some(a) | r), a -> b) -> #some(b) | r = { o, f ->\n" +
        "  match o\n" +
        "  | #some(v) -> #some(f(v))\n" +
        "}\n"
    let result = Compiler.compile source
    Assert.False(result.Diagnostics.IsEmpty)
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("catch-all", message)

[<Fact>]
let ``Typecheck tag union width and rest regression`` () =
    let source =
        "def u: #some(string) | #nil = #nil\n" +
        "def g: (#some(string) | #nil | #notFound) -> string = { o -> \"\" }\n" +
        "def _ = g(u)\n" +
        "def mapOption: ((#some(a) | r), a -> b) -> #some(b) | r = { o, f ->\n" +
        "  match o\n" +
        "  | #some(v) -> #some(f(v))\n" +
        "  | other -> other\n" +
        "}\n" +
        "def l: #some(string) | #nil = #some(\"\")\n" +
        "def mapper: string -> int = { s -> 1 }\n" +
        "def l2 = l \\mapOption(mapper)"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    match typed.Types.["l2"] with
    | TyUnion [ TyTag("some", Some (TyPrimitive "int")); TyUnion [ TyTag("nil", None) ] ] -> Assert.True(true)
    | other -> Assert.True(false, $"Unexpected l2 type: %A{other}")

[<Fact>]
let ``Desugar workflow bind and return`` () =
    let source =
        "def result = async {\n" +
        "  def x = await! foo()\n" +
        "  return! x.y\n" +
        "}"
    match parseModule source with
    | Result.Error err -> Assert.True(false, $"Parse error: {err}")
    | Result.Ok ast ->
        let desugared = Compiler.desugar ast
        match desugared with
        | [Def (ValueDef(_, "result", _, FunctionCall("async", _, [Lambda([], Block [ExprStatement expr])])))] ->
            match expr with
            | FunctionCall(
                "await",
                _,
                [
                    FunctionCall("foo", _, [])
                    Lambda([("x", None)], FunctionCall("pure", _, [MemberAccess(IdentifierExpr("x", _), "y", _)]))
                ]
              ) ->
                Assert.True(true)
            | other -> Assert.True(false, $"Unexpected workflow desugar: %A{other}")
        | other -> Assert.True(false, $"Unexpected desugared module: %A{other}")

[<Fact>]
let ``Desugar workflow implicit return`` () =
    let source =
        "def result = async {\n" +
        "  1\n" +
        "}"
    match parseModule source with
    | Result.Error err -> Assert.True(false, $"Parse error: {err}")
    | Result.Ok ast ->
        let desugared = Compiler.desugar ast
        match desugared with
        | [Def (ValueDef(_, "result", _, FunctionCall("async", _, [Lambda([], Block [ExprStatement (LiteralExpr (IntLit 1))])])))] ->
            Assert.True(true)
        | other -> Assert.True(false, $"Unexpected desugared module: %A{other}")

[<Fact>]
let ``Typecheck workflow context members`` () =
    let source =
        "context AsyncWorkflow = (await: (int, (int -> int)) -> int pure: int -> int)\n" +
        "def async: AsyncWorkflow = (await = { x, f -> f(x) }, pure = { x -> x })\n" +
        "def result = async {\n" +
        "  def x = await! 1\n" +
        "  return! x\n" +
        "}"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)

[<Fact>]
let ``Typecheck workflow requires pure`` () =
    let source =
        "context AsyncWorkflow = (await: (int, (int -> int)) -> int)\n" +
        "def async: AsyncWorkflow = (await = { x, f -> f(x) })\n" +
        "def result = async {\n" +
        "  return! 1\n" +
        "}"
    let result = Compiler.compile source
    Assert.False(result.Diagnostics.IsEmpty)
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("pure", message)

[<Fact>]
let ``Typecheck supports nominal types`` () =
    let source =
        "type $Email = string\n" +
        "def email: $Email = \"a@b.com\"\n" +
        "def addr: string = email\n" +
        "def email2: $Email = addr"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    match typed.Types.["email"] with
    | TyNominal(name, underlying, false) ->
        Assert.Equal("$Email", name)
        Assert.Equal(TyPrimitive "string", underlying)
    | other -> Assert.True(false, $"Unexpected nominal type: %A{other}")
    Assert.Equal(TyPrimitive "string", typed.Types.["addr"])
    match typed.Types.["email2"] with
    | TyNominal(name, _, false) -> Assert.Equal("$Email", name)
    | other -> Assert.True(false, $"Unexpected nominal type: %A{other}")

[<Fact>]
let ``Typecheck rejects distinct nominal types`` () =
    let source =
        "type $Email = string\n" +
        "type $UserId = string\n" +
        "def email: $Email = \"a@b.com\"\n" +
        "def user: $UserId = email"
    let result = Compiler.compile source
    Assert.False(result.Diagnostics.IsEmpty)
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("Type mismatch", message)

[<Fact>]
let ``Typecheck allows local private nominal construction`` () =
    let source =
        "type $Token = private string\n" +
        "def token: $Token = \"secret\""
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    match typed.Types.["token"] with
    | TyNominal(name, underlying, true) ->
        Assert.Equal("$Token", name)
        Assert.Equal(TyPrimitive "string", underlying)
    | other -> Assert.True(false, $"Unexpected token type: %A{other}")

[<Fact>]
let ``Typecheck match arms agree`` () =
    let source =
        "def result = match 1\n" +
        "  | 1 -> \"one\"\n" +
        "  | _ -> \"other\""
    let result = Compiler.compile source
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "string", typed.Types.["result"])

[<Fact>]
let ``Typecheck populates typed AST items`` () =
    let source = "def message = \"Hello\""
    let result = Compiler.compile source
    let typed = result.Typed.Value
    let hasMessage =
        typed.Items
        |> List.exists (function
            | TypedDef (TypedValueDef(_, name, _, typedExpr)) ->
                name = "message" && typedExpr.Type = TyPrimitive "string"
            | _ -> false)
    Assert.True(hasMessage)

[<Fact>]
let ``Typecheck captures typed block statements in lambda`` () =
    let source =
        "def f = { x ->\n" +
        "  def y = x\n" +
        "  y\n" +
        "}"
    let result = Compiler.compile source
    let typed = result.Typed.Value
    let hasBlockStatements =
        typed.Items
        |> List.exists (function
            | TypedDef (TypedValueDef(_, name, _, typedExpr)) when name = "f" ->
                match typedExpr.Body with
                | Some bodyExpr ->
                    match bodyExpr.Statements with
                    | Some statements ->
                        statements
                        |> List.exists (function
                            | TypedDefStatement(_, defName, _, _) -> defName = "y"
                            | _ -> false)
                    | None -> false
                | None -> false
            | _ -> false)
    Assert.True(hasBlockStatements)

[<Fact>]
let ``Typecheck supports match shorthand lambda`` () =
    let source =
        "def f: string -> int = {\n" +
        "  | \"one\" -> 1\n" +
        "  | \"two\" -> 2\n" +
        "  | _ -> 0\n" +
        "}\n" +
        "def result = f(\"one\")"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty)
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "int", typed.Types.["result"])

[<Fact>]
let ``Typecheck records typed match patterns`` () =
    let source =
        "def result = match 1\n" +
        "  | 1 -> \"one\"\n" +
        "  | _ -> \"other\""
    let result = Compiler.compile source
    let typed = result.Typed.Value
    let matchHasPattern =
        typed.Items
        |> List.exists (function
            | TypedDef (TypedValueDef(_, name, _, typedExpr)) when name = "result" ->
                match typedExpr.MatchArms with
                | Some arms ->
                    arms
                    |> List.exists (fun (patterns, _) ->
                        patterns
                        |> List.exists (fun pattern ->
                            match pattern.Pattern with
                            | LiteralPattern (IntLit 1) -> pattern.Type = TyPrimitive "int"
                            | _ -> false))
                | None -> false
            | _ -> false)
    Assert.True(matchHasPattern)

[<Fact>]
let ``Typecheck types list and guard patterns`` () =
    let source =
        "def result = match [1, 2]\n" +
        "  | [x, y] -> x\n" +
        "  | _ -> 0"
    let result = Compiler.compile source
    let typed = result.Typed.Value
    let hasListPattern =
        typed.Items
        |> List.exists (function
            | TypedDef (TypedValueDef(_, name, _, typedExpr)) when name = "result" ->
                match typedExpr.MatchArms with
                | Some arms ->
                    arms
                    |> List.exists (fun (patterns, _) ->
                        patterns
                        |> List.exists (fun pattern ->
                            match pattern.Pattern with
                            | ListPattern _ -> true
                            | _ -> false))
                | None -> false
            | _ -> false)
    Assert.True(hasListPattern)

[<Fact>]
let ``Typecheck infers list element type`` () =
    let source = "def numbers = [1, 2, 3]"
    let result = Compiler.compile source
    let typed = result.Typed.Value
    Assert.Equal(TyApply("list", [TyPrimitive "int"]), typed.Types.["numbers"])

[<Fact>]
let ``Typecheck infers operator results`` () =
    let source =
        "def sum = 1 + 2\n" +
        "def lt = 1 < 2\n" +
        "def eq = \"a\" == \"b\""
    let result = Compiler.compile source
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "int", typed.Types.["sum"])
    Assert.Equal(TyPrimitive "bool", typed.Types.["lt"])
    Assert.Equal(TyPrimitive "bool", typed.Types.["eq"])

[<Fact>]
let ``Compile reports diagnostics on parse error`` () =
    let source = "def message = "
    let result = Compiler.compile source
    Assert.Equal(CompilationPhase.Parsed, result.Phase)
    Assert.False(result.Diagnostics.IsEmpty)

[<Fact>]
let ``Unifier binds type variables`` () =
    let tvar = TyVar { Id = 1; Name = None }
    let constraints: ConstraintSet = [ (tvar, TyPrimitive "string", Equal) ]
    let result = Unifier.unify constraints
    match result with
    | Ok subst -> Assert.Equal(Some (TyPrimitive "string"), subst |> Map.tryFind 1)
    | Error message -> Assert.True(false, message)

[<Fact>]
let ``Unifier rejects occurs check`` () =
    let tvar = TyVar { Id = 2; Name = None }
    let constraintSet: ConstraintSet = [ (tvar, TyFunc(tvar, TyPrimitive "int"), Equal) ]
    let result = Unifier.unify constraintSet
    match result with
    | Ok _ -> Assert.True(false, "Expected occurs check failure")
    | Error message -> Assert.Contains("Occurs", message)

[<Fact>]
let ``Unifier allows record width subtyping`` () =
    let narrow = TyRecord (Map.ofList [ ("a", TyPrimitive "int") ])
    let wide = TyRecord (Map.ofList [ ("a", TyPrimitive "int"); ("b", TyPrimitive "string") ])
    let result = Unifier.unify [ (narrow, wide, Equal) ]
    match result with
    | Ok _ -> Assert.True(true)
    | Error message -> Assert.True(false, message)

[<Fact>]
let ``Unifier matches unions by set and width`` () =
    let unionA = TyUnion [ TyTag("some", Some (TyPrimitive "int")); TyTag("notFound", None) ]
    let unionB = TyUnion [ TyTag("notFound", None); TyTag("some", Some (TyPrimitive "int")) ]
    let unionWide = TyUnion [ TyTag("some", Some (TyPrimitive "int")); TyTag("notFound", None); TyTag("error", Some (TyPrimitive "string")) ]
    let resultOrder = Unifier.unify [ (unionA, unionB, Equal) ]
    let resultWidth = Unifier.unify [ (unionA, unionWide, Equal) ]
    match resultOrder with
    | Ok _ -> Assert.True(true)
    | Error message -> Assert.True(false, message)
    match resultWidth with
    | Ok _ -> Assert.True(true)
    | Error message -> Assert.True(false, message)
