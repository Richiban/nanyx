module TranspilerWasmTests.Run

open System
open System.Diagnostics
open System.IO
open Xunit

/// What `nanyx run <file>` produced.
type private RunOutcome =
    { ExitCode: int
      StandardOutput: string
      StandardError: string }

/// Runs the real CLI (built next to the tests through the project reference) in a child process, so the
/// program's stdout, the diagnostics on stderr and the exit code are all observed exactly as a user would.
let private nanyxRun (inputPath: string) : RunOutcome =
    let cliPath = Path.Combine(AppContext.BaseDirectory, "nanyx.dll")
    if not (File.Exists cliPath) then
        failwith $"The nanyx CLI was not found at {cliPath}"
    let startInfo = ProcessStartInfo("dotnet", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false)
    for argument in [ cliPath; "run"; inputPath ] do
        startInfo.ArgumentList.Add argument
    use proc = Process.Start startInfo
    let stdout = proc.StandardOutput.ReadToEndAsync()
    let stderr = proc.StandardError.ReadToEndAsync()
    proc.WaitForExit()
    { ExitCode = proc.ExitCode
      StandardOutput = stdout.Result
      StandardError = stderr.Result }

let private runE2E testName =
    nanyxRun (Path.Combine(__SOURCE_DIRECTORY__, testName, "input.nyx"))

let private runSource (source: string) : RunOutcome =
    let inputPath = Path.Combine(Path.GetTempPath(), $"nanyx-run-{Guid.NewGuid():N}.nyx")
    File.WriteAllText(inputPath, source)
    try nanyxRun inputPath
    finally File.Delete inputPath

let private assertRan (expectedOutput: string) (outcome: RunOutcome) =
    Assert.True((outcome.ExitCode = 0), $"Expected exit code 0, got {outcome.ExitCode}. stderr:\n{outcome.StandardError}")
    Assert.Equal(expectedOutput, outcome.StandardOutput)
    Assert.Equal("", outcome.StandardError)

[<Fact>]
let ``Run - script hello world prints the greeting`` () =
    runE2E "twentythree" |> assertRan "Hello, Nanyx!\n"

[<Fact>]
let ``Run - project hello world runs the entry point`` () =
    runE2E "twentyfour" |> assertRan "Hello, World!\n"

[<Fact>]
let ``Run - dbg prints integers, strings and tags in order`` () =
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def main = {\n" +
        "  dbg(42)\n" +
        "  dbg(-7)\n" +
        "  dbg(\"text\")\n" +
        "  dbg(#ok)\n" +
        "  dbg(#some(5))\n" +
        "  println(\"done\")\n" +
        "}\n" +
        "\n" +
        "main()\n"
    runSource source |> assertRan "dbg: 42\ndbg: -7\ndbg: text\ndbg: #ok\ndbg: #some(5)\ndone\n"

[<Fact>]
let ``Run - console print and println write to stdout`` () =
    let source =
        "import nanyx/console\n" +
        "\n" +
        "print(\"a\")\n" +
        "print(\"b\")\n" +
        "println(\"\")\n" +
        "println(\"c\")\n"
    runSource source |> assertRan "ab\nc\n"

[<Fact>]
let ``Run - a function requiring Console gets the runtime's instance`` () =
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def greet: [Console] string -> () = { name -> println(\"Hello, \" ++ name) }\n" +
        "\n" +
        "greet(\"Nanyx\")\n"
    runSource source |> assertRan "Hello, Nanyx\n"

[<Fact>]
let ``Run - a use binding overrides the runtime's Console`` () =
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def greet: [Console] string -> () = { name -> println(name) }\n" +
        "\n" +
        "def main = {\n" +
        "  use (println = { s -> print(\"[\" ++ s ++ \"]\") }, print = { s -> print(s) })\n" +
        "  greet(\"quiet\")\n" +
        "}\n" +
        "\n" +
        "main()\n" +
        "greet(\"loud\")\n"
    runSource source |> assertRan "[quiet]loud\n"

[<Fact>]
let ``Run - string concatenation and equality`` () =
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def name = \"Nanyx\"\n" +
        "println(\"Hello, \" ++ name ++ \"!\")\n" +
        "println(if name == \"Nanyx\" then \"same\" else \"different\")\n" +
        "println(if name != \"Nanyx\" then \"different\" else \"same\")\n" +
        "println(if \"a\" ++ \"b\" == \"ab\" then \"concat works\" else \"concat broken\")\n"
    runSource source |> assertRan "Hello, Nanyx!\nsame\nsame\nconcat works\n"

[<Fact>]
let ``Run - text upper-cases and prints`` () =
    let source =
        "import nanyx/console\n" +
        "import nanyx/text\n" +
        "\n" +
        "println(toUpper(\"hello, nanyx!\"))\n" +
        "\"shout\" \\toUpper \\println\n"
    runSource source |> assertRan "HELLO, NANYX!\nSHOUT\n"

[<Fact>]
let ``Run - text length, toLower, trim, repeat and substring`` () =
    let source =
        "import nanyx/console\n" +
        "import nanyx/text\n" +
        "\n" +
        "dbg(length(\"\"))\n" +
        "dbg(length(\"hello\"))\n" +
        "println(toLower(\"Hello, NANYX!\"))\n" +
        "println(\"[\" ++ trim(\"  padded \") ++ \"]\")\n" +
        "println(\"[\" ++ trim(\"   \") ++ \"]\")\n" +
        "println(repeat(\"ab\", 3))\n" +
        "println(\"[\" ++ repeat(\"ab\", 0) ++ \"]\")\n" +
        "println(substring(\"nanyx\", 1, 3))\n" +
        "println(substring(\"nanyx\", 3, 10))\n"
    runSource source |> assertRan "dbg: 0\ndbg: 5\nhello, nanyx!\n[padded]\n[]\nababab\n[]\nany\nyx\n"

[<Fact>]
let ``Run - text startsWith, endsWith and contains`` () =
    let source =
        "import nanyx/console\n" +
        "import nanyx/text\n" +
        "\n" +
        "def show: bool -> () = { b -> println(if b then \"yes\" else \"no\") }\n" +
        "show(startsWith(\"nanyx\", \"nan\"))\n" +
        "show(startsWith(\"nanyx\", \"nyx\"))\n" +
        "show(startsWith(\"nan\", \"nanyx\"))\n" +
        "show(endsWith(\"nanyx\", \"nyx\"))\n" +
        "show(endsWith(\"nanyx\", \"nan\"))\n" +
        "show(contains(\"hello world\", \"o w\"))\n" +
        "show(contains(\"hello world\", \"xyz\"))\n" +
        "show(contains(\"hello\", \"\"))\n"
    runSource source |> assertRan "yes\nno\nno\nyes\nno\nyes\nno\nyes\n"

[<Fact>]
let ``Run - text split, join and concat`` () =
    let source =
        "import nanyx/console\n" +
        "import nanyx/text\n" +
        "\n" +
        "def parts = split(\"a,b,,c\", \",\")\n" +
        "println(join(parts, \" | \"))\n" +
        "println(join(split(\"no separators\", \"-\"), \"+\"))\n" +
        "println(join(split(\"a--b\", \"--\"), \"/\"))\n" +
        "println(\"[\" ++ join([], \", \") ++ \"]\")\n" +
        "println(concat([\"Hello\", \", \", \"World\"]))\n" +
        "println(join(split(\"x\", \"\"), \"?\"))\n"
    runSource source |> assertRan "a | b |  | c\nno separators\na/b\n[]\nHello, World\nx\n"

[<Fact>]
let ``Run - list literals and patterns`` () =
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def describe: list(int) -> string = { xs ->\n" +
        "  match xs\n" +
        "  | [] -> \"empty\"\n" +
        "  | [x] -> \"one\"\n" +
        "  | [1, y] -> \"one then another\"\n" +
        "  | [x, ...rest] -> \"many\"\n" +
        "}\n" +
        "def sum: list(int) -> int = { xs ->\n" +
        "  match xs\n" +
        "  | [] -> 0\n" +
        "  | [x, ...rest] -> x + sum(rest)\n" +
        "}\n" +
        "println(describe([]))\n" +
        "println(describe([7]))\n" +
        "println(describe([1, 2]))\n" +
        "println(describe([2, 2, 2]))\n" +
        "dbg(sum([1, 2, 3, 4]))\n"
    runSource source |> assertRan "empty\none\none then another\nmany\ndbg: 10\n"

[<Fact>]
let ``Run - list spread in expressions`` () =
    // Regression: `[1, ...rest]` worked as a pattern but not as an expression (#44).
    let source =
        "import nanyx/console\n" +
        "import nanyx/text\n" +
        "\n" +
        "def sum: list(int) -> int = {\n" +
        "  | [] -> 0\n" +
        "  | [head, ...tail] -> head + sum(tail)\n" +
        "}\n" +
        "@entry\n" +
        "def main = {\n" +
        "  def xs = [1, ...[2, 3]]\n" +
        "  println(\"{sum(xs)}\")\n" +
        "  def names = [\"b\"]\n" +
        "  println(join([\"a\", ...names, \"c\", ...[], ...[\"d\"]], \"-\"))\n" +
        "}\n"
    runSource source |> assertRan "6\na-b-c-d\n"

[<Fact>]
let ``Run - multi-argument pattern lambdas`` () =
    // The two-parameter `{ | [], _ -> ... | [head, ...tail], f -> ... }` form from the pattern-matching page (#45).
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def map: (list(int), (int -> int)) -> list(int) = {\n" +
        "  | [], _ -> []\n" +
        "  | [head, ...tail], f -> [f(head), ...map(tail, f)]\n" +
        "}\n" +
        "def sum: list(int) -> int = {\n" +
        "  | [] -> 0\n" +
        "  | [head, ...tail] -> head + sum(tail)\n" +
        "}\n" +
        "@entry\n" +
        "def main = {\n" +
        "  println(\"{sum(map([1, 2, 3], { * 10 }))}\")\n" +
        "}\n"
    runSource source |> assertRan "60\n"

[<Fact>]
let ``Run - ++ joins lists`` () =
    let source =
        "import nanyx/console\n" +
        "import nanyx/text\n" +
        "\n" +
        "def front = [\"a\", \"b\"]\n" +
        "println(join(front ++ [\"c\"] ++ [], \"-\"))\n" +
        "println(join([] ++ front, \"-\"))\n" +
        "dbg(length(\"ab\" ++ \"cd\" ++ \"e\"))\n"
    runSource source |> assertRan "a-b-c\na-b\ndbg: 5\n"

[<Fact>]
let ``Run - an annotated zero-parameter function returning a tag union`` () =
    // The `() -> ...` annotation names a unit input for a lambda with no parameters; the backend used to
    // crash with a negative padding count instead of compiling it.
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def getMessage: () -> #some(string) | #noMessage = {\n" +
        "  def message = \"Hello, from Nanyx!\"\n" +
        "  #some(message)\n" +
        "}\n" +
        "\n" +
        "@entry\n" +
        "def main = {\n" +
        "  match getMessage()\n" +
        "    | #some(_) -> println(\"There was a message\")\n" +
        "    | #noMessage -> println(\"No message available\")\n" +
        "}\n"
    runSource source |> assertRan "There was a message\n"

[<Fact>]
let ``Run - a function annotated as returning a function is reported as unsupported, not a crash`` () =
    // `int -> (int -> int)` is one parameter returning a function. Closures are not supported yet, so this
    // must fail with the backend's own diagnostic rather than the negative-count exception it used to throw.
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def makeAdder: int -> (int -> int) = { x -> { y -> x + y } }\n" +
        "\n" +
        "@entry\n" +
        "def main = {\n" +
        "  def add5 = makeAdder(5)\n" +
        "  dbg(add5(10))\n" +
        "}\n"
    let outcome = runSource source
    Assert.Equal(1, outcome.ExitCode)
    Assert.Contains("Unsupported", outcome.StandardError)
    Assert.DoesNotContain("non-negative", outcome.StandardError)

[<Fact>]
let ``Run - interpolating variables bound by match patterns`` () =
    // Regression: the backend did not know the type of a pattern-bound name, so `"{n}"` inside a
    // match arm failed with "Unable to determine interpolated value type" (#35).
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def describe: #some(int) | #none -> string = { v ->\n" +
        "  match v\n" +
        "    | #some(n) -> \"some {n}\"\n" +
        "    | #none -> \"none\"\n" +
        "}\n" +
        "def greet: #named(string) | #anon -> string = { v ->\n" +
        "  match v\n" +
        "    | #named(name) -> \"hello {name}\"\n" +
        "    | #anon -> \"hello stranger\"\n" +
        "}\n" +
        "def first: list(int) -> string = { xs ->\n" +
        "  match xs\n" +
        "    | [] -> \"empty\"\n" +
        "    | [x, ...rest] -> \"first {x}\"\n" +
        "}\n" +
        "println(describe(#some(3)) ++ \", \" ++ describe(#none))\n" +
        "println(greet(#named(\"Ada\")) ++ \", \" ++ greet(#anon))\n" +
        "println(first([7, 8]) ++ \", \" ++ first([]))\n"
    runSource source |> assertRan "some 3, none\nhello Ada, hello stranger\nfirst 7, empty\n"
[<Fact>]
let ``Run - interpolation at the top level of a script`` () =
    // Regression: the script start function used to omit the interpolation scratch locals (#48).
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def x = 1 + 1\n" +
        "println(\"script {x}\")\n" +
        "println(\"{x * 10} again\")\n"
    runSource source |> assertRan "script 2\n20 again\n"

[<Fact>]
let ``Run - string literals inside interpolation holes`` () =
    // Regression: literals nested in `{...}` were not collected into the data section (#49).
    let source =
        "import nanyx/console\n" +
        "import nanyx/text\n" +
        "\n" +
        "def main = {\n" +
        "  println(\"len {length(\"abc\")} and {toUpper(\"x\")}\")\n" +
        "}\n" +
        "\n" +
        "main()\n"
    runSource source |> assertRan "len 3 and X\n"

[<Fact>]
let ``Run - interpolation holes that allocate or have effects run once`` () =
    // Regression: holes used to be evaluated twice, and an allocating hole in the second pass
    // overwrote the output buffer, so `"a {s ++ "!"} c"` printed just `b!`.
    let source =
        "import nanyx/console\n" +
        "import nanyx/text\n" +
        "\n" +
        "def s = \"b\"\n" +
        "def shout: string -> string = { v ->\n" +
        "  println(\"shouting \" ++ v)\n" +
        "  toUpper(v)\n" +
        "}\n" +
        "println(\"a {s ++ \"!\"} c {toUpper(s)} d\")\n" +
        "println(\"[{shout(\"x\")}]\")\n"
    runSource source |> assertRan "a b! c B d\nshouting x\n[X]\n"

[<Fact>]
let ``Run - importing an unknown standard library module is a diagnostic`` () =
    let outcome = runSource "import nanyx/sockets\n\ndbg(1)\n"
    Assert.Equal(1, outcome.ExitCode)
    Assert.Equal("", outcome.StandardOutput)
    Assert.Contains("Unknown standard library module 'nanyx/sockets'", outcome.StandardError)

[<Fact>]
let ``Run - a compile error exits 1 with the diagnostic on stderr`` () =
    let outcome = runSource "import nanyx/console\n\nprintln(42)\n"
    Assert.Equal(1, outcome.ExitCode)
    Assert.Equal("", outcome.StandardOutput)
    Assert.Contains("[error]", outcome.StandardError)
    Assert.Contains("Type mismatch", outcome.StandardError)

[<Fact>]
let ``Run - matching on string literals`` () =
    // Regression: `| "Sat" -> ...` failed with "Unsupported match pattern" (#32).
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def kind: string -> string = { day ->\n" +
        "  match day\n" +
        "    | \"Sat\" -> \"weekend\"\n" +
        "    | \"Sun\" -> \"weekend\"\n" +
        "    | _ -> \"weekday\"\n" +
        "}\n" +
        "def greet: #name(string) | #anon -> string = { who ->\n" +
        "  match who\n" +
        "    | #name(\"Ada\") -> \"hello, Ada!\"\n" +
        "    | #name(_) -> \"hello, someone\"\n" +
        "    | #anon -> \"hello, stranger\"\n" +
        "}\n" +
        "def suffix = \"t\"\n" +
        "println(kind(\"Sat\") ++ \" \" ++ kind(\"Sun\") ++ \" \" ++ kind(\"Mon\"))\n" +
        "println(kind(\"Sa\" ++ suffix))\n" +
        "println(greet(#name(\"Ada\")) ++ \" / \" ++ greet(#name(\"Bob\")) ++ \" / \" ++ greet(#anon))\n"
    runSource source |> assertRan "weekend weekend weekday\nweekend\nhello, Ada! / hello, someone / hello, stranger\n"
[<Fact>]
let ``Run - tuple patterns in match arms`` () =
    // Regression: `| (0, _) -> ...` failed with "Unsupported match pattern" (#33).
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def describe: (int, int) -> string = { p ->\n" +
        "  match p\n" +
        "    | (0, _) -> \"x zero\"\n" +
        "    | (_, 0) -> \"y zero\"\n" +
        "    | _ -> \"other\"\n" +
        "}\n" +
        "def swapped: (int, int) -> string = { p ->\n" +
        "  match p\n" +
        "    | (a, b) -> \"{b} {a}\"\n" +
        "}\n" +
        "def firstName: list((string, int)) -> string = { people ->\n" +
        "  match people\n" +
        "    | [(name, _), ...rest] -> name\n" +
        "    | [] -> \"nobody\"\n" +
        "}\n" +
        "@entry\n" +
        "def main = {\n" +
        "  println(describe((0, 1)) ++ \" \" ++ describe((1, 0)) ++ \" \" ++ describe((1, 1)))\n" +
        "  println(swapped((1, 2)))\n" +
        "  println(firstName([(\"Ada\", 36), (\"Grace\", 45)]) ++ \" \" ++ firstName([]))\n" +
        "}\n"
    runSource source |> assertRan "x zero y zero other\n2 1\nAda nobody\n"

[<Fact>]
let ``Run - identifier patterns bind the scrutinee`` () =
    // Regression: `| n -> ...` and `| 0, y -> ...` failed with "Unsupported identifier" (#34).
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def echo: int -> int = { v ->\n" +
        "  match v\n" +
        "    | 0 -> 100\n" +
        "    | n -> n + 1\n" +
        "}\n" +
        "def pick: (int, int) -> int = { a, b ->\n" +
        "  match a, b\n" +
        "    | 0, y -> y\n" +
        "    | x, _ -> x\n" +
        "}\n" +
        "def label: string -> string = { s ->\n" +
        "  match s\n" +
        "    | text -> \"<\" ++ text ++ \">\"\n" +
        "}\n" +
        "dbg(echo(0))\n" +
        "dbg(echo(41))\n" +
        "dbg(pick(0, 5))\n" +
        "dbg(pick(7, 5))\n" +
        "println(label(\"x\"))\n"
    runSource source |> assertRan "dbg: 100\ndbg: 42\ndbg: 5\ndbg: 7\n<x>\n"
[<Fact>]
let ``Run - mutually recursive top-level functions`` () =
    // Regression: a def could not call one declared later in the file (#47).
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def isEven: int -> bool = { n -> if n == 0 then true else isOdd(n - 1) }\n" +
        "def isOdd: int -> bool = { n -> if n == 0 then false else isEven(n - 1) }\n" +
        "def show: bool -> () = { b -> println(if b then \"yes\" else \"no\") }\n" +
        "show(isEven(10))\n" +
        "show(isOdd(7))\n" +
        "show(isEven(7))\n" +
        "dbg(later(2))\n" +
        "def later = { x -> x * 21 }\n"
    runSource source |> assertRan "yes\nyes\nno\ndbg: 42\n"
[<Fact>]
let ``Run - tuple destructuring with def`` () =
    // Regression: `def (x, y) = point` was a parse error (#41).
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def (a, b) = (1, 2)\n" +
        "@entry\n" +
        "def main = {\n" +
        "  def point = (10, 20)\n" +
        "  def (x, y) = point\n" +
        "  def (_, (name, age)) = (0, (\"Ada\", 36))\n" +
        "  println(\"{x},{y}\")\n" +
        "  println(\"{a}+{b}={a + b}\")\n" +
        "  println(name ++ \" {age}\")\n" +
        "}\n"
    runSource source |> assertRan "10,20\n1+2=3\nAda 36\n"

[<Fact>]
let ``Run - with record update`` () =
    // Regression: `alice with age = 31` was a parse error (#40).
    let source =
        "import nanyx/console\n" +
        "\n" +
        "type Point = (x: int, y: int)\n" +
        "def shiftedX: Point -> int = { p -> (p with x = p.x + 1).x }\n" +
        "@entry\n" +
        "def main = {\n" +
        "  def alice = (name = \"Alice\", age = 30)\n" +
        "  def older = alice with age = 31\n" +
        "  def renamed = older with name = \"Al\" ++ \"ice\", age = older.age + 1\n" +
        "  println(\"{older.age} {alice.age} {renamed.name} {renamed.age}\")\n" +
        "  println(\"{shiftedX(Point(x = 3, y = 4))}\")\n" +
        "}\n"
    runSource source |> assertRan "31 30 Alice 32\n4\n"

[<Fact>]
let ``Run - member access on the result of a call`` () =
    // Regression: `shift(...)` returned a Point but `q.x` on it was "Record 'q' has no member 'x'" (#60).
    let source =
        "import nanyx/console\n" +
        "\n" +
        "type Point = (x: int, y: int)\n" +
        "def shift: Point -> Point = { q -> Point(x = q.x + 1, y = q.y + 1) }\n" +
        "def pair: (string, string) -> (first: string, second: string) = { a, b -> (first = a, second = b) }\n" +
        "@entry\n" +
        "def main = {\n" +
        "  def q = shift(Point(x = 3, y = 4))\n" +
        "  println(\"{q.x} {q.y} {shift(q).x}\")\n" +
        "  def r = shift(shift(q))\n" +
        "  println(\"{r.x} {r.y}\")\n" +
        "  println(pair(\"a\", \"b\").second ++ pair(\"c\", \"d\").first)\n" +
        "}\n"
    runSource source |> assertRan "4 5 5\n6 7\nbc\n"

[<Fact>]
let ``Run - nested record field access`` () =
    // Regression: `line.start.x` failed with "cannot determine layout for .x" (#30).
    let source =
        "import nanyx/console\n" +
        "\n" +
        "type Point = (x: int, y: int)\n" +
        "def origin = (x = 0, y = 0)\n" +
        "def unit = Point(x = 1, y = 1)\n" +
        "def main = {\n" +
        "  def line = (start = (x = 1, y = 2), finish = (x = 4, y = 6))\n" +
        "  def deep = (outer = (inner = (label = \"deep\")))\n" +
        "  dbg(line.finish.x - line.start.x)\n" +
        "  dbg(line.finish.y - line.start.y)\n" +
        "  println(deep.outer.inner.label)\n" +
        "  dbg(origin.x + origin.y)\n" +
        "  dbg(unit.x + unit.y)\n" +
        "}\n" +
        "main()\n"
    // The last line covers #31: a record built through a type constructor at the top level.
    runSource source |> assertRan "dbg: 3\ndbg: 4\ndeep\ndbg: 0\ndbg: 2\n"
[<Fact>]
let ``Run - float arithmetic and printing`` () =
    // Regression: float literals parsed but `pi * 2.0` was "Type mismatch: float vs int" and the backend had
    // no f64 support at all (#36). Floats are boxed f64 values.
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def half: float -> float = { x -> x / 2.0 }\n" +
        "@entry\n" +
        "def main = {\n" +
        "  def pi = 3.14\n" +
        "  def d = pi * 2.0\n" +
        "  println(\"{d}\")\n" +
        "  println(\"{2.0 * 2.0} {0.0 - 1.5} {1.0 / 3.0} {half(5.0)} {1234567.25 + 0.0}\")\n" +
        "  println(if 2.5 > 1.0 and 1.5 <= 1.5 and 0.1 + 0.2 != 0.3 then \"cmp ok\" else \"cmp wrong\")\n" +
        "  dbg(1.5 + 2.25)\n" +
        "}\n"
    runSource source |> assertRan "6.28\n4.0 -1.5 0.333333 2.5 1234567.25\ncmp ok\ndbg: 3.75\n"

[<Fact>]
let ``Run - boolean keywords not, and, or`` () =
    // #39: the docs make boolean operations keywords; `not` had no spelling at all.
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def show: bool -> () = { b -> println(if b then \"yes\" else \"no\") }\n" +
        "def between: (int, int, int) -> bool = { x, lo, hi -> x >= lo and x <= hi }\n" +
        "show(not true)\n" +
        "show(not false)\n" +
        "show(true and false)\n" +
        "show(true or false)\n" +
        "show(not (1 > 2) and between(5, 1, 10))\n" +
        "show(not between(50, 1, 10) or false)\n"
    runSource source |> assertRan "no\nyes\nno\nyes\nyes\nyes\n"
[<Fact>]
let ``Run - if-matching`` () =
    // Regression: `if get(3) is #some(v) then ...` was a parse error (#42).
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def get: int -> #some(int) | #none = { n -> #some(n) }\n" +
        "def nothing: int -> #some(int) | #none = { n -> #none }\n" +
        "def describe: #some(int) | #none -> string = { r ->\n" +
        "  if r is #some(v) then \"got {v}\" else \"nothing\"\n" +
        "}\n" +
        "@entry\n" +
        "def main = {\n" +
        "  if get(3) is #some(v) then println(\"got {v}\")\n" +
        "  if nothing(0) is #some(v) then println(\"unexpected {v}\")\n" +
        "  println(describe(get(5)) ++ \" \" ++ describe(nothing(5)))\n" +
        "}\n"
    runSource source |> assertRan "got 3\ngot 5 nothing\n"
[<Fact>]
let ``Run - guard patterns in the documented form`` () =
    // Regression: the docs write guards as `| { < 0 } -> ...`; the parser only knew `| < 0 ->` (#46).
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def describe: int -> string = { n ->\n" +
        "  match n\n" +
        "    | { < 0 } -> \"negative\"\n" +
        "    | 0 -> \"zero\"\n" +
        "    | _ -> \"positive\"\n" +
        "}\n" +
        "def limit = 10\n" +
        "def size: int -> string = { n ->\n" +
        "  match n\n" +
        "    | { >= limit * 2 } -> \"huge\"\n" +
        "    | { >= limit } -> \"big\"\n" +
        "    | else -> \"small\"\n" +
        "}\n" +
        "@entry\n" +
        "def main = {\n" +
        "  println(describe(-1) ++ \" \" ++ describe(0) ++ \" \" ++ describe(5))\n" +
        "  println(size(25) ++ \" \" ++ size(10) ++ \" \" ++ size(3))\n" +
        "}\n"
    runSource source |> assertRan "negative zero positive\nhuge big small\n"

[<Fact>]
let ``Run - or-patterns in match arms`` () =
    // #43: `| a | b -> ...` is one arm matching either alternative.
    let source =
        "import nanyx/console\n" +
        "\n" +
        "type Day = #mon | #tue | #wed | #thu | #fri | #sat | #sun\n" +
        "def kind: Day -> string = { day ->\n" +
        "  match day\n" +
        "    | #sat | #sun -> \"weekend\"\n" +
        "    | _ -> \"weekday\"\n" +
        "}\n" +
        "def size: int -> string = { n ->\n" +
        "  match n\n" +
        "    | 0\n" +
        "    | 1\n" +
        "    | 2 -> \"small\"\n" +
        "    | _ -> \"big\"\n" +
        "}\n" +
        "def axis: (int, int) -> string = {\n" +
        "  | 0, _ | _, 0 -> \"axis\"\n" +
        "  | _, _ -> \"plane\"\n" +
        "}\n" +
        "println(kind(#sat) ++ \" \" ++ kind(#sun) ++ \" \" ++ kind(#wed))\n" +
        "println(size(1) ++ \" \" ++ size(2) ++ \" \" ++ size(9))\n" +
        "println(axis(0, 5) ++ \" \" ++ axis(5, 0) ++ \" \" ++ axis(3, 3))\n"
    runSource source |> assertRan "weekend weekend weekday\nsmall small big\naxis axis plane\n"
[<Fact>]
let ``Run - functions as values and higher-order calls`` () =
    // #28: a parameter holding a function can be called; a named function or a lambda can be passed.
    let source =
        "import nanyx/console\n" +
        "\n" +
        "def apply: ((int -> int), int) -> int = { f, x -> f(x) }\n" +
        "def twice: ((int -> int), int) -> int = { f, x -> f(f(x)) }\n" +
        "def double: int -> int = { x -> x * 2 }\n" +
        "def map: (list(int), (int -> int)) -> list(int) = { xs, f ->\n" +
        "  match xs\n" +
        "    | [] -> []\n" +
        "    | [head, ...tail] -> [f(head)] ++ map(tail, f)\n" +
        "}\n" +
        "def sum: list(int) -> int = { xs ->\n" +
        "  match xs\n" +
        "    | [] -> 0\n" +
        "    | [head, ...tail] -> head + sum(tail)\n" +
        "}\n" +
        "def main = {\n" +
        "  def square = { x -> x * x }\n" +
        "  dbg(apply(double, 21))\n" +
        "  dbg(apply({ x -> x + 1 }, 41))\n" +
        "  dbg(apply({ * 3 }, 14))\n" +
        "  dbg(twice(double, 10))\n" +
        "  dbg(square(6))\n" +
        "  dbg(sum(map([1, 2, 3], double)))\n" +
        "  dbg(sum([1, 2, 3] \\map { * 10 }))\n" +
        "}\n" +
        "main()\n"
    runSource source |> assertRan "dbg: 42\ndbg: 42\ndbg: 42\ndbg: 40\ndbg: 36\ndbg: 12\ndbg: 60\n"

[<Fact>]
let ``Run - a capturing lambda is still reported as unsupported`` () =
    let source =
        "def apply = { f, x -> f(x) }\n" +
        "def main = { n ->\n" +
        "  apply({ x -> x + n }, 1)\n" +
        "}\n" +
        "dbg(main(2))\n"
    let outcome = runSource source
    Assert.Equal(1, outcome.ExitCode)
    Assert.Contains("Unsupported", outcome.StandardError)

[<Fact>]
let ``Run - a match with no matching arm exits 1 with a runtime error`` () =
    let source =
        "def pick = { x ->\n" +
        "    match x\n" +
        "    | 1 -> 10\n" +
        "}\n" +
        "\n" +
        "dbg(pick(2))\n"
    let outcome = runSource source
    Assert.Equal(1, outcome.ExitCode)
    Assert.Contains("Runtime error", outcome.StandardError)
    Assert.Contains("unreachable", outcome.StandardError)
