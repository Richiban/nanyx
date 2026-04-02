module Transpiler.Wasm.Program

open System
open System.IO
open NyxCompiler
open Transpiler.Wasm.CodeGen

let private printDiagnostics (diagnostics: Diagnostic list) =
    diagnostics
    |> List.iter (fun diag ->
        let severity =
            match diag.Severity with
            | ErrorSeverity -> "error"
            | WarningSeverity -> "warning"
        printfn "[%s] %s" severity diag.Message)

[<EntryPoint>]
let main argv =
    if argv.Length = 0 then
        printfn "Usage: Nyx.Transpiler.Wasm <input.nyx> [output.wat]"
        printfn "  Transpiles a Nanyx source file to WebAssembly text format (.wat)"
        1
    else
        let inputFile = argv.[0]
        let outputFile = if argv.Length > 1 then argv.[1] else inputFile + ".wat"

        if String.IsNullOrWhiteSpace inputFile || not (File.Exists inputFile) then
            printfn "Error: Input file not found: %s" inputFile
            1
        else
            try
                let result = Compiler.compileFile inputFile
                if not result.Diagnostics.IsEmpty then
                    printDiagnostics result.Diagnostics
                    1
                else
                    match result.Typed with
                    | Some typed ->
                        let wat = transpileTypedModuleToWat typed
                        File.WriteAllText(outputFile, wat)
                        printfn "Successfully transpiled %s -> %s" inputFile outputFile
                        0
                    | None ->
                        printfn "Error: Unable to transpile without a typed module."
                        1
            with
            | ex ->
                printfn "Error: %s" ex.Message
                1
