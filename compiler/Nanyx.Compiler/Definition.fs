/// Where a name an editor points at is declared. Positions are character offsets into the source as the parser
/// saw it, with line endings normalised to `\n`, as for `Hover`.
module NyxCompiler.Definition

open Parser.Program

let private contains (offset: int) (span: SourceSpan) =
    not (SourceSpan.isNone span) && span.Start <= offset && offset < span.End

/// The span of the name a referring expression is written with: a call's or pipe's function name (not its
/// arguments), or all of an identifier or qualified name.
let private nameSpan (expr: Expression) =
    match expr.Kind with
    | FunctionCall(_, nameSpan, _)
    | Pipe(_, _, nameSpan, _) -> nameSpan
    | _ -> Some expr.Span

/// The symbol for the name at `offset` in the compiled root module: what it refers to, or, on a declaration's
/// own name, the symbol it declares. `file` is the file the root module was compiled as (empty for source
/// compiled directly), which tells its own declarations from those of the modules it imports. `None` when the
/// module did not type-check or no name is at `offset`.
let symbolAt (file: string) (result: CompileResult) (offset: int) : Symbol option =
    match result.Syntax, result.Typed with
    | Some syntax, Some typed ->
        let referenced =
            Hover.expressionsAt offset syntax
            |> List.tryPick (fun expr ->
                match typed.References |> Map.tryFind expr.Id, nameSpan expr with
                | Some id, Some span when contains offset span -> Some typed.Symbols.[id]
                | _ -> None)
        match referenced with
        | Some _ -> referenced
        | None ->
            typed.Symbols
            |> Map.values
            |> Seq.filter (fun symbol -> symbol.Declaration.File = file && contains offset symbol.Declaration)
            |> Seq.sortBy (fun symbol -> symbol.Declaration.End - symbol.Declaration.Start)
            |> Seq.tryHead
    | _ -> None
