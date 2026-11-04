using Consolo;
using Microsoft.FSharp.Collections;
using Nanyx.Runtime;
using NyxCompiler;
using Transpiler.Wasm;

enum Target
{
    Parse,
    Check,
    Wasm,
    Run
}

internal static class CliCommands
{
    /// <summary>
    /// Compile a Nanyx source file.
    /// </summary>
    /// <param name="inputFile">The path to the .nyx file to compile.</param>
    /// <param name="target">The compilation target: parse (print the AST), check, wasm (emit WebAssembly text) or run (compile and execute).</param>
    /// <param name="outputPath">Optional output path for generated artifacts.</param>
    [Consolo("")]
    public static void Run(
        Target target,
        string inputFile,
        [Consolo("out", Alias = "o")] string? outputPath = null)
    {
        Environment.ExitCode = Execute(inputFile, target, outputPath);
    }

    private static int Execute(string inputFile, Target target, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(inputFile))
        {
            Console.Error.WriteLine("Input file is required.");
            return 2;
        }

        if (!File.Exists(inputFile))
        {
            Console.Error.WriteLine($"File not found: {inputFile}");
            return 2;
        }

        if (target == Target.Parse)
        {
            return RunParse(inputFile);
        }

        var result = Compiler.compileFile(inputFile);

        // `run` is the user-facing command: its stdout belongs to the program, so compiler chatter is kept off it.
        if (target == Target.Run)
        {
            PrintDiagnostics(result.Diagnostics, Console.Error);
            return RunProgram(result);
        }

        Console.WriteLine($"Phase: {result.Phase}");
        PrintDiagnostics(result.Diagnostics, Console.Out);

        return target switch
        {
            Target.Check => RunCheck(result),
            Target.Wasm => RunWasm(result, inputFile, outputPath),
            _ => UnsupportedTarget(target.ToString())
        };
    }

    private static int RunParse(string inputFile)
    {
        var parsed = Parser.Program.parseModule(File.ReadAllText(inputFile));

        if (parsed.IsError)
        {
            Console.WriteLine($"Parse error: {parsed.ErrorValue}");
            return 1;
        }

        Console.WriteLine("Parsed successfully:");
        foreach (var item in parsed.ResultValue)
        {
            Console.WriteLine($"  {item}");
        }

        return 0;
    }

    private static void PrintDiagnostics(FSharpList<Diagnostic> diagnostics, TextWriter writer)
    {
        if (diagnostics.IsEmpty)
        {
            if (writer == Console.Out)
            {
                writer.WriteLine("No diagnostics.");
            }

            return;
        }

        foreach (var diagnostic in diagnostics)
        {
            var severity = diagnostic.Severity.ToString().Replace("Severity", string.Empty).ToLowerInvariant();
            var rangeText = string.Empty;

            if (diagnostic.Range is not null)
            {
                var range = diagnostic.Range.Value;
                rangeText = $"({range.Item1 + 1}:{range.Item2 + 1}) ";
            }

            writer.WriteLine($"[{severity}] {rangeText}{diagnostic.Message}");
        }
    }

    private static void PrintTypes(FSharpMap<string, Ty> types)
    {
        if (types.Count == 0)
        {
            Console.WriteLine("(no inferred types)");
            return;
        }

        foreach (var entry in types.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"{entry.Key} : {entry.Value}");
        }
    }

    private static int RunCheck(CompileResult result)
    {
        if (result.Typed is null)
        {
            return 1;
        }

        Console.WriteLine("Inferred types:");
        PrintTypes(result.Typed.Value.Types);
        return 0;
    }

    private static int RunWasm(CompileResult result, string inputFile, string? outputPath)
    {
        if (!TryEmitWat(result, Console.Out, out var wat))
        {
            return 1;
        }

        var finalOutputPath = string.IsNullOrWhiteSpace(outputPath) ? DefaultWasmOutputPath(inputFile) : outputPath!;
        File.WriteAllText(finalOutputPath, wat);
        Console.WriteLine($"WASM (WAT) emitted: {finalOutputPath}");
        return 0;
    }

    private static int RunProgram(CompileResult result)
    {
        if (!TryEmitWat(result, Console.Error, out var wat))
        {
            return 1;
        }

        var outcome = WasmRunner.run(wat);
        if (outcome.IsOk)
        {
            return 0;
        }

        switch (outcome.ErrorValue)
        {
            case RunError.InvalidModule invalid:
                Console.Error.WriteLine($"Internal compiler error: the generated WebAssembly is invalid: {invalid.Item}");
                return 1;
            case RunError.Trapped trapped:
                Console.Error.WriteLine($"Runtime error: {trapped.Item}");
                return 1;
            default:
                Console.Error.WriteLine($"Run failed: {outcome.ErrorValue}");
                return 1;
        }
    }

    /// <summary>Lowers a successfully type-checked program to WebAssembly text, reporting failures to <paramref name="errors"/>.</summary>
    private static bool TryEmitWat(CompileResult result, TextWriter errors, out string wat)
    {
        wat = string.Empty;

        if (result.Typed is null || HasErrors(result.Diagnostics))
        {
            return false;
        }

        try
        {
            wat = CodeGen.transpileTypedModuleToWat(result.Typed.Value);
            return true;
        }
        catch (Exception ex)
        {
            errors.WriteLine($"WASM emission failed: {ex.Message}");
            return false;
        }
    }

    private static bool HasErrors(FSharpList<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (string.Equals(diagnostic.Severity.ToString(), nameof(Severity.ErrorSeverity), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string DefaultWasmOutputPath(string inputFile)
    {
        var directory = Path.GetDirectoryName(inputFile) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(inputFile);
        return Path.Combine(directory, stem + ".wat");
    }

    private static int UnsupportedTarget(string target)
    {
        Console.Error.WriteLine($"Unsupported target: {target}");
        return 2;
    }
}
