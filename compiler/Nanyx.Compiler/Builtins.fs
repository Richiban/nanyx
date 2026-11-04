namespace NyxCompiler

open Parser.Program

/// Functions and standard-library modules that the compiler knows about without any user declaration.
///
/// This is a stop-gap until there is a real standard library: `import nanyx/console` is accepted and
/// `println` is always in scope with the type `string -> unit`, whether or not the import is present.
module Builtins =
    /// The standard-library module prefix in import paths, e.g. `import nanyx/console`.
    let standardLibraryPrefix = "nanyx/"

    /// Standard-library modules that may be imported. None of them currently contributes bindings;
    /// the functions they describe are built in (see `typeEnv`).
    let standardLibraryModules = set [ "nanyx/console" ]

    /// The name of the standard-library module an import refers to, if it refers to one at all.
    let tryStandardLibraryModule (item: ImportItem) : string option =
        match item.Source with
        | ModuleImport name when name.StartsWith standardLibraryPrefix -> Some name
        | _ -> None

    let isStandardLibraryModule (name: string) = standardLibraryModules.Contains name

    /// Writes a string followed by a newline to standard output.
    let printlnName = "println"

    let printlnType = TyFunc(TyPrimitive "string", TyPrimitive "unit")

    /// The type environment every module is checked in.
    let typeEnv : TypeEnv =
        TypeEnv.empty
        |> TypeEnv.extend printlnName (TypeEnv.mono printlnType)

    /// Names of the members of every record or context type the module declares.
    let private declaredMemberNames (module': Module) : Set<string> =
        module'
        |> List.collect (function
            | Def (TypeDef(_, _, _, TypeRecord fields)) ->
                fields
                |> List.map (function
                    | TypeField(name, _, _, _, _) -> name
                    | TypeMember(name, _) -> name)
            | _ -> [])
        |> Set.ofList

    /// The type environment for a particular module: a built-in is shadowed when the module declares a
    /// context member of the same name (for example `context Console = (println: string -> ())`), so
    /// that member must then be brought into scope with `use` like any other.
    let typeEnvFor (module': Module) : TypeEnv =
        let declared = declaredMemberNames module'
        typeEnv |> Map.filter (fun name _ -> not (declared.Contains name))
