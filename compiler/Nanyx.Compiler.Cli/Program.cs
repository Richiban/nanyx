using Consolo;
using Microsoft.FSharp.Collections;
using NyxCompiler;
using Transpiler.Wasm;

enum Target
{
    Check,
    Wasm
}

internal static class CliCommands
{
    /// <summary>
    /// Compile a Nanyx source file.
    /// </summary>
    /// <param name="inputFile">The path to the .nyx file to compile.</param>
    /// <param name="target">The compilation target: check or wasm.</param>
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
            Console.WriteLine("Input file is required.");
            return 2;
        }

        if (!File.Exists(inputFile))
        {
            Console.WriteLine($"File not found: {inputFile}");
            return 2;
        }

        var result = Compiler.compileFile(inputFile);
        Console.WriteLine($"Phase: {result.Phase}");
        PrintDiagnostics(result.Diagnostics);

        return target switch
        {
            Target.Check => RunCheck(result),
            Target.Wasm => RunWasm(result, inputFile, outputPath),
            _ => UnsupportedTarget(target.ToString())
        };
    }

    private static void PrintDiagnostics(FSharpList<Diagnostic> diagnostics)
    {
        if (diagnostics.IsEmpty)
        {
            Console.WriteLine("No diagnostics.");
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

            Console.WriteLine($"[{severity}] {rangeText}{diagnostic.Message}");
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
        if (result.Typed is null)
        {
            return 1;
        }

        if (HasErrors(result.Diagnostics))
        {
            return 1;
        }

        try
        {
            var finalOutputPath = string.IsNullOrWhiteSpace(outputPath) ? DefaultWasmOutputPath(inputFile) : outputPath!;
            var wat = CodeGen.transpileTypedModuleToWat(result.Typed.Value);
            File.WriteAllText(finalOutputPath, wat);
            Console.WriteLine($"WASM (WAT) emitted: {finalOutputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WASM emission failed: {ex.Message}");
            return 1;
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
        Console.WriteLine($"Unsupported target: {target}");
        return 2;
    }
}
