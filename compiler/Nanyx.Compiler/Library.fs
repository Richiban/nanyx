namespace NyxCompiler

open System
open System.IO
open System.Text.RegularExpressions
open Parser.Program

type CompilationPhase =
    | Parsed
    | Desugared
    | Typed
    | Lowered
    | Emitted

type CompileResult =
    { Phase: CompilationPhase
      Diagnostics: Diagnostic list
      Typed: TypedModule option }

/// Where a module's source came from, which decides how its imports are resolved and what is in scope.
type private ModuleOrigin =
    /// A file on disk: imports by path are relative to its directory.
    | SourceFile of fullPath: string
    /// A module of the embedded standard library, checkedModule with the compiler intrinsics in scope.
    | StandardLibraryModule of name: string
    /// Source given directly (e.g. by tests): only standard-library imports can be resolved.
    | InMemory

/// A type-checkedModule module together with what an importer needs from it.
type private CheckedModule =
    { Typed: TypedModule
      /// The module's own items (imports removed), as the backend will see them.
      Own: Module
      /// Items inlined from the modules it imports, already renamed and ready for the backend.
      Inlined: TopLevelItem list
      /// The declared module name, if any.
      Name: string option }

module Compiler =
    let private normalizeSource (source: string) =
        source.Replace("\r\n", "\n").Replace("\r", "\n")

    let private tryFindIdentifierRange (source: string) (identifier: string) : (int * int) option =
        let normalized = normalizeSource source
        let lines = normalized.Split('\n')
        let mutable found: (int * int) option = None
        let mutable index = 0
        while found.IsNone && index < lines.Length do
            let pos = lines.[index].IndexOf(identifier, StringComparison.Ordinal)
            if pos >= 0 then
                found <- Some (index, pos)
            index <- index + 1
        found

    let private extractQuotedIdentifiers (message: string) : string list =
        let matches = Regex.Matches(message, "'([^']+)'", RegexOptions.None)
        [ for m in matches do
            if m.Groups.Count > 1 then
                m.Groups.[1].Value ]

    let private annotateDiagnosticsWithSource (source: string) (diagnostics: Diagnostic list) =
        diagnostics
        |> List.map (fun diag ->
            match diag.Range with
            | Some _ -> diag
            | None ->
                let candidates = extractQuotedIdentifiers diag.Message
                let found =
                    candidates
                    |> List.tryPick (fun identifier -> tryFindIdentifierRange source identifier)
                match found with
                | Some (line, col) -> { diag with Range = Some (line, col) }
                | None -> diag)
    let private sanitizeIdentifier (name: string) =
        let sanitized =
            name
            |> Seq.map (fun c -> if Char.IsLetterOrDigit c || c = '_' then c else '_')
            |> Seq.toArray
            |> String
        if String.IsNullOrWhiteSpace sanitized then
            "_"
        elif Char.IsDigit sanitized.[0] then
            "_" + sanitized
        else
            sanitized

    let private importQualifier (item: ImportItem) =
        item.Alias |> Option.map sanitizeIdentifier

    let private resolveImportPath (baseDir: string) (item: ImportItem) : Result<string, Diagnostic> =
        let rawPath =
            match item.Source with
            | PathImport path ->
                if Path.IsPathRooted path then path else Path.Combine(baseDir, path)
            | ModuleImport name ->
                let relative =
                    name.Replace(".", string Path.DirectorySeparatorChar)
                        .Replace("/", string Path.DirectorySeparatorChar)
                Path.Combine(baseDir, relative + ".nyx")
        let fullPath =
            if Path.GetExtension rawPath = "" then rawPath + ".nyx" else rawPath
        if File.Exists fullPath then
            Ok (Path.GetFullPath fullPath)
        else
            Error (Diagnostics.error ($"Import not found: {rawPath}"))

    let private collectImports (module': Module) =
        module'
        |> List.choose (function
            | Import items -> Some items
            | _ -> None)
        |> List.collect id

    let private removeImportStatements (module': Module) =
        module'
        |> List.filter (function
            | Import _ -> false
            | _ -> true)

    let private declaredModuleName (module': Module) =
        module'
        |> List.tryPick (function
            | ModuleDecl name -> Some name
            | _ -> None)

    /// Renames every reference to a name in `renames` throughout an expression. Used when a module is
    /// inlined into its importer, so that its definitions keep pointing at each other under their new names.
    /// (Lambda parameters and local definitions that shadow a renamed name are not accounted for.)
    let private renameReferencesInExpr (renames: Map<string, string>) (expr: Expression) : Expression =
        let rename (name: string) = renames |> Map.tryFind name |> Option.defaultValue name
        let rec mapExpr (current: Expression) : Expression =
            { current with Kind = mapKind current }
        and mapKind current =
            match current.Kind with
            | IdentifierExpr name -> IdentifierExpr(rename name)
            | FunctionCall(name, rangeOpt, args) ->
                FunctionCall(rename name, rangeOpt, args |> List.map mapExpr)
            | Lambda(parameters, body) ->
                Lambda(parameters, mapExpr body)
            | BinaryOp(op, left, right) ->
                BinaryOp(op, mapExpr left, mapExpr right)
            | Pipe(value, name, rangeOpt, args) ->
                Pipe(mapExpr value, rename name, rangeOpt, args |> List.map mapExpr)
            | Block statements ->
                Block(statements |> List.map mapStatement)
            | Match(values, arms) ->
                let mappedValues = values |> List.map mapExpr
                let mappedArms = arms |> List.map (fun (patterns, armExpr) -> patterns, mapExpr armExpr)
                Match(mappedValues, mappedArms)
            | TupleExpr items -> TupleExpr(items |> List.map mapExpr)
            | RecordExpr fields ->
                RecordExpr(
                    fields
                    |> List.map (function
                        | NamedField(name, value) -> NamedField(name, mapExpr value)
                        | PositionalField value -> PositionalField(mapExpr value)))
            | ListExpr items -> ListExpr(items |> List.map mapExpr)
            | TagExpr(tagName, payloadOpt) -> TagExpr(tagName, payloadOpt |> Option.map mapExpr)
            | IfExpr(cond, thenExpr, elseExpr) ->
                IfExpr(mapExpr cond, mapExpr thenExpr, mapExpr elseExpr)
            | MemberAccess(baseExpr, fieldName, rangeOpt) -> MemberAccess(mapExpr baseExpr, fieldName, rangeOpt)
            | ContextMemberCall(ctxType, memberName, args) ->
                ContextMemberCall(ctxType, memberName, args |> List.map mapExpr)
            | UseIn(binding, body) ->
                UseIn(mapBinding binding, mapExpr body)
            | InterpolatedString parts ->
                InterpolatedString(
                    parts
                    |> List.map (function
                        | StringText text -> StringText text
                        | StringExpr value -> StringExpr(mapExpr value)))
            | WorkflowBindExpr(keyword, value) -> WorkflowBindExpr(keyword, mapExpr value)
            | WorkflowReturnExpr value -> WorkflowReturnExpr(mapExpr value)
            | ContextMemberAccess _
            | UnitExpr
            | LiteralExpr _ -> current.Kind
        and mapBinding binding =
            match binding with
            | UseValue valueExpr -> UseValue(mapExpr valueExpr)
            | UseContextInstance(ctxType, valueExpr) -> UseContextInstance(ctxType, mapExpr valueExpr)
        and mapStatement statement =
            match statement with
            | DefStatement(isExport, name, typeOpt, value) -> DefStatement(isExport, name, typeOpt, mapExpr value)
            | ExprStatement value -> ExprStatement(mapExpr value)
            | UseStatement binding -> UseStatement(mapBinding binding)
            | ImportStatement _
            | TypeDefStatement _ -> statement
        mapExpr expr

    /// The items an importer inlines for the backend from a module it imports: the module's own value
    /// definitions and type definitions (plus whatever that module inlined itself). Exported definitions keep
    /// their names, or take the import's alias as a qualifier; private ones are qualified with the module's own
    /// name so they cannot collide with anything in the importer.
    let private inlinedItemsForImport (item: ImportItem) (checkedModule: CheckedModule) : TopLevelItem list =
        let qualifier = importQualifier item
        let moduleName =
            checkedModule.Name
            |> Option.map sanitizeIdentifier
            |> Option.defaultValue "import"
        let renames =
            checkedModule.Own
            |> List.choose (function
                | Def (ValueDef(true, name, _, _)) ->
                    qualifier |> Option.map (fun q -> name, $"{q}.{name}")
                | Def (ValueDef(false, name, _, _)) -> Some (name, $"{moduleName}.{name}")
                | _ -> None)
            |> Map.ofList
        let rename (name: string) = renames |> Map.tryFind name |> Option.defaultValue name
        let ownItems =
            checkedModule.Own
            |> List.choose (function
                | Def (ValueDef(_, name, typeOpt, expr)) ->
                    Some (Def (ValueDef(false, rename name, typeOpt, renameReferencesInExpr renames expr)))
                | Def (TypeDef _) as typeDef -> Some typeDef
                | _ -> None)
        checkedModule.Inlined @ ownItems

    /// Keeps the first definition of each name, so a module inlined along two import paths appears once.
    let private distinctDefinitions (items: TopLevelItem list) : TopLevelItem list =
        let seen = System.Collections.Generic.HashSet<string>()
        items
        |> List.filter (function
            | Def (ValueDef(_, name, _, _)) -> seen.Add("value:" + name)
            | Def (TypeDef(name, _, _, _)) -> seen.Add("type:" + name)
            | _ -> true)

    let private exportedValueNames (module': Module) =
        module'
        |> List.choose (function
            | Def (ValueDef(true, name, _, _)) -> Some name
            | _ -> None)
        |> Set.ofList

    let private exportedTypeNames (module': Module) =
        module'
        |> List.choose (function
            | Def (TypeDef(name, modifiers, _, _)) when modifiers |> List.contains Export -> Some name
            | _ -> None)
        |> Set.ofList

    let private mergeTypeDefs (target: Map<string, TypeDefInfo>) (incoming: Map<string, TypeDefInfo>) =
        incoming
        |> Map.fold (fun acc key value ->
            if Map.containsKey key acc then acc else acc |> Map.add key value) target

    let private extendEnvWithImport (env: TypeEnv) (qualifierOpt: string option) (types: Map<string, Ty>) =
        types
        |> Map.fold (fun acc name ty ->
            let importedName =
                match qualifierOpt with
                | Some qualifier -> $"{qualifier}.{name}"
                | None -> name
            acc |> TypeEnv.extend importedName (TypeEnv.mono ty)) env

    let private tryParseLineColumn (message: string) : (int * int) option =
        let patterns =
            [
                @"Ln:\s*(\d+)\s+Col:\s*(\d+)"
                @"line\s+(\d+)\s+column\s+(\d+)"
                @"line\s+(\d+),\s*column\s+(\d+)"
            ]
        patterns
        |> List.tryPick (fun pattern ->
            let m = Regex.Match(message, pattern, RegexOptions.IgnoreCase)
            if m.Success then
                let line = int m.Groups.[1].Value - 1
                let col = int m.Groups.[2].Value - 1
                Some (max 0 line, max 0 col)
            else
                None)

    let private parseWithDiagnostics (filePath: string) (source: string) : Result<Module, Diagnostic list> =
        match parseModuleFile filePath source with
        | Result.Ok ast -> Ok ast
        | Result.Error err ->
            match tryParseLineColumn err with
            | Some (line, col) -> Error [ Diagnostics.errorAt err (line, col) ]
            | None -> Error [ Diagnostics.error err ]

    let desugar (module': Module) =
        let isReturnKeyword keyword = keyword = "return"

        let rec desugarExpr (expr: Expression) : Expression =
            { expr with Kind = desugarKind expr }
        and desugarKind expr =
            match expr.Kind with
            | FunctionCall(name, rangeOpt, args) ->
                FunctionCall(name, rangeOpt, args |> List.map desugarExpr)
            | Lambda(args, body) ->
                Lambda(args, desugarExpr body)
            | BinaryOp(op, left, right) ->
                BinaryOp(op, desugarExpr left, desugarExpr right)
            | Pipe(value, name, rangeOpt, args) ->
                Pipe(desugarExpr value, name, rangeOpt, args |> List.map desugarExpr)
            | Block statements ->
                Block(desugarBlock statements)
            | Match(exprs, arms) ->
                let mappedExprs = exprs |> List.map desugarExpr
                let mappedArms = arms |> List.map (fun (patterns, expr) -> patterns, desugarExpr expr)
                Match(mappedExprs, mappedArms)
            | TupleExpr items -> TupleExpr(items |> List.map desugarExpr)
            | RecordExpr fields ->
                let mappedFields =
                    fields
                    |> List.map (function
                        | NamedField(name, value) -> NamedField(name, desugarExpr value)
                        | PositionalField value -> PositionalField(desugarExpr value))
                RecordExpr mappedFields
            | ListExpr items -> ListExpr(items |> List.map desugarExpr)
            | TagExpr(name, payload) -> TagExpr(name, payload |> Option.map desugarExpr)
            | IfExpr(cond, thenExpr, elseExpr) ->
                IfExpr(desugarExpr cond, desugarExpr thenExpr, desugarExpr elseExpr)
            | MemberAccess(baseExpr, field, rangeOpt) -> MemberAccess(desugarExpr baseExpr, field, rangeOpt)
            | ContextMemberAccess(ctxType, field) -> ContextMemberAccess(ctxType, field)
            | ContextMemberCall(ctxType, field, args) -> ContextMemberCall(ctxType, field, args |> List.map desugarExpr)
            | UseIn(binding, body) ->
                let binding' = desugarUseBinding binding
                UseIn(binding', desugarExpr body)
            | WorkflowBindExpr(keyword, value) ->
                if isReturnKeyword keyword then
                    FunctionCall("pure", None, [desugarExpr value])
                else
                    FunctionCall(keyword, None, [desugarExpr value])
            | WorkflowReturnExpr value -> FunctionCall("pure", None, [desugarExpr value])
            | InterpolatedString parts ->
                let mappedParts =
                    parts
                    |> List.map (function
                        | StringText text -> StringText text
                        | StringExpr expr -> StringExpr (desugarExpr expr))
                InterpolatedString mappedParts
            | UnitExpr
            | LiteralExpr _
            | IdentifierExpr _ -> expr.Kind
        and desugarUseBinding binding =
            match binding with
            | UseValue expr -> UseValue (desugarExpr expr)
            | UseContextInstance(ctxType, expr) -> UseContextInstance(ctxType, desugarExpr expr)
        and desugarStatement stmt =
            match stmt with
            | DefStatement(isExport, name, typeOpt, expr) ->
                let expr' =
                    match expr.Kind with
                    | WorkflowBindExpr(keyword, value) when isReturnKeyword keyword ->
                        { expr with Kind = FunctionCall("pure", None, [desugarExpr value]) }
                    | _ -> desugarExpr expr
                DefStatement(isExport, name, typeOpt, expr')
            | ExprStatement expr -> ExprStatement (desugarExpr expr)
            | UseStatement binding -> UseStatement (desugarUseBinding binding)
            | ImportStatement _
            | TypeDefStatement _ -> stmt
        and desugarWorkflowChain statements =
            match statements with
            | [] -> Expression.synthetic UnitExpr
            | DefStatement(isExport, name, typeOpt, expr) :: rest ->
                match expr.Kind with
                | WorkflowBindExpr(keyword, bindExpr) when isReturnKeyword keyword ->
                    { expr with Kind = FunctionCall("pure", None, [desugarExpr bindExpr]) }
                | WorkflowBindExpr(keyword, bindExpr) ->
                    let nextExpr = desugarWorkflowChain rest
                    { expr with Kind = FunctionCall(keyword, None, [desugarExpr bindExpr; Expression.from expr (Lambda([name, None], nextExpr))]) }
                | _ ->
                    let expr' = desugarExpr expr
                    let nextExpr = desugarWorkflowChain rest
                    chainBlock expr.Span nextExpr [DefStatement(isExport, name, typeOpt, expr'); ExprStatement nextExpr]
            | ExprStatement expr :: rest ->
                match expr.Kind with
                | WorkflowReturnExpr value -> { expr with Kind = FunctionCall("pure", None, [desugarExpr value]) }
                | WorkflowBindExpr(keyword, bindExpr) when isReturnKeyword keyword ->
                    { expr with Kind = FunctionCall("pure", None, [desugarExpr bindExpr]) }
                | WorkflowBindExpr(keyword, bindExpr) ->
                    let nextExpr = desugarWorkflowChain rest
                    { expr with Kind = FunctionCall(keyword, None, [desugarExpr bindExpr; Expression.from expr (Lambda([("_", None)], nextExpr))]) }
                | _ ->
                    let expr' = desugarExpr expr
                    let nextExpr = desugarWorkflowChain rest
                    chainBlock expr.Span nextExpr [ExprStatement expr'; ExprStatement nextExpr]
            | UseStatement binding :: rest ->
                let binding' = desugarUseBinding binding
                let nextExpr = desugarWorkflowChain rest
                let bound = match binding with UseValue expr | UseContextInstance(_, expr) -> expr
                chainBlock bound.Span nextExpr [UseStatement binding'; ExprStatement nextExpr]
            | statement :: rest ->
                let nextExpr = desugarWorkflowChain rest
                chainBlock SourceSpan.none nextExpr [desugarStatement statement; ExprStatement nextExpr]
        // A block made up for a statement and the rest of the chain spans from the statement to the end of the
        // rest. The rest is a made-up `()` with no span when the statement is the last one, and a statement
        // without an expression (an import, a type) has no span yet.
        and chainBlock (statementSpan: SourceSpan) (nextExpr: Expression) statements =
            let span =
                if SourceSpan.isNone statementSpan then nextExpr.Span
                elif SourceSpan.isNone nextExpr.Span then statementSpan
                else SourceSpan.between statementSpan nextExpr.Span
            Expression.at span (Block statements)
        and desugarBlock statements =
            let rec split prefix remaining =
                match remaining with
                | [] -> List.rev prefix
                | (DefStatement(_, _, _, Kind (WorkflowBindExpr _))) :: _
                | (ExprStatement (Kind (WorkflowBindExpr _))) :: _
                | (ExprStatement (Kind (WorkflowReturnExpr _))) :: _ ->
                    let chainExpr = desugarWorkflowChain remaining
                    List.rev prefix @ [ExprStatement chainExpr]
                | stmt :: rest ->
                    split (desugarStatement stmt :: prefix) rest
            split [] statements

        module'
        |> List.map (function
            | Def (ValueDef(isExport, name, typeOpt, expr)) ->
                Def (ValueDef(isExport, name, typeOpt, desugarExpr expr))
            | Expr expr -> Expr (desugarExpr expr)
            | other -> other)

    /// The attribute that marks a definition as the program's entry point: `@entry def main = { ... }`.
    let entryAttribute = "entry"

    /// The standard-library module the entry point runs inside: `@entry` is called by the runtime with its
    /// `Console` instance, so the module that declares the entry point sees `nanyx/console` without importing it.
    let entryContextModule = StandardLibrary.importPrefix + "console"

    let private importsModule (moduleName: string) (module': Module) =
        collectImports module'
        |> List.exists (fun item ->
            match item.Source with
            | ModuleImport name -> name = moduleName
            | PathImport _ -> false)

    /// Resolves `@attribute` lines. `@entry` on a definition appends a call to that definition at the end
    /// of the module, so it runs as the program's start function, and imports `nanyx/console` for the module
    /// (the context the runtime supplies to the entry point); any other attribute, or an attribute that does
    /// not precede a value definition, is an error.
    let applyAttributes (module': Module) : Result<Module, Diagnostic list> =
        let rec loop (remaining: TopLevelItem list) (acc: TopLevelItem list) (entryCalls: TopLevelItem list) (errors: Diagnostic list) =
            match remaining with
            | [] -> List.rev acc, List.rev entryCalls, List.rev errors
            | Attribute name :: rest when name <> entryAttribute ->
                loop rest acc entryCalls (Diagnostics.error $"Unknown attribute '@{name}'" :: errors)
            | Attribute _ :: (Def (ValueDef(_, name, _, value)) as def) :: rest ->
                loop rest (def :: acc) (Expr (Expression.from value (FunctionCall(name, None, []))) :: entryCalls) errors
            | Attribute name :: rest ->
                loop rest acc entryCalls (Diagnostics.error $"Attribute '@{name}' must be followed by a definition" :: errors)
            | item :: rest -> loop rest (item :: acc) entryCalls errors
        match loop module' [] [] [] with
        | items, entryCalls, [] ->
            let entryImport =
                if not entryCalls.IsEmpty && not (importsModule entryContextModule items) then
                    [ Import [ { Source = ModuleImport entryContextModule; Alias = None } ] ]
                else
                    []
            Ok (entryImport @ items @ entryCalls)
        | _, _, errors -> Error errors

    /// Applies the substitution found by unification to everything inference produced.
    let private resolveTypes (subst: Subst) (types: Map<string, Ty>) (typeDefs: Map<string, TypeDefInfo>) (items: TypedTopLevelItem list) =
        let resolvedTypes = types |> Map.map (fun _ ty -> Unifier.apply subst ty)
        let resolvedTypeDefs =
            typeDefs
            |> Map.map (fun _ info -> { info with Underlying = Unifier.apply subst info.Underlying })
        let rec applyTypedExpr (typedExpr: TypedExpr) =
            { typedExpr with
                Type = Unifier.apply subst typedExpr.Type
                Body = typedExpr.Body |> Option.map applyTypedExpr
                Statements = typedExpr.Statements |> Option.map (List.map applyTypedStatement)
                MatchArms = typedExpr.MatchArms |> Option.map (List.map applyTypedMatchArm) }
        and applyTypedStatement statement =
            match statement with
            | TypedDefStatement(isExport, name, typeOpt, expr) ->
                TypedDefStatement(isExport, name, typeOpt, applyTypedExpr expr)
            | TypedExprStatement expr -> TypedExprStatement (applyTypedExpr expr)
            | TypedUseStatement(binding, exprOpt) ->
                TypedUseStatement(binding, exprOpt |> Option.map applyTypedExpr)
            | TypedImportStatement _
            | TypedTypeDefStatement _ -> statement
        and applyTypedMatchArm (patterns, expr) =
            let mappedPatterns =
                patterns |> List.map (fun pattern -> { pattern with Type = Unifier.apply subst pattern.Type })
            (mappedPatterns, applyTypedExpr expr)
        let resolvedItems =
            items
            |> List.map (fun item ->
                match item with
                | TypedExprItem expr -> TypedExprItem (applyTypedExpr expr)
                | TypedDef (TypedValueDef(isExport, name, typeOpt, expr)) ->
                    TypedDef (TypedValueDef(isExport, name, typeOpt, applyTypedExpr expr))
                | TypedDef (TypedTypeDef _)
                | TypedImport _
                | TypedModuleDecl _ -> item)
        resolvedTypes, resolvedTypeDefs, resolvedItems

    /// The module as the backend will see it: the typed items turned back into syntax (so that, for
    /// example, pipes to attached functions carry their resolved names), without import statements.
    let private moduleFromTypedItems (items: TypedTopLevelItem list) : Module =
        items
        |> List.choose (function
            | TypedModuleDecl name -> Some (ModuleDecl name)
            | TypedImport _ -> None
            | TypedDef (TypedValueDef(isExport, name, typeOpt, expr)) ->
                Some (Def (ValueDef(isExport, name, typeOpt, expr.Expr)))
            | TypedDef (TypedTypeDef(name, modifiers, parameters, body)) ->
                Some (Def (TypeDef(name, modifiers, parameters, body)))
            | TypedExprItem expr -> Some (Expr expr.Expr))

    let private originKey (origin: ModuleOrigin) =
        match origin with
        | SourceFile fullPath -> fullPath
        | StandardLibraryModule name -> StandardLibrary.importPrefix + name
        | InMemory -> "<source>"

    /// The file a module's syntax nodes are reported in.
    let private sourceFileName (origin: ModuleOrigin) =
        match origin with
        | SourceFile fullPath -> fullPath
        | StandardLibraryModule name -> StandardLibrary.importPrefix + name + ".nyx"
        | InMemory -> ""

    /// Parses, desugars and type-checks one module, resolving and checking the modules it imports first. An
    /// error says which phase it came from.
    let rec private typecheckModule (origin: ModuleOrigin) (source: string) (visited: Set<string>) : Result<CheckedModule, CompilationPhase * Diagnostic list> =
        let key = originKey origin
        if visited.Contains key then
            Error (Typed, [ Diagnostics.error ($"Import cycle detected for {key}") ])
        else
            let resolved =
                parseWithDiagnostics (sourceFileName origin) source
                |> Result.mapError (fun diagnostics -> Parsed, diagnostics)
                |> Result.bind (fun ast ->
                    applyAttributes (desugar ast)
                    |> Result.mapError (fun diagnostics -> Desugared, diagnostics))
            match resolved with
            | Result.Error failure -> Error failure
            | Result.Ok desugared ->
                let imports = collectImports desugared
                let mutable env =
                    match origin with
                    | StandardLibraryModule _ -> Intrinsics.typeEnv
                    | SourceFile _
                    | InMemory -> TypeEnv.empty
                let mutable state = NyxCompiler.Infer.emptyState
                let mutable errors: Diagnostic list = []
                let mutable inlined: TopLevelItem list = []
                let mergeImport (item: ImportItem) (imported: CheckedModule) =
                    let qualifier = importQualifier item
                    let exportedValues = exportedValueNames imported.Own
                    let exportedTypes = exportedTypeNames imported.Own
                    let exportedValueTypes =
                        imported.Typed.Types |> Map.filter (fun name _ -> exportedValues.Contains name)
                    let exportedTypeDefs =
                        imported.Typed.TypeDefs |> Map.filter (fun name _ -> exportedTypes.Contains name)
                    env <- extendEnvWithImport env qualifier exportedValueTypes
                    state <- { state with TypeDefs = mergeTypeDefs state.TypeDefs exportedTypeDefs }
                    inlined <- inlined @ inlinedItemsForImport item imported
                for item in imports do
                    match StandardLibrary.tryModuleName item with
                    | Some moduleName ->
                        match StandardLibrary.tryLoadSource moduleName with
                        | None ->
                            let available = String.concat ", " (StandardLibrary.moduleNames |> List.map (fun name -> StandardLibrary.importPrefix + name))
                            errors <- errors @ [ Diagnostics.error ($"Unknown standard library module '{StandardLibrary.importPrefix}{moduleName}' (available: {available})") ]
                        | Some moduleSource ->
                            match typecheckModule (StandardLibraryModule moduleName) moduleSource (visited.Add key) with
                            | Error (_, diags) -> errors <- errors @ diags
                            | Ok imported when imported.Name <> Some moduleName ->
                                let declared = imported.Name |> Option.defaultValue "(none)"
                                errors <- errors @ [ Diagnostics.error ($"Standard library module '{StandardLibrary.importPrefix}{moduleName}' declares the module name '{declared}'") ]
                            | Ok imported -> mergeImport item imported
                    | None ->
                        match origin with
                        | SourceFile fullPath ->
                            match resolveImportPath (Path.GetDirectoryName fullPath) item with
                            | Error diag -> errors <- errors @ [ diag ]
                            | Ok importPath ->
                                match typecheckModule (SourceFile importPath) (File.ReadAllText importPath) (visited.Add key) with
                                | Error (_, diags) -> errors <- errors @ diags
                                | Ok imported -> mergeImport item imported
                        | StandardLibraryModule name ->
                            errors <- errors @ [ Diagnostics.error ($"Standard library module '{name}' may only import other standard library modules") ]
                        | InMemory ->
                            let shown =
                                match item.Source with
                                | PathImport path -> path
                                | ModuleImport name -> name
                            errors <- errors @ [ Diagnostics.error ($"Import not found: {shown} (only standard library modules can be imported here)") ]
                if not errors.IsEmpty then
                    Error (Typed, errors)
                else
                    match NyxCompiler.Infer.inferModuleWithEnv env state desugared with
                    | Error diagnostics -> Error (Typed, diagnostics)
                    | Ok (types, items, (state: NyxCompiler.Infer.InferState)) ->
                        match Unifier.unify state.Constraints with
                        | Error message -> Error (Typed, [ Diagnostics.error message ])
                        | Ok subst ->
                            let resolvedTypes, resolvedTypeDefs, resolvedItems = resolveTypes subst types state.TypeDefs items
                            let own = moduleFromTypedItems resolvedItems
                            // The module's own definitions win over imported ones of the same name.
                            let ownNames = exportedValueNames own |> Set.union (own |> List.choose (function Def (ValueDef(false, name, _, _)) -> Some name | _ -> None) |> Set.ofList)
                            let inlined =
                                inlined
                                |> List.filter (function
                                    | Def (ValueDef(_, name, _, _)) -> not (ownNames.Contains name)
                                    | _ -> true)
                                |> distinctDefinitions
                            Ok { Typed =
                                    { Module = inlined @ own
                                      Types = resolvedTypes
                                      TypeDefs = resolvedTypeDefs
                                      Items = resolvedItems }
                                 Own = own
                                 Inlined = inlined
                                 Name = declaredModuleName desugared }

    let parse (source: string) : Result<Module, Diagnostic list> =
        match parseModule source with
        | Result.Ok ast -> Ok ast
        | Result.Error err -> Error [ Diagnostics.error err ]

    let private compileWith (origin: ModuleOrigin) (source: string) : CompileResult =
        match typecheckModule origin source Set.empty with
        | Result.Error (phase, diagnostics) ->
            { Phase = phase
              Diagnostics = annotateDiagnosticsWithSource source diagnostics
              Typed = None }
        | Result.Ok checkedModule ->
            { Phase = Typed
              Diagnostics = []
              Typed = Some checkedModule.Typed }

    /// Compiles the file at `filePath` up to the typed module, resolving its imports (standard-library modules
    /// from the embedded library, everything else relative to the file).
    let compileFile (filePath: string) : CompileResult =
        if not (File.Exists filePath) then
            { Phase = Parsed
              Diagnostics = [ Diagnostics.error ($"File not found: {filePath}") ]
              Typed = None }
        else
            compileWith (SourceFile (Path.GetFullPath filePath)) (File.ReadAllText filePath)

    /// Compiles `source` as if it were the contents of the file at `filePath` (which need not exist, or may hold
    /// older text: an editor's unsaved buffer, say). Imports are resolved exactly as `compileFile` resolves them.
    let compileSource (filePath: string) (source: string) : CompileResult =
        compileWith (SourceFile (Path.GetFullPath filePath)) source

    /// Compiles source given directly, up to the typed module. Standard-library imports are resolved; imports by
    /// path cannot be, since there is no file for them to be relative to.
    let compile (source: string) : CompileResult =
        compileWith InMemory source
