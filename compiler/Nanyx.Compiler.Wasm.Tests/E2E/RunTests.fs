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
        "def greet: [Console] string -> () = { name -> println(\"Hello, \" + name) }\n" +
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
        "  use (println = { s -> print(\"[\" + s + \"]\") }, print = { s -> print(s) })\n" +
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
        "println(\"Hello, \" + name + \"!\")\n" +
        "println(if name == \"Nanyx\" then \"same\" else \"different\")\n" +
        "println(if name != \"Nanyx\" then \"different\" else \"same\")\n" +
        "println(if \"a\" + \"b\" == \"ab\" then \"concat works\" else \"concat broken\")\n"
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
        "println(\"[\" + trim(\"  padded \") + \"]\")\n" +
        "println(\"[\" + trim(\"   \") + \"]\")\n" +
        "println(repeat(\"ab\", 3))\n" +
        "println(\"[\" + repeat(\"ab\", 0) + \"]\")\n" +
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
        "println(\"[\" + join([], \", \") + \"]\")\n" +
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
