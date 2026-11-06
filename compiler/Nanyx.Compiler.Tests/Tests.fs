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
let ``Match checks exhaustiveness when the scrutinee is a call result or a local bound to one`` () =
    // #61: the scrutinee's type was still an unsolved variable when the check ran, so it passed silently.
    let direct =
        Compiler.compile (
            "def getMessage: () -> #some(string) | #noMessage = { #some(\"hi\") }\n" +
            "def partial = {\n" +
            "  match getMessage()\n" +
            "    | #some(_) -> \"message\"\n" +
            "}\n")
    Assert.False(direct.Diagnostics.IsEmpty, "a match on an annotated call result must be checked")
    Assert.Contains("missing tags: noMessage", (direct.Diagnostics |> List.head).Message)
    let viaLocal =
        Compiler.compile (
            "def getMessage: () -> #some(string) | #noMessage = { #some(\"hi\") }\n" +
            "def partial = {\n" +
            "  def m = getMessage()\n" +
            "  match m\n" +
            "    | #some(_) -> \"message\"\n" +
            "}\n")
    Assert.False(viaLocal.Diagnostics.IsEmpty, "a match on a local bound to a call result must be checked")
    let complete =
        Compiler.compile (
            "def getMessage: () -> #some(string) | #noMessage = { #some(\"hi\") }\n" +
            "def total = {\n" +
            "  match getMessage()\n" +
            "    | #some(_) -> \"message\"\n" +
            "    | #noMessage -> \"none\"\n" +
            "}\n")
    Assert.True(complete.Diagnostics.IsEmpty, sprintf "%A" complete.Diagnostics)

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
    | TyFunc(TyRecord fields, TyVar _) when fields.ContainsKey("0") && fields.ContainsKey("1") ->
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
let ``Typecheck rejects an unknown context in a signature`` () =
    let source =
        "context MyConsole = (p: string -> ())\n" +
        "def greet: [Bogus] string -> () = { name -> p(name) }"
    let result = Compiler.compile source
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("Unknown context 'Bogus' in the signature of 'greet'", message)

[<Fact>]
let ``Typecheck rejects an unknown context in a local signature`` () =
    let source =
        "def main = {\n" +
        "  def greet: [Bogus] string -> () = { name -> () }\n" +
        "  greet(\"hi\")\n" +
        "}"
    let result = Compiler.compile source
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("Unknown context 'Bogus' in the signature of 'greet'", message)

[<Fact>]
let ``Typecheck accepts the Console context from nanyx/console`` () =
    let source =
        "import nanyx/console\n\n" +
        "def greet: [Console] string -> () = { name -> println(name) }"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")

[<Fact>]
let ``println comes from nanyx/console with type string -> unit`` () =
    let result = Compiler.compile "import nanyx/console\n\ndef result = println(\"hi\")"
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    Assert.Equal(TyPrimitive "unit", result.Typed.Value.Types.["result"])

[<Fact>]
let ``println rejects a non-string argument`` () =
    let result = Compiler.compile "import nanyx/console\n\nprintln(42)"
    Assert.False(result.Diagnostics.IsEmpty)

[<Fact>]
let ``println is not in scope without importing nanyx/console`` () =
    let result = Compiler.compile "def main = { println(\"hi\") }"
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("Unknown function 'println'", message)

[<Fact>]
let ``Standard library import is resolved without a file`` () =
    withTempDir (fun dir ->
        let mainPath = Path.Combine(dir, "main.nyx")
        File.WriteAllText(mainPath, "import nanyx/console\n\nprintln(\"Hello, Nanyx!\")\n")
        let result = Compiler.compileFile mainPath
        Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
        // The module is compiled together with the program: the backend sees its definitions, not the import.
        let hasImport = result.Typed.Value.Module |> List.exists (function Import _ -> true | _ -> false)
        Assert.False(hasImport)
        let hasPrintln = result.Typed.Value.Module |> List.exists (function Def (ValueDef(_, "println", _, _)) -> true | _ -> false)
        Assert.True(hasPrintln))

[<Fact>]
let ``nanyx/console declares the Console context`` () =
    let source =
        "import nanyx/console\n" +
        "def greet: [Console] string -> () = { name -> println(name) }\n" +
        "def main = { greet(\"hi\") }"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    Assert.True(result.Typed.Value.TypeDefs.ContainsKey "Console")

[<Fact>]
let ``nanyx/text exports string functions with their types`` () =
    let source =
        "import nanyx/text\n" +
        "def shout = toUpper(\"hi\")\n" +
        "def n = length(\"hi\")\n" +
        "def parts = split(\"a,b\", \",\")\n" +
        "def joined = join(parts, \"-\")\n" +
        "def yes = startsWith(\"nanyx\", \"nan\")"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    let types = result.Typed.Value.Types
    Assert.Equal(TyPrimitive "string", types.["shout"])
    Assert.Equal(TyPrimitive "int", types.["n"])
    Assert.Equal(TyApply("list", [ TyPrimitive "string" ]), types.["parts"])
    Assert.Equal(TyPrimitive "string", types.["joined"])
    Assert.Equal(TyPrimitive "bool", types.["yes"])

[<Fact>]
let ``nanyx/text rejects a wrongly typed argument`` () =
    let result = Compiler.compile "import nanyx/text\n\ndef bad = repeat(\"ab\", \"twice\")"
    Assert.False(result.Diagnostics.IsEmpty)

[<Fact>]
let ``Standard library private helpers are not imported`` () =
    let result = Compiler.compile "import nanyx/text\n\ndef bad = indexFrom(\"abc\", \"b\", 0)"
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("Unknown function 'indexFrom'", message)

[<Fact>]
let ``Compiler intrinsics are not in scope in user code`` () =
    let result = Compiler.compile "import nanyx/text\n\ndef bad = __nanyx_string_length(\"abc\")"
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("Unknown function '__nanyx_string_length'", message)

[<Fact>]
let ``Standard library import with alias is qualified`` () =
    let source =
        "import nanyx/text as t\n" +
        "def shout = t.toUpper(\"hi\")"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    Assert.Equal(TyPrimitive "string", result.Typed.Value.Types.["shout"])
    let unqualified = Compiler.compile "import nanyx/text as t\n\ndef shout = toUpper(\"hi\")"
    Assert.False(unqualified.Diagnostics.IsEmpty)

[<Fact>]
let ``Unknown standard library module is an error`` () =
    withTempDir (fun dir ->
        let mainPath = Path.Combine(dir, "main.nyx")
        File.WriteAllText(mainPath, "import nanyx/sockets\n")
        let result = Compiler.compileFile mainPath
        let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
        Assert.Contains("Unknown standard library module 'nanyx/sockets'", message)
        Assert.Contains("nanyx/console", message))

[<Fact>]
let ``Entry point module sees nanyx/console without importing it`` () =
    let source =
        "module main\n" +
        "@entry\n" +
        "def main = {\n" +
        "  println(\"Hello, World!\")\n" +
        "}\n"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    let withoutEntry = Compiler.compile "module main\n\ndef main = {\n  println(\"Hello, World!\")\n}\n"
    Assert.False(withoutEntry.Diagnostics.IsEmpty)

[<Fact>]
let ``Entry attribute appends a call to the entry point`` () =
    let source =
        "module main\n" +
        "@entry\n" +
        "def main = {\n" +
        "  println(\"Hello, World!\")\n" +
        "}\n"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    let entryCalls =
        result.Typed.Value.Module
        |> List.filter (function Expr (Kind (FunctionCall("main", _, []))) -> true | _ -> false)
    Assert.Equal(1, entryCalls.Length)
    let hasAttribute = result.Typed.Value.Module |> List.exists (function Attribute _ -> true | _ -> false)
    Assert.False(hasAttribute)

[<Fact>]
let ``Unknown attribute is an error`` () =
    let result = Compiler.compile "@inline\ndef f = { 1 }"
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("Unknown attribute '@inline'", message)

[<Fact>]
let ``Entry attribute must precede a definition`` () =
    let result = Compiler.compile "@entry\n1 + 1"
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("must be followed by a definition", message)

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
    | TyRecord fields when fields.ContainsKey("0") && fields.ContainsKey("1") && fields.ContainsKey("z") ->
        Assert.Equal(TyPrimitive "int", fields.["0"])
        Assert.Equal(TyPrimitive "int", fields.["1"])
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
        "def mapOption: (#some(a) | r), (a -> b) -> #some(b) | r = { o, f ->\n" +
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
let ``Typecheck rejects an annotation more general than its body`` () =
    let result = Compiler.compile "def f: a -> b = { x -> x }"
    Assert.False(result.Diagnostics.IsEmpty)
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("more general than the inferred type", message)
    Assert.Contains("`b` would have to be `a`", message)

[<Fact>]
let ``Typecheck rejects an annotation variable the body makes concrete`` () =
    let result = Compiler.compile "def g: a -> int = { x -> x + 1 }"
    Assert.False(result.Diagnostics.IsEmpty)
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("`a` would have to be `int`", message)

[<Fact>]
let ``Typecheck rejects a non-function annotation more general than its value`` () =
    let result = Compiler.compile "def xs: list(a) = [1, 2]"
    Assert.False(result.Diagnostics.IsEmpty)
    let message = result.Diagnostics |> List.head |> fun diag -> diag.Message
    Assert.Contains("`a` would have to be `int`", message)

[<Fact>]
let ``Typecheck accepts annotations the body proves`` () =
    let source =
        "def id: a -> a = { x -> x }\n" +
        "def first: (a, b) -> a = { x, y -> x }\n" +
        "def apply: (a -> b), a -> b = { f, x -> f(x) }\n" +
        "def mapOption: (#some(a) | r), (a -> b) -> (#some(b) | r) = { | #some(a), f -> #some(f(a)) | other, _ -> other }\n" +
        "def result = mapOption(#some(first(1, \"\")), { x -> apply(id, x) })"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"%A{result.Diagnostics}")
    let typed = result.Typed.Value
    match typed.Types.["result"] with
    | TyUnion [ TyTag("some", Some (TyPrimitive "int")); TyVar _ ] -> Assert.True(true)
    | other -> Assert.True(false, $"Unexpected result type: %A{other}")

[<Fact>]
let ``Typecheck requires catch-all for tag union rest`` () =
    let source =
        "def mapOption: (#some(a) | r), (a -> b) -> #some(b) | r = { o, f ->\n" +
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
        "def mapOption: (#some(a) | r), (a -> b) -> #some(b) | r = { o, f ->\n" +
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
    | TyUnion [ TyTag("some", Some (TyPrimitive "int")); TyTag("nil", None) ] -> Assert.True(true)
    | other -> Assert.True(false, $"Unexpected l2 type: %A{other}")

[<Fact>]
let ``Typecheck binds a catch-all after a tag arm at the union's rest`` () =
    // `other` cannot be a `#some`, so it is the rest `r`: returning it does not force `a` and `b` together.
    let source =
        "def mapOption\n" +
        "  : (#some(a) | r), (a -> b) -> (#some(b) | r)\n" +
        "  = { | #some(a), f -> #some(f(a))\n" +
        "      | other, _ -> other }\n" +
        "def x: #some(int) | #notFound | #error = #some(1)\n" +
        "def y = mapOption(x, { n -> \"{n}\" })"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, sprintf "%A" result.Diagnostics)
    let typed = result.Typed.Value
    match typed.Types.["mapOption"] with
    | TyFunc(TyRecord fields, TyUnion [ TyTag("some", Some (TyVar b)); TyVar resultRest ]) ->
        match fields.["0"], fields.["1"] with
        | TyUnion [ TyTag("some", Some (TyVar a)); TyVar inputRest ], TyFunc(TyVar fa, TyVar fb) ->
            Assert.NotEqual(a.Id, b.Id)
            Assert.Equal(a.Id, fa.Id)
            Assert.Equal(b.Id, fb.Id)
            Assert.Equal(inputRest.Id, resultRest.Id)
        | other -> Assert.True(false, $"Unexpected mapOption parameters: %A{other}")
    | other -> Assert.True(false, $"Unexpected mapOption type: %A{other}")
    match typed.Types.["y"] with
    | TyUnion [ TyTag("some", Some (TyPrimitive "string")); TyTag("error", None); TyTag("notFound", None) ] -> ()
    | other -> Assert.True(false, $"Unexpected y type: %A{other}")

[<Fact>]
let ``Typecheck gives a lambda argument's parameter its type from the callee`` () =
    // `p.x` is checked inside the lambda, before the call is solved: `p` is a `P` from `apply`'s signature.
    let source =
        "type P = (x: int)\n" +
        "def apply: P, (P -> int) -> int = { p, f -> f(p) }\n" +
        "def n = P(x = 3) \\apply { p -> p.x }"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"%A{result.Diagnostics}")
    Assert.Equal(TyPrimitive "int", result.Typed.Value.Types.["n"])

[<Fact>]
let ``Typecheck gives a lambda argument's parameter a type solved from the arguments before it`` () =
    // `a` in `mapOption`'s signature is only known from the first argument, a `#some(Customer)`.
    let source =
        "type Customer = (name: string)\n" +
        "def mapOption\n" +
        "  : (#some(a) | r), (a -> b) -> (#some(b) | r)\n" +
        "  = { | #some(a), f -> #some(f(a))\n" +
        "      | other, _ -> other }\n" +
        "def c: #some(Customer) | #notFound = #notFound\n" +
        "def name = c \\mapOption { c -> c.name }"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"%A{result.Diagnostics}")
    match result.Typed.Value.Types.["name"] with
    | TyUnion [ TyTag("some", Some (TyPrimitive "string")); TyTag("notFound", None) ] -> ()
    | other -> Assert.True(false, $"Unexpected name type: %A{other}")

[<Fact>]
let ``Typecheck matches a call's result whose union rest is already solved`` () =
    // `mapOption`'s result is `#some(b) | r`, but here `r` is `#notFound`, so naming both tags is exhaustive.
    let mapOption =
        "def mapOption\n" +
        "  : (#some(a) | r), (a -> b) -> (#some(b) | r)\n" +
        "  = { | #some(a), f -> #some(f(a))\n" +
        "      | other, _ -> other }\n" +
        "def x: #some(int) | #notFound = #notFound\n"
    let exhaustive =
        mapOption +
        "def y = {\n" +
        "  def a = x \\mapOption { n -> n + 1 }\n" +
        "  match a\n" +
        "    | #some(n) -> n\n" +
        "    | #notFound -> 0\n" +
        "}\n"
    let result = Compiler.compile exhaustive
    Assert.True(result.Diagnostics.IsEmpty, $"%A{result.Diagnostics}")
    let missing =
        mapOption +
        "def y = {\n" +
        "  def a = x \\mapOption { n -> n + 1 }\n" +
        "  match a\n" +
        "    | #some(n) -> n\n" +
        "}\n"
    let result = Compiler.compile missing
    Assert.False(result.Diagnostics.IsEmpty)
    Assert.Contains("missing tags: notFound", (List.head result.Diagnostics).Message)

[<Fact>]
let ``Typecheck rejects a tag union with a rest where a string is expected`` () =
    // The rest `r` stands for more tags, so it cannot make `#some(string) | r` a `string`.
    let source =
        "def mapOption\n" +
        "  : (#some(a) | r), (a -> b) -> (#some(b) | r)\n" +
        "  = { | #some(a), f -> #some(f(a))\n" +
        "      | other, _ -> other }\n" +
        "def takesString: string -> string = { s -> s }\n" +
        "def x: #some(int) | #notFound = #notFound\n" +
        "def y = takesString(x \\mapOption { n -> \"{n}\" })"
    let result = Compiler.compile source
    Assert.False(result.Diagnostics.IsEmpty)
    Assert.Contains("mismatch", (List.head result.Diagnostics).Message)

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
        | [Def (ValueDef(_, "result", _, Kind (FunctionCall("async", _, [Kind (Lambda([], Kind (Block [ExprStatement expr])))]))))] ->
            match expr.Kind with
            | FunctionCall(
                "await",
                _,
                [
                    Kind (FunctionCall("foo", _, []))
                    Kind (Lambda([("x", None)], Kind (FunctionCall("pure", _, [Kind (MemberAccess(Kind (IdentifierExpr "x"), "y", _))]))))
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
        | [Def (ValueDef(_, "result", _, Kind (FunctionCall("async", _, [Kind (Lambda([], Kind (Block [ExprStatement (Kind (LiteralExpr (IntLit 1)))])))]))))] ->
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
let ``Typecheck joins the tags of if branches into a union`` () =
    let source =
        "def getMessage = {\n" +
        "  def message = \"Hello\"\n" +
        "  if false then #some(message)\n" +
        "  else #noMessage\n" +
        "}"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    let typed = result.Typed.Value
    let expected = TyFunc(TyPrimitive "unit", TyUnion [ TyTag("some", Some (TyPrimitive "string")); TyTag("noMessage", None) ])
    Assert.Equal(expected, typed.Types.["getMessage"])

[<Fact>]
let ``Typecheck joins the tags of match arms into a union`` () =
    let source =
        "def classify = { n ->\n" +
        "  match n\n" +
        "    | 0 -> #zero\n" +
        "    | 1 -> #one(n)\n" +
        "    | _ -> #many(n)\n" +
        "}"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    let typed = result.Typed.Value
    let expected =
        TyFunc(TyPrimitive "int", TyUnion [ TyTag("zero", None); TyTag("one", Some (TyPrimitive "int")); TyTag("many", Some (TyPrimitive "int")) ])
    Assert.Equal(expected, typed.Types.["classify"])

[<Fact>]
let ``Typecheck joins nested if branches and repeated tags once`` () =
    let source =
        "def pick = { n ->\n" +
        "  if n == 0 then #zero\n" +
        "  else if n == 1 then #small(n)\n" +
        "  else if n == 2 then #small(n)\n" +
        "  else #big\n" +
        "}"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    let typed = result.Typed.Value
    let expected = TyFunc(TyPrimitive "int", TyUnion [ TyTag("zero", None); TyTag("small", Some (TyPrimitive "int")); TyTag("big", None) ])
    Assert.Equal(expected, typed.Types.["pick"])

[<Fact>]
let ``Typecheck still rejects same-named tags with different payloads across branches`` () =
    let source = "def f = { c -> if c then #some(1) else #some(\"one\") }"
    let result = Compiler.compile source
    Assert.False(result.Diagnostics.IsEmpty)
    Assert.Contains("mismatch", (List.head result.Diagnostics).Message)

[<Fact>]
let ``Typecheck still rejects branches of different non-tag types`` () =
    let source = "def f = { c -> if c then 1 else \"one\" }"
    let result = Compiler.compile source
    Assert.False(result.Diagnostics.IsEmpty)
    Assert.Contains("Type mismatch", (List.head result.Diagnostics).Message)

[<Fact>]
let ``Typecheck matches on an inferred tag union exhaustively`` () =
    // Exhaustiveness is checked against the scrutinee's known type; an inferred union counts as known.
    // (Scrutinees that are call results or local bindings are not checked today, inferred or annotated.)
    let source =
        "def message = if false then #some(\"hi\") else #noMessage\n" +
        "def describe = {\n" +
        "  match message\n" +
        "    | #some(_) -> \"message\"\n" +
        "    | #noMessage -> \"nothing\"\n" +
        "}\n" +
        "def partial = {\n" +
        "  match message\n" +
        "    | #some(_) -> \"message\"\n" +
        "}"
    let result = Compiler.compile source
    Assert.Equal(1, result.Diagnostics.Length)
    Assert.Contains("Non-exhaustive match", (List.head result.Diagnostics).Message)
    Assert.Contains("noMessage", (List.head result.Diagnostics).Message)

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
let ``Typecheck ++ concatenates strings and lists`` () =
    let source =
        "def greeting = \"Hello, \" ++ \"World\"\n" +
        "def numbers = [1, 2] ++ [3]\n" +
        "def shout: string -> string = { s -> s ++ \"!\" }\n" +
        "def twice: list(int) -> list(int) = { xs -> xs ++ xs }"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    let typed = result.Typed.Value
    Assert.Equal(TyPrimitive "string", typed.Types.["greeting"])
    Assert.Equal(TyApply("list", [ TyPrimitive "int" ]), typed.Types.["numbers"])

[<Fact>]
let ``Typecheck rejects ++ between a string and an int`` () =
    let result = Compiler.compile "def bad = \"a\" ++ 1"
    Assert.False(result.Diagnostics.IsEmpty)

[<Fact>]
let ``Typecheck rejects + on strings`` () =
    let result = Compiler.compile "def bad = \"a\" + \"b\""
    Assert.False(result.Diagnostics.IsEmpty)

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

[<Fact>]
let ``Typecheck allows mutual recursion between annotated top-level defs`` () =
    let source =
        "def isEven: int -> bool = { n -> if n == 0 then true else isOdd(n - 1) }\n" +
        "def isOdd: int -> bool = { n -> if n == 0 then false else isEven(n - 1) }\n" +
        "def result = isEven(10)"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    Assert.Equal(TyPrimitive "bool", result.Typed.Value.Types.["result"])

[<Fact>]
let ``Typecheck allows a forward reference to an unannotated def`` () =
    let source =
        "def result = addOne(41)\n" +
        "def addOne = { x -> x + 1 }"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    Assert.Equal(TyPrimitive "int", result.Typed.Value.Types.["result"])
    Assert.Equal(TyFunc(TyPrimitive "int", TyPrimitive "int"), result.Typed.Value.Types.["addOne"])

[<Fact>]
let ``Typecheck lets an annotation name a type declared later`` () =
    let source =
        "def favourite: Color = #green\n" +
        "type Color = #red | #green | #blue"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")

[<Fact>]
let ``Typecheck still generalizes a def used after its definition`` () =
    let source =
        "def id = { x -> x }\n" +
        "def a = id(1)\n" +
        "def b = id(\"s\")"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    Assert.Equal(TyPrimitive "int", result.Typed.Value.Types.["a"])
    Assert.Equal(TyPrimitive "string", result.Typed.Value.Types.["b"])
[<Fact>]
let ``Typecheck types the boolean keywords as bool`` () =
    let source =
        "def a = true and false\n" +
        "def b = a or not a\n" +
        "def c = not (1 < 2)"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, $"Unexpected diagnostics: %A{result.Diagnostics}")
    let types = result.Typed.Value.Types
    Assert.Equal(TyPrimitive "bool", types.["a"])
    Assert.Equal(TyPrimitive "bool", types.["b"])
    Assert.Equal(TyPrimitive "bool", types.["c"])

[<Fact>]
let ``Typecheck gives a record update the record's type and checks the updated fields`` () =
    let ok = Compiler.compile "def alice = (name = \"Alice\", age = 30)\ndef older = alice with age = 31\ndef n: int = older.age"
    Assert.True(ok.Diagnostics.IsEmpty, sprintf "%A" ok.Diagnostics)
    let wrongType = Compiler.compile "def alice = (name = \"Alice\", age = 30)\ndef older = alice with age = \"old\""
    Assert.False(wrongType.Diagnostics.IsEmpty, "updating age with a string should not type-check")
    let unknownField = Compiler.compile "def alice = (name = \"Alice\", age = 30)\ndef older = alice with height = 1"
    Assert.False(unknownField.Diagnostics.IsEmpty, "updating a field the record lacks should not type-check")
[<Fact>]
let ``Typecheck allows member access on the result of a call`` () =
    // #60: the call's result used to be an unsolved variable when the member access looked for fields.
    let declared =
        Compiler.compile (
            "type Point = (x: int, y: int)\n" +
            "def shift: Point -> Point = { q -> Point(x = q.x + 1, y = q.y + 1) }\n" +
            "def a = shift(Point(x = 3, y = 4)).x\n" +
            "def q = shift(Point(x = 3, y = 4))\n" +
            "def b: int = q.y\n")
    Assert.True(declared.Diagnostics.IsEmpty, sprintf "%A" declared.Diagnostics)
    let inferred =
        Compiler.compile (
            "def make = { n -> (value = n, label = \"v\") }\n" +
            "def m = make(1)\n" +
            "def s: string = m.label\n" +
            "def t = (1, \"two\")\n" +
            "def pick = { p -> p }\n" +
            "def u: string = pick(t).1\n")
    Assert.True(inferred.Diagnostics.IsEmpty, sprintf "%A" inferred.Diagnostics)
    let missing = Compiler.compile "type Point = (x: int, y: int)\ndef id: Point -> Point = { p -> p }\ndef z = id(Point(x = 1, y = 2)).z"
    Assert.False(missing.Diagnostics.IsEmpty, "a field the result lacks should still be an error")
[<Fact>]
let ``Typecheck leaves the scrutinee union open when a match has a catch-all arm`` () =
    // `| #some(v) -> ... | _ -> ...` names only one tag; the scrutinee may still be a wider union.
    let result =
        Compiler.compile (
            "def get: int -> #some(int) | #none = { n -> #some(n) }\n" +
            "def d = { n -> match get(n)\n" +
            "  | #some(v) -> v\n" +
            "  | _ -> 0\n" +
            "}\n" +
            "def e = if get(1) is #some(v) then v else 0\n")
    Assert.True(result.Diagnostics.IsEmpty, sprintf "%A" result.Diagnostics)
[<Fact>]
let ``Typecheck accepts arithmetic and comparison on floats`` () =
    // #36: `+ - * /` and `< > <= >=` used to pin their operands to int.
    let ok =
        Compiler.compile (
            "def pi = 3.14\n" +
            "def d = pi * 2.0\n" +
            "def e: float = 1.5 + 2.5\n" +
            "def half = { x -> x / 2.0 }\n" +
            "def big = 2.5 > 1.0\n" +
            "def sum: (float, float) -> float = { a, b -> a + b }\n")
    Assert.True(ok.Diagnostics.IsEmpty, sprintf "%A" ok.Diagnostics)
    let mixed = Compiler.compile "def m = 1 + 2.0"
    Assert.False(mixed.Diagnostics.IsEmpty, "int + float should not type-check")
    let stillInt = Compiler.compile "def n: float = 1 + 2"
    Assert.False(stillInt.Diagnostics.IsEmpty, "int arithmetic is still int")
[<Fact>]
let ``Typecheck types for loops and ranges`` () =
    let ok =
        Compiler.compile (
            "def r = 1..3\n" +
            "def total = { xs -> for xs | x -> x + 1 }\n" +
            "def f = for 1..3 | i -> i * 2\n" +
            "def g: () = for [\"a\"] | s -> s ++ \"!\"\n")
    Assert.True(ok.Diagnostics.IsEmpty, sprintf "%A" ok.Diagnostics)
    let wrong = Compiler.compile "def h = for 1..3 | i -> i ++ \"x\""
    Assert.False(wrong.Diagnostics.IsEmpty, "a range element is an int")
    let badRange = Compiler.compile "def h = \"a\"..\"z\""
    Assert.False(badRange.Diagnostics.IsEmpty, "ranges are over ints")

[<Fact>]
let ``Typecheck rejects boolean keywords on non-booleans`` () =
    let andResult = Compiler.compile "def a = 1 and 2"
    Assert.False(andResult.Diagnostics.IsEmpty, "`1 and 2` should not type-check")
    let notResult = Compiler.compile "def b = not \"text\""
    Assert.False(notResult.Diagnostics.IsEmpty, "`not \"text\"` should not type-check")

// Hover: the type of the expression at a position.

/// The offset of the `occurrence`th (zero-based) appearance of `text` in `source`, plus `within` characters.
let private offsetOf (source: string) (text: string) (occurrence: int) (within: int) =
    let rec find from remaining =
        let index = source.IndexOf(text, from, StringComparison.Ordinal)
        if index < 0 then failwith $"'{text}' not found"
        elif remaining = 0 then index
        else find (index + 1) (remaining - 1)
    find 0 occurrence + within

let private hoverWith (names: TypeVarNames) (source: string) (offset: int) =
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, sprintf "%A" result.Diagnostics)
    Hover.typeAt names result offset

let private hoverAt source offset = hoverWith Greek source offset

let private hoverType source offset =
    hoverAt source offset |> Option.map (fun info -> info.Type)

let private latinHoverType source offset =
    hoverWith Latin source offset |> Option.map (fun info -> info.Type)

[<Fact>]
let ``Expressions at a position are innermost first`` () =
    let source = "def value = 1 + 2 * 3"
    let syntax =
        match Parser.Program.parseModule source with
        | Ok syntax -> syntax
        | Error message -> failwith message
    let found = Hover.expressionsAt (offsetOf source "2" 0 0) syntax
    match found |> List.map (fun expr -> expr.Kind) with
    | [ LiteralExpr (IntLit 2); BinaryOp("*", _, _); BinaryOp("+", _, _) ] -> ()
    | kinds -> failwithf "Unexpected expressions: %A" kinds
    // Each one encloses the one before it.
    found
    |> List.pairwise
    |> List.iter (fun (inner, outer) ->
        Assert.True(outer.Span.Start <= inner.Span.Start && inner.Span.End <= outer.Span.End))

[<Fact>]
let ``Nothing is found outside every expression`` () =
    let source = "def value = 1 + 2"
    Assert.Equal(None, hoverType source 0)

[<Fact>]
let ``Hover shows the type of literals, locals, parameters, calls and lambdas`` () =
    let source =
        "def double = { n -> n * 2 }\n" +
        "def answer = {\n" +
        "  def half = 21\n" +
        "  double(half)\n" +
        "}\n"
    // The innermost expression wins: the literal, not the multiplication around it.
    Assert.Equal(Some "int", hoverType source (offsetOf source "2" 0 0))
    Assert.Equal(Some "int", hoverType source (offsetOf source "n *" 0 0))
    Assert.Equal(Some "int -> int", hoverType source (offsetOf source "{ n" 0 0))
    Assert.Equal(Some "int", hoverType source (offsetOf source "half)" 0 0))
    Assert.Equal(Some "int", hoverType source (offsetOf source "(half)" 0 0))
    // On the call's function name, the function's type.
    Assert.Equal(Some "int -> int", hoverType source (offsetOf source "double(" 0 1))

[<Fact>]
let ``Hover range is the span of the expression`` () =
    let source = "def value = 1 + 23"
    let info = (hoverAt source (offsetOf source "23" 0 1)).Value
    Assert.Equal("int", info.Type)
    Assert.Equal(offsetOf source "23" 0 0, info.Span.Start)
    Assert.Equal(offsetOf source "23" 0 2, info.Span.End)

[<Fact>]
let ``Hover on a polymorphic function shows it instantiated at the call`` () =
    let source =
        "def id = { x -> x }\n" +
        "def n = id(5)\n" +
        "def s = id(\"a\")\n"
    Assert.Equal(Some "α -> α", hoverType source (offsetOf source "{ x" 0 0))
    Assert.Equal(Some "α", hoverType source (offsetOf source "-> x" 0 3))
    Assert.Equal(Some "int -> int", hoverType source (offsetOf source "id(" 0 0))
    Assert.Equal(Some "string -> string", hoverType source (offsetOf source "id(" 1 0))
    Assert.Equal(Some "string", hoverType source (offsetOf source "id(" 1 4))

[<Fact>]
let ``Type variables are shown as α, β in order of appearance`` () =
    let source = "def pair = { a, b -> (b, a) }"
    Assert.Equal(Some "α, β -> (β, α)", hoverType source (offsetOf source "{" 0 0))

[<Fact>]
let ``Type variables are shown as a, b when Latin names are asked for`` () =
    let source = "def pair = { a, b -> (b, a) }"
    Assert.Equal(Some "a, b -> (b, a)", latinHoverType source (offsetOf source "{" 0 0))

[<Fact>]
let ``Greek type variable names go through the alphabet, then start again with a subscript`` () =
    let vars = [ for id in 1..26 -> TyVar { Id = id; Name = None } ]
    let shown = Unifier.tyToDisplayString Greek (TyApply("f", vars))
    Assert.Equal("f(α, β, γ, δ, ε, ζ, η, θ, ι, κ, λ, μ, ν, ξ, ο, π, ρ, σ, τ, υ, φ, χ, ψ, ω, α₁, β₁)", shown)

[<Fact>]
let ``A union's rest variable is named from the end of the alphabet`` () =
    // The catch-all arm leaves the union open for the tags the arms do not name.
    let source =
        "def mapOption = { o, f ->\n" +
        "  match o\n" +
        "  | #some(a) -> #some(f(a))\n" +
        "  | other -> other\n" +
        "}\n"
    Assert.Equal(Some "#some(α) | ω, (α -> β) -> #some(β) | ω", hoverType source (offsetOf source "{" 0 0))
    Assert.Equal(Some "#some(a) | z, (a -> b) -> #some(b) | z", latinHoverType source (offsetOf source "{" 0 0))

[<Fact>]
let ``A def's type is resolved before it is generalised, so a use of it has the inferred type`` () =
    let source = "def increment = { x -> x + 1 }\ndef f = increment\n"
    Assert.Equal(Some "int -> int", hoverType source (offsetOf source "increment" 1 0))

[<Fact>]
let ``A shorthand match lambda shows its own type, not that of the parameters it makes up`` () =
    let source =
        "def mapOption = { | #some(a), f -> #some(f(a)) | other, _ -> other }\n" +
        "def g = mapOption\n"
    Assert.Equal(Some "#some(α) | ω, (α -> β) -> #some(β) | ω", hoverType source (offsetOf source "{" 0 0))
    Assert.Equal(Some "#some(α) | ω, (α -> β) -> #some(β) | ω", hoverType source (offsetOf source "mapOption" 1 0))

[<Fact>]
let ``Where the two ends of the alphabet meet, each goes round its own part again`` () =
    let var id = TyVar { Id = id; Name = None }
    let open' id = TyUnion [ TyTag("x", None); var id ]
    // 20 variables and 4 rests take the whole alphabet the first time, so the front keeps α to υ and the back
    // φ to ω; the rests and variables after that go round their own parts again.
    let ty = TyApply("f", [ for id in 1..20 -> var id ] @ [ for id in 21..26 -> open' id ] @ [ var 27; var 28 ])
    Assert.Equal(
        "f(α, β, γ, δ, ε, ζ, η, θ, ι, κ, λ, μ, ν, ξ, ο, π, ρ, σ, τ, υ, "
        + "#x | ω, #x | ψ, #x | χ, #x | φ, #x | ω₁, #x | ψ₁, α₁, β₁)",
        Unifier.tyToDisplayString Greek ty)

[<Fact>]
let ``Hover inside a for loop's arm shows the arm's expression, not the loop element`` () =
    let source = "def f = for [\"a\"] | s -> s ++ \"!\""
    let info = (hoverAt source (offsetOf source "\"!\"" 0 1)).Value
    Assert.Equal("string", info.Type)
    Assert.Equal(offsetOf source "\"!\"" 0 0, info.Span.Start)
    Assert.Equal(Some "int", hoverType "def g = for 1..3 | i -> i * 2" (offsetOf "def g = for 1..3 | i -> i * 2" "2" 0 0))

[<Fact>]
let ``Hover inside a match arm shows the innermost expression`` () =
    let source =
        "def describe = { n ->\n" +
        "  match n\n" +
        "  | 0 -> \"zero\"\n" +
        "  | _ -> \"many\"\n" +
        "}\n"
    let info = (hoverAt source (offsetOf source "\"many\"" 0 2)).Value
    Assert.Equal("string", info.Type)
    Assert.Equal(offsetOf source "\"many\"" 0 0, info.Span.Start)

[<Fact>]
let ``A workflow bind is not given the type of the rest of the workflow`` () =
    let source =
        "context AsyncWorkflow = (await: (string, (string -> int)) -> int pure: int -> int)\n" +
        "def async: AsyncWorkflow = (await = { x, f -> f(x) }, pure = { x -> x })\n" +
        "def result = async {\n" +
        "  def x = await! \"a\"\n" +
        "  return! 1\n" +
        "}"
    let result = Compiler.compile source
    Assert.True(result.Diagnostics.IsEmpty, sprintf "%A" result.Diagnostics)
    let bind =
        Hover.expressionsAt (offsetOf source "await!" 0 0) result.Syntax.Value
        |> List.find (fun expr -> match expr.Kind with WorkflowBindExpr _ -> true | _ -> false)
    Assert.False(result.Typed.Value.ExprTypes.ContainsKey bind.Id)
    Assert.Equal(Some "string", hoverType source (offsetOf source "\"a\"" 0 1))

[<Fact>]
let ``Type variables named in an annotation keep their names, written as in the annotation`` () =
    let source = "def first: (t, a) -> t = { x, y -> x }"
    Assert.Equal(Some "t, a -> t", hoverType source (offsetOf source "{" 0 0))
    let mixed =
        "def f: a -> int = { x ->\n" +
        "  def g = { y -> (y, x) }\n" +
        "  1\n" +
        "}\n"
    Assert.Equal(Some "α -> (α, a)", hoverType mixed (offsetOf mixed "{ y" 0 0))
    // A made-up Latin name skips the names an annotation took.
    Assert.Equal(Some "b -> (b, a)", latinHoverType mixed (offsetOf mixed "{ y" 0 0))

[<Fact>]
let ``Hover shows tags with their hash`` () =
    let source = "def found = #some(1)\ndef missing = #notFound"
    Assert.Equal(Some "#some(int)", hoverType source (offsetOf source "#some" 0 1))
    Assert.Equal(Some "#notFound", hoverType source (offsetOf source "#notFound" 0 1))
    let union =
        "def mapOption: (#some(a) | r), (a -> b) -> (#some(b) | r) = { | #some(a), f -> #some(f(a)) | other, _ -> other }\n" +
        "def result = mapOption(#some(1), { n -> \"Mapped: {n}\" })"
    Assert.Equal(
        Some "#some(int) | ω, (int -> string) -> #some(string) | ω",
        hoverType union (offsetOf union "mapOption(" 0 0))

[<Fact>]
let ``Printed types have the fewest brackets the grammar allows`` () =
    let a = TyVar { Id = 1; Name = Some "a" }
    let b = TyVar { Id = 2; Name = Some "b" }
    let c = TyVar { Id = 3; Name = Some "c" }
    let union = TyUnion [ TyTag("some", Some a); TyTag("none", None) ]
    let pair x y = TyRecord(Map.ofList [ "0", x; "1", y ])
    // `->` groups to the right: a function input is bracketed, a function result is not.
    Assert.Equal("(a -> b) -> c", Unifier.tyToString (TyFunc(TyFunc(a, b), c)))
    Assert.Equal("a -> b -> c", Unifier.tyToString (TyFunc(a, TyFunc(b, c))))
    // `|` binds tighter than `->`: a union is never bracketed next to an arrow.
    Assert.Equal("a -> #some(a) | #none", Unifier.tyToString (TyFunc(a, union)))
    Assert.Equal("#some(a) | #none -> a", Unifier.tyToString (TyFunc(union, a)))
    // A signature's parameters are bare; a function among them, or anywhere items are separated by commas,
    // has brackets of its own.
    Assert.Equal("int, (a -> b) -> c", Unifier.tyToString (TyFunc(pair (TyPrimitive "int") (TyFunc(a, b)), c)))
    Assert.Equal("(int, (a -> b))", Unifier.tyToString (pair (TyPrimitive "int") (TyFunc(a, b))))
    Assert.Equal("map(string, (a -> b))", Unifier.tyToString (TyApply("map", [ TyPrimitive "string"; TyFunc(a, b) ])))
    // Only the signature itself writes parameters bare: a function nested in it, as its input or its result,
    // keeps its brackets, since a bare list can only come before the arrow.
    Assert.Equal("((a, b) -> c) -> (a, b) -> c", Unifier.tyToString (TyFunc(TyFunc(pair a b, c), TyFunc(pair a b, c))))
    Assert.Equal("a -> (a, b) -> c", Unifier.tyToString (TyFunc(a, TyFunc(pair a b, c))))
    // A lone argument or payload needs no brackets, and a tuple payload's items are the payload's.
    Assert.Equal("list(a -> b)", Unifier.tyToString (TyApply("list", [ TyFunc(a, b) ])))
    Assert.Equal("#some(int)", Unifier.tyToString (TyTag("some", Some (TyPrimitive "int"))))
    Assert.Equal("#pair(int, (a -> b))", Unifier.tyToString (TyTag("pair", Some (pair (TyPrimitive "int") (TyFunc(a, b))))))
    Assert.Equal("#apply((int, a) -> b)",Unifier.tyToString (TyTag("apply", Some (TyFunc(pair (TyPrimitive "int") a, b)))))
    // The unit type is written `()`.
    Assert.Equal("() -> ()", Unifier.tyToString (TyFunc(TyPrimitive "unit", TyPrimitive "unit")))

// Symbols and references: what each name refers to.

/// The symbol that `table` records for the innermost expression at `offset` that it has an entry for.
let private symbolAt (table: TypedModule -> Map<NodeId, SymbolId>) (result: CompileResult) (offset: int) =
    Assert.True(result.Diagnostics.IsEmpty, sprintf "%A" result.Diagnostics)
    let typed = result.Typed.Value
    Hover.expressionsAt offset result.Syntax.Value
    |> List.tryPick (fun expr -> table typed |> Map.tryFind expr.Id)
    |> Option.map (fun id -> typed.Symbols.[id])

/// The kind of what the name at `offset` refers to, and where that is declared.
let private resolvesTo (source: string) (offset: int) =
    symbolAt (fun typed -> typed.References) (Compiler.compile source) offset
    |> Option.map (fun symbol -> symbol.Kind, symbol.Declaration.Start)

[<Fact>]
let ``Names refer to parameters and locals`` () =
    let source =
        "def scale = { factor ->\n" +
        "  def offset = 1\n" +
        "  factor * 2 + offset\n" +
        "}\n"
    Assert.Equal(Some (Parameter, offsetOf source "factor" 0 0), resolvesTo source (offsetOf source "factor *" 0 0))
    Assert.Equal(Some (LocalValue, offsetOf source "offset" 0 0), resolvesTo source (offsetOf source "offset" 1 0))
    // A declaration spans its name.
    let symbol = (symbolAt (fun typed -> typed.References) (Compiler.compile source) (offsetOf source "offset" 1 0)).Value
    Assert.Equal(offsetOf source "offset" 0 6, symbol.Declaration.End)

[<Fact>]
let ``Names refer to the variables patterns bind`` () =
    let source =
        "def rest = { items -> match items\n" +
        "  | [_, ...tail] -> tail\n" +
        "  | _ -> items }\n" +
        "def either = { pair -> match pair\n" +
        "  | (a, 0) | (0, a) -> a\n" +
        "  | _ -> 0 }\n" +
        "def unwrap = if #some(1) is #some(v) then v else 0\n"
    Assert.Equal(Some (PatternVariable, offsetOf source "tail" 0 0), resolvesTo source (offsetOf source "tail" 1 0))
    // Each alternative of an or-pattern binds the same variable, declared where it is first written.
    Assert.Equal(Some (PatternVariable, offsetOf source "a, 0" 0 0), resolvesTo source (offsetOf source "-> a" 0 3))
    Assert.Equal(Some (PatternVariable, offsetOf source "v)" 0 0), resolvesTo source (offsetOf source "then v" 0 5))

[<Fact>]
let ``The nearest declaration of a name wins`` () =
    let source =
        "def x = 1\n" +
        "def f = { x -> x }\n" +
        "def g = {\n" +
        "  def x = 2\n" +
        "  x\n" +
        "}\n" +
        "def h = x\n"
    Assert.Equal(Some (Parameter, offsetOf source "{ x" 0 2), resolvesTo source (offsetOf source "-> x" 0 3))
    Assert.Equal(Some (LocalValue, offsetOf source "def x = 2" 0 4), resolvesTo source (offsetOf source "  x\n" 0 2))
    Assert.Equal(Some (TopLevelValue, offsetOf source "def x = 1" 0 4), resolvesTo source (offsetOf source "def h = x" 0 8))

[<Fact>]
let ``Names refer to top-level definitions declared later`` () =
    let source =
        "def a = b + 1\n" +
        "def c = twice(b)\n" +
        "def d = b \\twice\n" +
        "def b = 2\n" +
        "def twice = { n -> n * 2 }\n" +
        "def (p, q) = (1, 2)\n" +
        "def r = q\n"
    let b = offsetOf source "def b" 0 4
    let twice = offsetOf source "def twice" 0 4
    Assert.Equal(Some (TopLevelValue, b), resolvesTo source (offsetOf source "b + 1" 0 0))
    // A call and a pipe refer to the function by its name.
    Assert.Equal(Some (TopLevelValue, twice), resolvesTo source (offsetOf source "twice(" 0 0))
    Assert.Equal(Some (TopLevelValue, twice), resolvesTo source (offsetOf source "\\twice" 0 1))
    // A destructuring def declares each name where the pattern writes it.
    Assert.Equal(Some (TopLevelValue, offsetOf source "q)" 0 0), resolvesTo source (offsetOf source "= q" 0 2))

[<Fact>]
let ``Every declaration is a symbol, referred to or not`` () =
    let result = Compiler.compile "def f = { used, unused -> used }\ndef g = 1\n"
    let typed = result.Typed.Value
    let declared =
        typed.Symbols
        |> Map.values
        |> Seq.map (fun symbol -> symbol.Name, symbol.Kind)
        |> Set.ofSeq
    Assert.Equal<Set<string * SymbolKind>>(
        set [ "f", TopLevelValue; "used", Parameter; "unused", Parameter; "g", TopLevelValue ],
        declared)
    let referred = typed.References |> Map.values |> Seq.map (fun id -> typed.Symbols.[id].Name) |> Set.ofSeq
    Assert.Equal<Set<string>>(set [ "used" ], referred)

[<Fact>]
let ``Imported names refer to the imported definition and the import`` () =
    withTempDir (fun dir ->
        let modulePath = Path.Combine(dir, "mod.nyx")
        let moduleSource = "module mod\nexport def foo = 1"
        File.WriteAllText(modulePath, moduleSource)
        let check (mainSource: string) (useText: string) (importName: string) =
            let mainPath = Path.Combine(dir, "main.nyx")
            File.WriteAllText(mainPath, mainSource)
            let result = Compiler.compileFile mainPath
            let offset = offsetOf mainSource useText 0 0
            let definition = (symbolAt (fun typed -> typed.References) result offset).Value
            Assert.Equal(TopLevelValue, definition.Kind)
            Assert.Equal(modulePath, definition.Declaration.File)
            Assert.Equal(offsetOf moduleSource "foo" 0 0, definition.Declaration.Start)
            let import = (symbolAt (fun typed -> typed.ImportReferences) result offset).Value
            Assert.Equal(ImportedModule, import.Kind)
            Assert.Equal(importName, import.Name)
            Assert.Equal(offsetOf mainSource "\"mod.nyx\"" 0 0, import.Declaration.Start)
        check "module main\nimport \"mod.nyx\" as m1\ndef value = m1.foo" "m1.foo" "m1"
        check "module main\nimport \"mod.nyx\"\ndef value = foo" "foo" "mod.nyx"
        // A standard-library definition is declared in the library's own module.
        let source = "import nanyx/text as t\ndef shout = t.toUpper(\"hi\")"
        let toUpper = (symbolAt (fun typed -> typed.References) (Compiler.compile source) (offsetOf source "t.toUpper" 0 2)).Value
        Assert.Equal("nanyx/text.nyx", toUpper.Declaration.File))
