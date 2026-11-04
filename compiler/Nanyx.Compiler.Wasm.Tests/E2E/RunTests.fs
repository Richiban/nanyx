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
