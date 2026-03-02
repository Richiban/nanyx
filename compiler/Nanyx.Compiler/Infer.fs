module NyxCompiler.Infer

open System
open Parser.Program
open NyxCompiler

type TypedExpr = NyxCompiler.TypedExpr
type TypedStatement = NyxCompiler.TypedStatement

type InferState =
        { NextId: int;
            Constraints: ConstraintSet;
            TypeVars: Map<string, TyVar>;
            TypeDefs: Map<string, TypeDefInfo>;
            ContextDefs: Map<string, TypeExpr>;
            ContextRequirements: Map<string, TypeExpr list>;
            LocalNominals: Set<string>;
            LocalPrivateNominals: Set<string> }

let emptyState =
        { NextId = 0;
            Constraints = [];
            TypeVars = Map.empty;
            TypeDefs = Map.empty;
            ContextDefs = Map.empty;
            ContextRequirements = Map.empty;
            LocalNominals = Set.empty;
            LocalPrivateNominals = Set.empty }

let private freshVar (state: InferState) =
    let var = { Id = state.NextId; Name = None }
    TyVar var, { state with NextId = state.NextId + 1 }

let private addConstraint left right (state: InferState) =
    { state with Constraints = (left, right, Equal) :: state.Constraints }

let private addAssignableConstraint left right (state: InferState) =
    { state with Constraints = (left, right, Assignable) :: state.Constraints }

let private primitiveType name =
    TyPrimitive name

let private literalType literal =
    match literal with
    | StringLit _ -> primitiveType "string"
    | IntLit _ -> primitiveType "int"
    | FloatLit _ -> primitiveType "float"
    | BoolLit _ -> primitiveType "bool"

let private listType elementTy =
    TyApply("list", [ elementTy ])

let private numericType =
    TyPrimitive "int"

let private tupleFieldName index =
    string index

let rec private isAssignableType (expected: Ty) (actual: Ty) =
    match expected, actual with
    | TyVar _, _ -> true
    | TyPrimitive left, TyPrimitive right -> left = right
    | TyApply(leftName, leftArgs), TyApply(rightName, rightArgs) ->
        leftName = rightName
        && leftArgs.Length = rightArgs.Length
        && List.forall2 isAssignableType leftArgs rightArgs
    | TyApply _, TyVar _ -> true
    | TyNominal(_, underlying, _), _ -> isAssignableType underlying actual
    | _, TyNominal(_, underlying, _) -> isAssignableType expected underlying
    | TyRecord expectedFields, TyRecord actualFields ->
        expectedFields
        |> Map.forall (fun name expectedTy ->
            match actualFields |> Map.tryFind name with
            | Some actualTy -> isAssignableType expectedTy actualTy
            | None -> false)
    | TyUnion options, _ -> options |> List.exists (fun optionTy -> isAssignableType optionTy actual)
    | _ -> true

let private ensureAttachedTypeExists (state: InferState) (currentModuleOpt: string option) (name: Identifier) : Result<unit, Diagnostic list> =
    let firstDot = name.IndexOf(".")
    if firstDot <= 0 then
        Ok ()
    else
        let lastDot = name.LastIndexOf(".")
        let candidateTypeName = name.Substring(0, lastDot)
        if state.TypeDefs.ContainsKey candidateTypeName then
            Ok ()
        else
            if firstDot = lastDot then
                match currentModuleOpt with
                | Some currentModule when currentModule = candidateTypeName -> Ok ()
                | _ -> Error [ Diagnostics.error ($"Unknown attached type '{candidateTypeName}' for definition '{name}'") ]
            else
                Ok ()

let private matchesAttachedInput (inputTy: Ty) (lhsType: Ty) : bool =
    let rec containsNominal name ty =
        match ty with
        | TyNominal(nominalName, _, _) -> nominalName = name
        | TyUnion options -> options |> List.exists (containsNominal name)
        | _ -> false
    match inputTy with
    | TyNominal(name, _, _) ->
        match lhsType with
        | TyNominal(lhsName, _, _) -> lhsName = name
        | TyUnion options -> options |> List.exists (containsNominal name)
        | TyVar _ -> true
        | _ -> false
    | _ ->
        match lhsType with
        | TyNominal _ -> false
        | _ -> isAssignableType inputTy lhsType

let private tryResolveAttachedFunction (env: TypeEnv) (lhsType: Ty) (funcName: string) (rangeOpt: (int * int) option) : Result<string option, Diagnostic list> =
    if funcName.Contains(".") then
        Ok None
    else
        let suffix = $".{funcName}"
        let candidates =
            env
            |> Map.toList
            |> List.choose (fun (name, scheme) ->
                if name.EndsWith(suffix) then
                    match scheme.Type with
                    | TyFunc(inputTy, _) when matchesAttachedInput inputTy lhsType -> Some name
                    | _ -> None
                else
                    None)
        match candidates with
        | [] -> Ok None
        | [single] -> Ok (Some single)
        | _ ->
            let names = String.concat ", " candidates
            match rangeOpt with
            | Some (line, col) -> Error [ Diagnostics.errorAt ($"Ambiguous attached function '{funcName}': {names}") (line, col) ]
            | None -> Error [ Diagnostics.error ($"Ambiguous attached function '{funcName}': {names}") ]

let private tryInferAttachedLambdaInput (state: InferState) (name: Identifier) (expr: Expression) : Ty option =
    match expr with
    | Lambda(args, _) when name.Contains(".") && args.Length = 1 ->
        let dotIndex = name.LastIndexOf(".")
        let typeName = name.Substring(0, dotIndex)
        match state.TypeDefs |> Map.tryFind typeName with
        | Some info when typeName.StartsWith("@") -> Some (TyNominal(typeName, info.Underlying, info.IsPrivate))
        | Some info -> Some info.Underlying
        | None -> None
    | _ -> None

let private isContextDef (modifiers: TypeDefModifier list) =
    modifiers |> List.contains Context

let private tupleRecordType (items: Ty list) =
    items
    |> List.mapi (fun index ty -> tupleFieldName index, ty)
    |> Map.ofList
    |> TyRecord

let private inputTypeFromArgs argTys =
    match argTys with
    | [] -> TyPrimitive "unit"
    | [ single ] -> single
    | _ -> tupleRecordType argTys

let private isPrivateType modifiers =
    modifiers |> List.exists (fun modifier -> modifier = TypeDefModifier.Private)


let private lookupPrimitive (name: string) =
    match name with
    | "int" | "Int" -> Some (primitiveType "int")
    | "float" | "Float" -> Some (primitiveType "float")
    | "bool" | "Bool" -> Some (primitiveType "bool")
    | "string" | "String" -> Some (primitiveType "string")
    | "unit" | "Unit" -> Some (primitiveType "unit")
    | _ -> None

let rec private typeExprToTy (state: InferState) (typeExpr: TypeExpr) : Ty * InferState =
    match typeExpr with
    | TypeName name ->
        match lookupPrimitive name with
        | Some prim -> prim, state
        | None ->
            match state.TypeDefs |> Map.tryFind name with
            | Some info when name.StartsWith("@") -> TyNominal(name, info.Underlying, info.IsPrivate), state
            | Some info -> info.Underlying, state
            | None -> TyPrimitive name, state
    | TypeVar name ->
        match state.TypeVars |> Map.tryFind name with
        | Some existing -> TyVar existing, state
        | None ->
            let tyVar, next = freshVar state
            match tyVar with
            | TyVar var -> tyVar, { next with TypeVars = next.TypeVars |> Map.add name var }
            | _ -> tyVar, next
    | TypeUnit -> TyPrimitive "unit", state
    | TypeTuple items ->
        let mutable current = state
        let tys =
            items
            |> List.map (fun item ->
                let ty, next = typeExprToTy current item
                current <- next
                ty)
        tupleRecordType tys, current
    | TypeRecord fields ->
        let mutable current = state
        let fieldMap =
            fields
            |> List.choose (function
                | TypeField(name, _, _, typeOpt, _) -> Some (name, typeOpt)
                | TypeMember(name, typeOpt) -> Some (name, typeOpt))
            |> List.map (fun (name, typeOpt) ->
                let ty, next =
                    match typeOpt with
                    | Some t -> typeExprToTy current t
                    | None -> freshVar current
                current <- next
                name, ty)
            |> Map.ofList
        TyRecord fieldMap, current
    | TypeApply(name, args) ->
        let mutable current = state
        let argTys =
            args
            |> List.map (fun arg ->
                let ty, next = typeExprToTy current arg
                current <- next
                ty)
        if name.Equals("list", StringComparison.OrdinalIgnoreCase) then
            let elementTy =
                match argTys with
                | [] -> TyPrimitive "unit"
                | [single] -> single
                | multiple -> tupleRecordType multiple
            TyApply("list", [elementTy]), current
        else
            match current.TypeDefs |> Map.tryFind name with
            | Some info when not (name.StartsWith("@")) ->
                if info.Parameters.Length = argTys.Length then
                    let substitutions =
                        (info.Parameters, argTys)
                        ||> List.zip
                        |> List.fold (fun acc (paramVar, argTy) -> acc |> Map.add paramVar.Id argTy) Map.empty
                    Unifier.apply substitutions info.Underlying, current
                else
                    TyApply(name, argTys), current
            | _ -> TyApply(name, argTys), current
    | TypeFunc(arg, ret) ->
        let argTy, next = typeExprToTy state arg
        let retTy, nextState = typeExprToTy next ret
        TyFunc(argTy, retTy), nextState
    | TypeTag(name, payloadOpt) ->
        match payloadOpt with
        | Some payload ->
            let payloadTy, next = typeExprToTy state payload
            TyTag(name, Some payloadTy), next
        | None -> TyTag(name, None), state
    | TypeUnion items ->
        let mutable current = state
        let tys =
            items
            |> List.map (fun item ->
                let ty, next = typeExprToTy current item
                current <- next
                ty)
        TyUnion tys, current
    | TypeIntersection items ->
        let mutable current = state
        let tys =
            items
            |> List.map (fun item ->
                let ty, next = typeExprToTy current item
                current <- next
                ty)
        TyUnion tys, current
    | TypeOptional inner ->
        let innerTy, next = typeExprToTy state inner
        TyUnion [ innerTy; TyTag("none", None) ], next
    | TypeContext _ ->
        let ty, next = freshVar state
        ty, next
    | TypeWithContext(_, inner) ->
        typeExprToTy state inner
    | TypeConstraint(_, baseType, _) ->
        typeExprToTy state baseType
    | TypeWhere(baseType, _) ->
        typeExprToTy state baseType

let private extractContextFromTypeExpr (typeExpr: TypeExpr option) : TypeExpr list option * TypeExpr option =
    match typeExpr with
    | Some (TypeWithContext(contexts, inner)) -> Some contexts, Some inner
    | Some (TypeContext contexts) -> Some contexts, None
    | Some other -> None, Some other
    | None -> None, None

let private extendEnvWithRecordFields (env: TypeEnv) (ty: Ty) =
    let addFields fields =
        fields
        |> Map.fold (fun acc name fieldTy -> TypeEnv.extend name (TypeEnv.mono fieldTy) acc) env
    match ty with
    | TyRecord fields -> addFields fields
    | TyNominal(_, underlying, _) ->
        match underlying with
        | TyRecord fields -> addFields fields
        | _ -> env
    | _ -> env

let rec private collectContextMembers (state: InferState) (ctxExpr: TypeExpr) : (Identifier * Ty) list * InferState =
    match ctxExpr with
    | TypeRecord fields ->
        let mutable current = state
        let members =
            fields
            |> List.choose (function
                | TypeField(name, _, _, typeOpt, _) -> Some (name, typeOpt)
                | TypeMember(name, typeOpt) -> Some (name, typeOpt))
            |> List.map (fun (name, typeOpt) ->
                let ty, next =
                    match typeOpt with
                    | Some t -> typeExprToTy current t
                    | None -> freshVar current
                current <- next
                name, ty)
        members, current
    | TypeContext contexts ->
        let mutable current = state
        let members =
            contexts
            |> List.collect (fun item ->
                let itemMembers, next = collectContextMembers current item
                current <- next
                itemMembers)
        members, current
    | TypeName name
    | TypeApply(name, _) ->
        match state.ContextDefs |> Map.tryFind name with
        | Some ctxDef -> collectContextMembers state ctxDef
        | None -> 
            // Also check TypeDefs for non-context types used as contexts
            match state.TypeDefs |> Map.tryFind name with
            | Some info ->
                match info.Underlying with
                | TyRecord fields -> 
                    fields |> Map.toList, state
                | _ -> [], state
            | None -> [], state
    | _ -> [], state

let private extendEnvWithContexts (env: TypeEnv) (state: InferState) (contexts: TypeExpr list) =
    let mutable current = state
    let mutable env' = env
    for ctx in contexts do
        let members, next = collectContextMembers current ctx
        current <- next
        env' <- members |> List.fold (fun acc (name, ty) -> TypeEnv.extend name (TypeEnv.mono ty) acc) env'
    env', current

let private registerContextRequirement (state: InferState) (name: Identifier) (contexts: TypeExpr list) =
    { state with ContextRequirements = state.ContextRequirements |> Map.add name contexts }

let private ensureContextAvailable (env: TypeEnv) (state: InferState) (name: Identifier) (contexts: TypeExpr list) (rangeOpt: SourceRange option) =
    let requiredMembers =
        contexts
        |> List.collect (fun ctx ->
            let members, _ = collectContextMembers state ctx
            members |> List.map fst)
    let missing =
        requiredMembers
        |> List.filter (fun memberName -> TypeEnv.tryFind memberName env |> Option.isNone)
        |> List.distinct
    if missing.IsEmpty then
        Ok ()
    else
        let missingText = String.concat ", " missing
        match rangeOpt with
        | Some (line, col) -> Error [ Diagnostics.errorAt ($"Missing context members for '{name}': {missingText}") (line, col) ]
        | None -> Error [ Diagnostics.error ($"Missing context members for '{name}': {missingText}") ]

let private registerTypeDef (state: InferState) (name: Identifier) (modifiers: TypeDefModifier list) (parameters: (Identifier * TypeExpr option) list) (body: TypeExpr) =
    let originalTypeVars = state.TypeVars
    let mutable current = state
    let paramEntries =
        parameters
        |> List.map (fun (paramName, _) ->
            let tyVar, next = freshVar current
            current <- next
            match tyVar with
            | TyVar var -> paramName, var
            | _ -> failwith "Expected TyVar")
    let updatedTypeVars =
        paramEntries
        |> List.fold (fun acc (paramName, var) -> acc |> Map.add paramName var) current.TypeVars
    let stateWithParams = { current with TypeVars = updatedTypeVars }
    let underlyingTy, next = typeExprToTy stateWithParams body
    let isPrivate = isPrivateType modifiers
    let paramVars = paramEntries |> List.map snd
    let nextNominals =
        if name.StartsWith("@") then next.LocalNominals |> Set.add name else next.LocalNominals
    let nextPrivate =
        if name.StartsWith("@") && isPrivate then next.LocalPrivateNominals |> Set.add name else next.LocalPrivateNominals
    let nextContexts =
        if isContextDef modifiers then next.ContextDefs |> Map.add name body else next.ContextDefs
    { next with
        TypeVars = originalTypeVars
        TypeDefs = next.TypeDefs |> Map.add name { Parameters = paramVars; Underlying = underlyingTy; IsPrivate = isPrivate }
        ContextDefs = nextContexts
        LocalNominals = nextNominals
        LocalPrivateNominals = nextPrivate }

let private instantiate (state: InferState) (scheme: TypeScheme) : Ty * InferState =
    if scheme.Quantified.IsEmpty then
        scheme.Type, state
    else
        let mutable current = state
        let substitutions =
            scheme.Quantified
            |> Seq.fold (fun subst id ->
                let tyVar, next = freshVar current
                current <- next
                subst |> Map.add id tyVar) Map.empty
        Unifier.apply substitutions scheme.Type, current

let private mkTypedExpr expr ty body statements matchArms : TypedExpr =
    { Expr = expr; Type = ty; Body = body; Statements = statements; MatchArms = matchArms }

let private argTypesFromExpected (state: InferState) (args: (Identifier * TypeExpr option) list) (expectedInput: Ty) =
    let mutable current = state
    let argTys =
        match expectedInput with
        | TyRecord _ when args.Length = 1 -> [ expectedInput ]
        | TyRecord fields ->
            args
            |> List.mapi (fun index (name, _) ->
                match fields |> Map.tryFind name with
                | Some ty -> ty
                | None ->
                    let tupleName = tupleFieldName index
                    match fields |> Map.tryFind tupleName with
                    | Some ty -> ty
                    | None ->
                        let ty, next = freshVar current
                        current <- next
                        ty)
        | _ when args.Length = 1 -> [ expectedInput ]
        | _ ->
            args
            |> List.map (fun _ ->
                let ty, next = freshVar current
                current <- next
                ty)
    argTys, current


let rec private inferExpr (env: TypeEnv) (state: InferState) (expr: Expression) : Result<TypedExpr * InferState, Diagnostic list> =
    match expr with
    | LiteralExpr literal ->
        Ok (mkTypedExpr expr (literalType literal) None None None, state)
    | InterpolatedString parts ->
        let mutable current = state
        let mutable errors: Diagnostic list = []
        for part in parts do
            match part with
            | StringText _ -> ()
            | StringExpr inner ->
                match inferExpr env current inner with
                | Ok (_, next) -> current <- next
                | Error err -> errors <- errors @ err
        if errors.IsEmpty then
            Ok (mkTypedExpr expr (TyPrimitive "string") None None None, current)
        else
            Error errors
    | UnitExpr ->
        Ok (mkTypedExpr expr (TyPrimitive "unit") None None None, state)
    | IdentifierExpr(name, rangeOpt) ->
        match TypeEnv.tryFind name env with
        | Some scheme ->
            let ty, next = instantiate state scheme
            Ok (mkTypedExpr expr ty None None None, next)
        | None ->
            match rangeOpt with
            | Some (line, col) -> Error [ Diagnostics.errorAt ($"Unknown identifier '{name}'") (line, col) ]
            | None -> Error [ Diagnostics.error ($"Unknown identifier '{name}'") ]
    | FunctionCall(name, rangeOpt, args) ->
        if name = "dbg" then
            match args with
            | [single] ->
                match inferExpr env state single with
                | Ok (typedArg, next) -> Ok (mkTypedExpr expr typedArg.Type None None None, next)
                | Error err -> Error err
            | _ ->
                match rangeOpt with
                | Some (line, col) -> Error [ Diagnostics.errorAt "dbg expects a single argument" (line, col) ]
                | None -> Error [ Diagnostics.error "dbg expects a single argument" ]
        else
        let contextCheck =
            match state.ContextRequirements |> Map.tryFind name with
            | Some contexts -> ensureContextAvailable env state name contexts rangeOpt
            | None -> Ok ()
        match contextCheck with
        | Error err -> Error err
        | Ok () ->
            match TypeEnv.tryFind name env with
            | Some scheme ->
                let mutable current = state
                let funcTy, nextState = instantiate current scheme
                current <- nextState

                let recordFieldsOpt =
                    match funcTy with
                    | TyRecord fields -> Some fields
                    | TyNominal(_, underlying, _) ->
                        match underlying with
                        | TyRecord fields -> Some fields
                        | _ -> None
                    | _ -> None

                let isWorkflowCall =
                    match args with
                    | [Lambda(lambdaArgs, _)] when lambdaArgs.IsEmpty -> recordFieldsOpt
                    | _ -> None

                match isWorkflowCall with
                | Some fields ->
                    let missingMembers =
                        ["await"; "pure"]
                        |> List.filter (fun memberName -> not (fields |> Map.containsKey memberName))
                    if not missingMembers.IsEmpty then
                        let missingText = String.concat ", " missingMembers
                        match rangeOpt with
                        | Some (line, col) -> Error [ Diagnostics.errorAt ($"Workflow '{name}' is missing context members: {missingText}") (line, col) ]
                        | None -> Error [ Diagnostics.error ($"Workflow '{name}' is missing context members: {missingText}") ]
                    else
                        let envWithCtx = extendEnvWithRecordFields env funcTy
                        match args with
                        | [Lambda(lambdaArgs, lambdaBody)] ->
                            match inferLambdaWithExpected envWithCtx current lambdaArgs lambdaBody (Some (TyPrimitive "unit")) None with
                            | Ok (typedLambda, next) ->
                                let returnTy =
                                    match typedLambda.Type with
                                    | TyFunc(_, ret) -> ret
                                    | other -> other
                                Ok (mkTypedExpr expr returnTy None None None, next)
                            | Error err -> Error err
                        | _ ->
                            match rangeOpt with
                            | Some (line, col) -> Error [ Diagnostics.errorAt ($"Workflow '{name}' must be called with a trailing lambda") (line, col) ]
                            | None -> Error [ Diagnostics.error ($"Workflow '{name}' must be called with a trailing lambda") ]
                | None ->
                    let argResults =
                        args
                        |> List.map (fun arg ->
                            match inferExpr env current arg with
                            | Ok (ty, next) ->
                                current <- next
                                Ok ty
                            | Error err -> Error err)
                    let errors = argResults |> List.choose (function Error err -> Some err | _ -> None) |> List.concat
                    if errors.IsEmpty then
                        let argTys =
                            argResults
                            |> List.choose (function Ok typedExpr -> Some typedExpr.Type | _ -> None)
                        let retTy, nextState = freshVar current
                        let expectedInput, nextState2 = freshVar nextState
                        let inputTy = inputTypeFromArgs argTys
                        let withFuncConstraint = addConstraint funcTy (TyFunc(expectedInput, retTy)) nextState2
                        let withAssignable = addAssignableConstraint inputTy expectedInput withFuncConstraint
                        Ok (mkTypedExpr expr retTy None None None, withAssignable)
                    else
                        Error errors
            | None ->
                match state.TypeDefs |> Map.tryFind name with
                | Some info ->
                    let mutable current = state
                    let argResults =
                        args
                        |> List.map (fun arg ->
                            match inferExpr env current arg with
                            | Ok (ty, next) ->
                                current <- next
                                Ok ty
                            | Error err -> Error err)
                    let errors = argResults |> List.choose (function Error err -> Some err | _ -> None) |> List.concat
                    if errors.IsEmpty then
                        let argTys =
                            argResults
                            |> List.choose (function Ok typedExpr -> Some typedExpr.Type | _ -> None)
                        let inputTy = inputTypeFromArgs argTys
                        let constrained = addAssignableConstraint inputTy info.Underlying current
                        let resultTy =
                            if name.StartsWith("@") then TyNominal(name, info.Underlying, info.IsPrivate) else info.Underlying
                        Ok (mkTypedExpr expr resultTy None None None, constrained)
                    else
                        Error errors
                | None ->
                    match rangeOpt with
                    | Some (line, col) -> Error [ Diagnostics.errorAt ($"Unknown function '{name}'") (line, col) ]
                    | None -> Error [ Diagnostics.error ($"Unknown function '{name}'") ]
    | Lambda(args, body) ->
        inferLambdaWithExpected env state args body None None
    | BinaryOp(_, left, right) ->
        match inferExpr env state left with
        | Error err -> Error err
        | Ok (leftExpr, next) ->
            match inferExpr env next right with
            | Error err -> Error err
            | Ok (rightExpr, nextState) ->
                let constrained = addConstraint leftExpr.Type rightExpr.Type nextState
                match expr with
                | BinaryOp(op, _, _) when ["+"; "-"; "*"; "/"] |> List.contains op ->
                    let numericConstrained =
                        constrained
                        |> addConstraint leftExpr.Type numericType
                    Ok (mkTypedExpr expr leftExpr.Type None None None, numericConstrained)
                | BinaryOp(op, _, _) when ["<"; ">"; "<="; ">="] |> List.contains op ->
                    let numericConstrained =
                        constrained
                        |> addConstraint leftExpr.Type numericType
                    Ok (mkTypedExpr expr (TyPrimitive "bool") None None None, numericConstrained)
                | BinaryOp(op, _, _) when ["=="; "!="] |> List.contains op ->
                    Ok (mkTypedExpr expr (TyPrimitive "bool") None None None, constrained)
                | _ -> Ok (mkTypedExpr expr leftExpr.Type None None None, constrained)
    | Pipe(value, name, rangeOpt, args) ->
        match inferExpr env state value with
        | Error err -> Error err
        | Ok (typedValue, nextState) ->
            match tryResolveAttachedFunction env typedValue.Type name rangeOpt with
            | Error err -> Error err
            | Ok resolvedOpt ->
                let resolvedName = resolvedOpt |> Option.defaultValue name
                let callArgs = value :: args
                match inferExpr env nextState (FunctionCall(resolvedName, rangeOpt, callArgs)) with
                | Ok (typedCall, finalState) ->
                    Ok ({ typedCall with Expr = Pipe(value, resolvedName, rangeOpt, args) }, finalState)
                | Error err -> Error err
    | UseIn(binding, body) ->
        match inferUseBinding env state binding with
        | Error err -> Error err
        | Ok (typedBinding, nextState) ->
            let envWithUse = extendEnvWithRecordFields env typedBinding.Type
            match inferExpr envWithUse nextState body with
            | Ok (typedBody, finalState) -> Ok (mkTypedExpr expr typedBody.Type (Some typedBody) None None, finalState)
            | Error err -> Error err
    | WorkflowBindExpr _ ->
        Error [ Diagnostics.error "Workflow bind used outside of a workflow block" ]
    | WorkflowReturnExpr _ ->
        Error [ Diagnostics.error "Workflow return used outside of a workflow block" ]
    | Block statements ->
        inferBlock env state statements
    | TupleExpr items ->
        let mutable current = state
        let tys =
            items
            |> List.map (fun item ->
                match inferExpr env current item with
                | Ok (typedExpr, next) ->
                    current <- next
                    Ok typedExpr
                | Error err -> Error err)
        let errors = tys |> List.choose (function Error err -> Some err | _ -> None) |> List.concat
        if errors.IsEmpty then
            let itemExprs = tys |> List.choose (function Ok ty -> Some ty | _ -> None)
            let itemTys = itemExprs |> List.map (fun typedExpr -> typedExpr.Type)
            Ok (mkTypedExpr expr (tupleRecordType itemTys) None None None, current)
        else
            Error errors
    | RecordExpr fields ->
        let positional, named =
            fields
            |> List.fold (fun (pos, named) field ->
                match field with
                | PositionalField expr -> (expr :: pos, named)
                | NamedField(name, expr) -> (pos, (name, expr) :: named)) ([], [])
        let mutable current = state
        let positionalResults =
            positional
            |> List.rev
            |> List.map (fun expr ->
                match inferExpr env current expr with
                | Ok (typedExpr, next) ->
                    current <- next
                    Ok typedExpr
                | Error err -> Error err)
        let namedResults =
            named
            |> List.rev
            |> List.map (fun (name, expr) ->
                match inferExpr env current expr with
                | Ok (typedExpr, next) ->
                    current <- next
                    Ok (name, typedExpr)
                | Error err -> Error err)
        let errors =
            positionalResults
            |> List.choose (function Error err -> Some err | _ -> None)
            |> List.concat
            |> fun errs ->
                let namedErrors = namedResults |> List.choose (function Error err -> Some err | _ -> None) |> List.concat
                errs @ namedErrors
        if errors.IsEmpty then
            let positionalExprs = positionalResults |> List.choose (function Ok ty -> Some ty | _ -> None)
            let positionalFieldsMap =
                positionalExprs
                |> List.mapi (fun index typedExpr -> tupleFieldName index, typedExpr.Type)
            let namedFieldsMap =
                namedResults
                |> List.choose (function Ok (name, typedExpr) -> Some (name, typedExpr.Type) | _ -> None)
            let fieldMap =
                positionalFieldsMap @ namedFieldsMap
                |> Map.ofList
            Ok (mkTypedExpr expr (TyRecord fieldMap) None None None, current)
        else
            Error errors
    | ListExpr items ->
        let mutable current = state
        let itemResults =
            items
            |> List.map (fun item ->
                match inferExpr env current item with
                | Ok (typedExpr, next) ->
                    current <- next
                    Ok typedExpr
                | Error err -> Error err)
        let errors = itemResults |> List.choose (function Error err -> Some err | _ -> None) |> List.concat
        if errors.IsEmpty then
            match itemResults |> List.choose (function Ok ty -> Some ty | _ -> None) with
            | [] ->
                let elementTy, nextState = freshVar current
                Ok (mkTypedExpr expr (listType elementTy) None None None, nextState)
            | head :: tail ->
                let constrained = tail |> List.fold (fun st ty -> addConstraint head.Type ty.Type st) current
                Ok (mkTypedExpr expr (listType head.Type) None None None, constrained)
        else
            Error errors
    | TagExpr(name, payloadOpt) ->
        match payloadOpt with
        | None -> Ok (mkTypedExpr expr (TyTag(name, None)) None None None, state)
        | Some payload ->
            match inferExpr env state payload with
            | Ok (payloadExpr, next) -> Ok (mkTypedExpr expr (TyTag(name, Some payloadExpr.Type)) None None None, next)
            | Error err -> Error err
    | IfExpr(condition, thenExpr, elseExpr) ->
        match inferExpr env state condition with
        | Error err -> Error err
        | Ok (condExpr, next) ->
            let withConstraint = addConstraint condExpr.Type (primitiveType "bool") next
            match inferExpr env withConstraint thenExpr with
            | Error err -> Error err
            | Ok (thenExprTyped, nextState) ->
                match inferExpr env nextState elseExpr with
                | Error err -> Error err
                | Ok (elseExprTyped, finalState) ->
                    let constrained = addConstraint thenExprTyped.Type elseExprTyped.Type finalState
                    Ok (mkTypedExpr expr thenExprTyped.Type None None None, constrained)
    | Match(scrutinees, arms) ->
        let mutable current = state
        let scrutineeResults =
            scrutinees
            |> List.map (fun expr ->
                match inferExpr env current expr with
                | Ok (typedExpr, next) ->
                    current <- next
                    Ok typedExpr
                | Error err -> Error err)
        let scrutineeErrors = scrutineeResults |> List.choose (function Error err -> Some err | _ -> None) |> List.concat
        if not scrutineeErrors.IsEmpty then
            Error scrutineeErrors
        else
            let scrutineeExprs = scrutineeResults |> List.choose (function Ok ty -> Some ty | _ -> None)
            let scrutineeTypes = scrutineeExprs |> List.map (fun typedExpr -> typedExpr.Type)
            let mkTypedPattern pattern ty = { Pattern = pattern; Type = ty }
            let rec tryRecordFields ty =
                match ty with
                | TyRecord fields -> Some fields
                | TyNominal(_, underlying, _) -> tryRecordFields underlying
                | _ -> None
            let rec inferPattern (env': TypeEnv) (state': InferState) (scrutineeTy: Ty) (pattern: Pattern) =
                match pattern with
                | WildcardPattern
                | ElsePattern -> Ok (env', state', mkTypedPattern pattern scrutineeTy)
                | IdentifierPattern name ->
                    let scheme = TypeEnv.mono scrutineeTy
                    Ok (TypeEnv.extend name scheme env', state', mkTypedPattern pattern scrutineeTy)
                | LiteralPattern literal ->
                    let constrained = addConstraint scrutineeTy (literalType literal) state'
                    Ok (env', constrained, mkTypedPattern pattern scrutineeTy)
                | GuardPattern(_, guardExpr) ->
                    match inferExpr env' state' guardExpr with
                    | Ok (guardTyped, nextState) ->
                        let constrained = addConstraint scrutineeTy guardTyped.Type nextState
                        Ok (env', constrained, mkTypedPattern pattern scrutineeTy)
                    | Error err -> Error err
                | RangePattern(startExpr, endExpr) ->
                    match inferExpr env' state' startExpr with
                    | Error err -> Error err
                    | Ok (startTyped, nextState) ->
                        match inferExpr env' nextState endExpr with
                        | Error err -> Error err
                        | Ok (endTyped, finalState) ->
                            let constrained =
                                finalState
                                |> addConstraint scrutineeTy startTyped.Type
                                |> addConstraint startTyped.Type endTyped.Type
                            Ok (env', constrained, mkTypedPattern pattern scrutineeTy)
                | TuplePattern patterns ->
                    let mutable currentState = state'
                    let itemTypes, nextState =
                        patterns
                        |> List.map (fun _ ->
                            let ty, next = freshVar currentState
                            currentState <- next
                            ty)
                        |> fun tys -> tys, currentState
                    let tupleRecord = tupleRecordType itemTypes
                    let constrained = addConstraint scrutineeTy tupleRecord nextState
                    let mutable innerEnv = env'
                    let mutable innerState = constrained
                    let mutable innerErrors: Diagnostic list = []
                    let mutable innerPatterns: TypedPattern list = []
                    for (pat, ty) in List.zip patterns itemTypes do
                        match inferPattern innerEnv innerState ty pat with
                        | Ok (nextEnv, nextState, typedPattern) ->
                            innerEnv <- nextEnv
                            innerState <- nextState
                            innerPatterns <- innerPatterns @ [typedPattern]
                        | Error err -> innerErrors <- err
                    if innerErrors.IsEmpty then
                        Ok (innerEnv, innerState, mkTypedPattern pattern tupleRecord)
                    else
                        Error innerErrors
                | TagPattern(tagName, payloadOpt) ->
                    match payloadOpt with
                    | None ->
                        let constrained = addAssignableConstraint (TyTag(tagName, None)) scrutineeTy state'
                        Ok (env', constrained, mkTypedPattern pattern scrutineeTy)
                    | Some payload ->
                        let payloadTy, nextState = freshVar state'
                        let constrained = addAssignableConstraint (TyTag(tagName, Some payloadTy)) scrutineeTy nextState
                        match inferPattern env' constrained payloadTy payload with
                        | Ok (nextEnv, nextState, _) ->
                            Ok (nextEnv, nextState, mkTypedPattern pattern scrutineeTy)
                        | Error err -> Error err
                | ListPattern(patterns, splatOpt) ->
                    let elementTy, nextState = freshVar state'
                    let constrained = addConstraint scrutineeTy (listType elementTy) nextState
                    let mutable innerEnv = env'
                    let mutable innerState = constrained
                    let mutable innerErrors: Diagnostic list = []
                    for pat in patterns do
                        match inferPattern innerEnv innerState elementTy pat with
                        | Ok (nextEnv, nextState, _) ->
                            innerEnv <- nextEnv
                            innerState <- nextState
                        | Error err -> innerErrors <- err
                    let innerStateWithSplat =
                        match splatOpt with
                        | Some (IdentifierPattern name) ->
                            let scheme = TypeEnv.mono (listType elementTy)
                            let updatedEnv = TypeEnv.extend name scheme innerEnv
                            innerEnv <- updatedEnv
                            innerState
                        | _ -> innerState
                    if innerErrors.IsEmpty then
                        Ok (innerEnv, innerStateWithSplat, mkTypedPattern pattern scrutineeTy)
                    else
                        Error innerErrors
                | ListSplatMiddle(prefix, suffix) ->
                    let elementTy, nextState = freshVar state'
                    let constrained = addConstraint scrutineeTy (listType elementTy) nextState
                    let mutable innerEnv = env'
                    let mutable innerState = constrained
                    let mutable innerErrors: Diagnostic list = []
                    for pat in prefix @ suffix do
                        match inferPattern innerEnv innerState elementTy pat with
                        | Ok (nextEnv, nextState, _) ->
                            innerEnv <- nextEnv
                            innerState <- nextState
                        | Error err -> innerErrors <- err
                    if innerErrors.IsEmpty then
                        Ok (innerEnv, innerState, mkTypedPattern pattern scrutineeTy)
                    else
                        Error innerErrors
                | RecordPattern(typeName, patterns) ->
                    let mutable currentState = state'
                    let itemTypes, nextState =
                        patterns
                        |> List.map (fun _ ->
                            let ty, next = freshVar currentState
                            currentState <- next
                            ty)
                        |> fun tys -> tys, currentState
                    let constrained = addConstraint scrutineeTy (TyRecord (Map.empty)) nextState
                    let mutable innerEnv = env'
                    let mutable innerState = constrained
                    let mutable innerErrors: Diagnostic list = []
                    for (pat, ty) in List.zip patterns itemTypes do
                        match inferPattern innerEnv innerState ty pat with
                        | Ok (nextEnv, nextState, _) ->
                            innerEnv <- nextEnv
                            innerState <- nextState
                        | Error err -> innerErrors <- err
                    if innerErrors.IsEmpty then
                        Ok (innerEnv, innerState, mkTypedPattern pattern scrutineeTy)
                    else
                        Error innerErrors
                | RecordMemberPattern members ->
                    let mutable innerEnv = env'
                    let mutable innerState = state'
                    let mutable innerErrors: Diagnostic list = []
                    let fieldHints = tryRecordFields scrutineeTy |> Option.defaultValue Map.empty
                    let mutable fieldMap = Map.empty
                    for (name, pat) in members do
                        let hintTy = fieldHints |> Map.tryFind name
                        let memberTy, nextState =
                            match hintTy with
                            | Some ty -> ty, innerState
                            | None -> freshVar innerState
                        innerState <- nextState
                        match inferPattern innerEnv innerState memberTy pat with
                        | Ok (nextEnv, nextState, _) ->
                            innerEnv <- nextEnv
                            innerState <- nextState
                            if hintTy.IsNone then
                                fieldMap <- fieldMap.Add(name, memberTy)
                        | Error err -> innerErrors <- err
                    if innerErrors.IsEmpty then
                        let constrained =
                            if fieldMap.IsEmpty then innerState else addConstraint scrutineeTy (TyRecord fieldMap) innerState
                        Ok (innerEnv, constrained, mkTypedPattern pattern scrutineeTy)
                    else
                        Error innerErrors

            let mutable armExprs: TypedExpr list = []
            let mutable typedArms: TypedMatchArm list = []
            let mutable errors: Diagnostic list = []
            let isCatchAllPattern pattern =
                match pattern with
                | WildcardPattern
                | ElsePattern
                | IdentifierPattern _ -> true
                | _ -> false
            let tryGetTagName pattern =
                match pattern with
                | TagPattern(tagName, _) -> Some tagName
                | _ -> None
            let tryGetTagUnion ty =
                let rec unwrap ty =
                    match ty with
                    | TyNominal(_, underlying, _) -> unwrap underlying
                    | TyUnion items ->
                        if items |> List.forall (function TyTag _ -> true | _ -> false) then
                            items
                            |> List.choose (function TyTag(name, _) -> Some name | _ -> None)
                            |> Set.ofList
                            |> Some
                        else
                            None
                    | _ -> None
                unwrap ty
            let isListType ty =
                let rec unwrap ty =
                    match ty with
                    | TyNominal(_, underlying, _) -> unwrap underlying
                    | TyApply("list", _) -> true
                    | _ -> false
                unwrap ty
            let listPatternsCoverAll patternsAtPos =
                let hasEmpty =
                    patternsAtPos
                    |> List.exists (function
                        | ListPattern(patterns, None) when patterns.IsEmpty -> true
                        | _ -> false)
                let hasSplat =
                    patternsAtPos
                    |> List.exists (function
                        | ListPattern(_, Some _) -> true
                        | _ -> false)
                hasEmpty && hasSplat
            let isRecordType ty =
                let rec unwrap ty =
                    match ty with
                    | TyNominal(_, underlying, _) -> unwrap underlying
                    | TyRecord _ -> true
                    | _ -> false
                unwrap ty
            let patternsForIndex index =
                arms
                |> List.choose (fun (patterns, _) ->
                    if index < patterns.Length then Some patterns.[index] else None)
            let mutable scrutineeOverrides: Map<int, Ty> = Map.empty
            for (index, scrutineeTy) in scrutineeTypes |> List.indexed do
                let patternsAtPos = patternsForIndex index
                let nonCatchAll = patternsAtPos |> List.filter (fun pat -> not (isCatchAllPattern pat))
                let allTagPatterns =
                    not nonCatchAll.IsEmpty
                    && (nonCatchAll |> List.forall (function TagPattern _ -> true | _ -> false))
                if allTagPatterns then
                    let tagInfos =
                        nonCatchAll
                        |> List.choose (function TagPattern(tagName, payloadOpt) -> Some (tagName, payloadOpt) | _ -> None)
                        |> List.groupBy fst
                        |> List.sortBy fst
                    if tagInfos.Length > 1 then
                        let mutable stateForUnion = current
                        let unionItems =
                            tagInfos
                            |> List.map (fun (tagName, entries) ->
                                let hasPayload = entries |> List.exists (fun (_, payloadOpt) -> payloadOpt.IsSome)
                                if hasPayload then
                                    let payloadTy, nextState = freshVar stateForUnion
                                    stateForUnion <- nextState
                                    TyTag(tagName, Some payloadTy)
                                else
                                    TyTag(tagName, None))
                        let unionTy = TyUnion unionItems
                        scrutineeOverrides <- scrutineeOverrides |> Map.add index unionTy
                        current <- addConstraint scrutineeTy unionTy stateForUnion
            let exhaustivenessDiagnostics () =
                scrutineeTypes
                |> List.mapi (fun index scrutineeTy ->
                    let patternsAtPos = patternsForIndex index
                    let hasCatchAll = patternsAtPos |> List.exists isCatchAllPattern
                    if hasCatchAll then
                        None
                    else
                        match tryGetTagUnion scrutineeTy with
                        | Some tagUnion ->
                            let coveredTags =
                                patternsAtPos
                                |> List.choose tryGetTagName
                                |> Set.ofList
                            let missing = Set.difference tagUnion coveredTags |> Set.toList
                            if missing.IsEmpty then
                                None
                            else
                                let missingText = missing |> String.concat ", "
                                let suffix = if scrutineeTypes.Length > 1 then $" (scrutinee {index + 1})" else ""
                                Some (Diagnostics.error ($"Non-exhaustive match{suffix}, missing tags: {missingText}"))
                        | None ->
                            if isListType scrutineeTy then
                                if listPatternsCoverAll patternsAtPos then
                                    None
                                else
                                    let suffix = if scrutineeTypes.Length > 1 then $" (scrutinee {index + 1})" else ""
                                    Some (Diagnostics.error ($"Non-exhaustive match{suffix}, missing list cases"))
                            elif isRecordType scrutineeTy then
                                let suffix = if scrutineeTypes.Length > 1 then $" (scrutinee {index + 1})" else ""
                                Some (Diagnostics.error ($"Non-exhaustive match{suffix}, missing record cases"))
                            else
                                None)
                |> List.choose id
            for (patterns, expr) in arms do
                if errors.IsEmpty then
                    let mutable env' = env
                    let mutable state' = current
                    let effectiveScrutineeTypes =
                        scrutineeTypes
                        |> List.mapi (fun idx ty -> scrutineeOverrides |> Map.tryFind idx |> Option.defaultValue ty)
                    let patternPairs = List.zip patterns effectiveScrutineeTypes
                    let mutable typedPatterns: TypedPattern list = []
                    for (pattern, scrutineeTy) in patternPairs do
                        match inferPattern env' state' scrutineeTy pattern with
                        | Ok (nextEnv, nextState, typedPattern) ->
                            env' <- nextEnv
                            state' <- nextState
                            typedPatterns <- typedPatterns @ [typedPattern]
                        | Error err -> errors <- err
                    if errors.IsEmpty then
                        match inferExpr env' state' expr with
                        | Ok (typedExpr, next) ->
                            current <- next
                            armExprs <- typedExpr :: armExprs
                            typedArms <- typedArms @ [ (typedPatterns, typedExpr) ]
                        | Error err -> errors <- err
            if errors.IsEmpty then
                let hasRestUnion =
                    scrutineeTypes
                    |> List.exists (function
                        | TyUnion items -> items |> List.exists (function TyVar _ -> true | _ -> false)
                        | _ -> false)
                let restError =
                    if hasRestUnion then
                        let hasCatchAll =
                            arms
                            |> List.exists (fun (patterns, _) ->
                                patterns
                                |> List.exists (function
                                    | WildcardPattern
                                    | ElsePattern
                                    | IdentifierPattern _ -> true
                                    | _ -> false))
                        if hasCatchAll then
                            None
                        else
                            Some (Diagnostics.error "Tag union rest parameters require a catch-all match arm")
                    else
                        None
                let exhaustivenessErrors =
                    if restError.IsSome then
                        []
                    else
                        exhaustivenessDiagnostics ()
                match restError with
                | Some diag -> Error [ diag ]
                | None ->
                    if not exhaustivenessErrors.IsEmpty then
                        Error exhaustivenessErrors
                    else
                    match List.rev armExprs with
                    | [] -> Ok (mkTypedExpr expr (TyPrimitive "unit") None None (Some typedArms), current)
                    | head :: tail ->
                        let constrained = tail |> List.fold (fun st typedExpr -> addConstraint head.Type typedExpr.Type st) current
                        Ok (mkTypedExpr expr head.Type None None (Some typedArms), constrained)
            else
                Error errors
    | MemberAccess(baseExpr, memberName, rangeOpt) ->
        match baseExpr with
        | IdentifierExpr(baseName, _) ->
            let qualifiedName = $"{baseName}.{memberName}"
            match TypeEnv.tryFind qualifiedName env with
            | Some scheme ->
                let ty, next = instantiate state scheme
                Ok (mkTypedExpr expr ty None None None, next)
            | None ->
                match inferExpr env state baseExpr with
                | Error err -> Error err
                | Ok (typedBase, next) ->
                    let fieldTypeOpt =
                        match typedBase.Type with
                        | TyRecord fields -> fields |> Map.tryFind memberName
                        | TyNominal(_, underlying, _) ->
                            match underlying with
                            | TyRecord fields -> fields |> Map.tryFind memberName
                            | _ -> None
                        | _ -> None
                    match fieldTypeOpt with
                    | Some fieldTy -> Ok (mkTypedExpr expr fieldTy None None None, next)
                    | None ->
                        match rangeOpt with
                        | Some (line, col) -> Error [ Diagnostics.errorAt ($"Record '{baseName}' has no member '{memberName}'") (line, col) ]
                        | None -> Error [ Diagnostics.error ($"Record '{baseName}' has no member '{memberName}'") ]
        | _ ->
            match inferExpr env state baseExpr with
            | Error err -> Error err
            | Ok (typedBase, next) ->
                let fieldTypeOpt =
                    match typedBase.Type with
                    | TyRecord fields -> fields |> Map.tryFind memberName
                    | TyNominal(_, underlying, _) ->
                        match underlying with
                        | TyRecord fields -> fields |> Map.tryFind memberName
                        | _ -> None
                    | _ -> None
                match fieldTypeOpt with
                | Some fieldTy -> Ok (mkTypedExpr expr fieldTy None None None, next)
                | None ->
                    match rangeOpt with
                    | Some (line, col) -> Error [ Diagnostics.errorAt ($"Unknown member '{memberName}'") (line, col) ]
                    | None -> Error [ Diagnostics.error ($"Unknown member '{memberName}'") ]
    | ContextMemberAccess(ctxType, memberName) ->
        // TODO: Look up the context type and find the member's type
        // For now, return a fresh type variable
        let ty, next = freshVar state
        Ok (mkTypedExpr expr ty None None None, next)
    | ContextMemberCall(ctxType, memberName, args) ->
        // TODO: Look up the context, find the member function type, check args
        // For now, infer args and return a fresh type variable
        let mutable current = state
        let mutable errors: Diagnostic list = []
        for arg in args do
            match inferExpr env current arg with
            | Ok (_, next) -> current <- next
            | Error err -> errors <- errors @ err
        if errors.IsEmpty then
            let ty, next = freshVar current
            Ok (mkTypedExpr expr ty None None None, next)
        else
            Error errors

and inferUseBinding (env: TypeEnv) (state: InferState) (binding: UseBinding) : Result<TypedExpr * InferState, Diagnostic list> =
    match binding with
    | UseValue expr -> inferExpr env state expr
    | UseContextInstance(ctxType, expr) -> 
        // TODO: Validate that expr satisfies the context type
        inferExpr env state expr

and inferLambdaWithExpected (env: TypeEnv) (state: InferState) (args: (Identifier * TypeExpr option) list) (body: Expression) (expectedInputOpt: Ty option) (expectedReturnOpt: Ty option) : Result<TypedExpr * InferState, Diagnostic list> =
    let mutable current = state
    let mutable env' = env
    let argTys =
        match expectedInputOpt with
        | Some expectedInput ->
            let tys, next = argTypesFromExpected current args expectedInput
            current <- next
            tys
        | None ->
            args
            |> List.map (fun (_, typeOpt) ->
                let ty, next =
                    match typeOpt with
                    | Some typeExpr -> typeExprToTy current typeExpr
                    | None -> freshVar current
                current <- next
                ty)
    for ((name, _), ty) in List.zip args argTys do
        env' <- TypeEnv.extend name (TypeEnv.mono ty) env'
    match inferExpr env' current body with
    | Ok (bodyExpr, next) ->
        let withReturnConstraint =
            match expectedReturnOpt with
            | Some expectedReturn ->
                match expectedReturn with
                | TyNominal(name, underlying, _) when state.LocalNominals.Contains name ->
                    match bodyExpr.Type with
                    | TyNominal _ -> addAssignableConstraint bodyExpr.Type expectedReturn next
                    | _ -> addAssignableConstraint bodyExpr.Type underlying next
                | _ -> addAssignableConstraint bodyExpr.Type expectedReturn next
            | None -> next
        let inputTy =
            match expectedInputOpt with
            | Some expectedInput -> expectedInput
            | None -> inputTypeFromArgs argTys
        Ok (mkTypedExpr (Lambda(args, body)) (TyFunc(inputTy, bodyExpr.Type)) (Some bodyExpr) None None, withReturnConstraint)
    | Error err -> Error err

and inferBlock (env: TypeEnv) (state: InferState) (statements: Statement list) : Result<TypedExpr * InferState, Diagnostic list> =
    match statements with
    | [] -> Ok (mkTypedExpr (Block []) (TyPrimitive "unit") None (Some []) None, state)
    | _ ->
        let mutable current = state
        let mutable env' = env
        let mutable lastExpr = mkTypedExpr (LiteralExpr (IntLit 0)) (TyPrimitive "unit") None None None
        let mutable typedStatements: TypedStatement list = []
        let mutable errors: Diagnostic list = []
        for statement in statements do
            match statement with
            | DefStatement(isExport, name, typeOpt, expr) ->
                if errors.IsEmpty then
                    match ensureAttachedTypeExists current None name with
                    | Error err -> errors <- err
                    | Ok () ->
                        let contextOpt, declaredTypeExprOpt = extractContextFromTypeExpr typeOpt
                        let envWithCtx, nextState =
                            match contextOpt with
                            | Some contexts -> extendEnvWithContexts env' current contexts
                            | None -> env', current
                        let expectedInputOpt, expectedReturnOpt, expectedDeclaredOpt, nextState2 =
                            match declaredTypeExprOpt with
                            | Some declaredExpr ->
                                let ty, st = typeExprToTy nextState declaredExpr
                                match ty with
                                | TyFunc(inputTy, returnTy) -> Some inputTy, Some returnTy, Some ty, st
                                | _ -> None, None, Some ty, st
                            | None -> None, None, None, nextState
                        let effectiveExpectedInputOpt =
                            match expectedInputOpt with
                            | Some _ -> expectedInputOpt
                            | None when expectedDeclaredOpt.IsNone -> tryInferAttachedLambdaInput nextState2 name expr
                            | None -> None
                        let preboundTy, preboundState, envWithRec, preboundFresh =
                            match expectedDeclaredOpt with
                            | Some declaredTy -> declaredTy, nextState2, TypeEnv.extend name (TypeEnv.mono declaredTy) envWithCtx, false
                            | None ->
                                let ty, st = freshVar nextState2
                                ty, st, TypeEnv.extend name (TypeEnv.mono ty) envWithCtx, true
                        let nextStateWithReq =
                            match contextOpt with
                            | Some contexts -> registerContextRequirement preboundState name contexts
                            | None -> preboundState
                        let inferResult =
                            match expr with
                            | Lambda(args, body) when effectiveExpectedInputOpt.IsSome || expectedReturnOpt.IsSome ->
                                inferLambdaWithExpected envWithRec nextStateWithReq args body effectiveExpectedInputOpt expectedReturnOpt
                            | _ -> inferExpr envWithRec nextStateWithReq expr
                        match inferResult with
                        | Ok (typedExpr, next) ->
                            let declaredTy, finalState =
                                match expectedDeclaredOpt with
                                | Some declaredTy ->
                                    match expr with
                                    | Lambda _ -> declaredTy, next
                                    | _ ->
                                        let updatedState =
                                            match declaredTy with
                                            | TyNominal(name, underlying, _) when current.LocalNominals.Contains name ->
                                                match typedExpr.Type with
                                                | TyNominal _ -> addAssignableConstraint typedExpr.Type declaredTy next
                                                | _ -> addAssignableConstraint typedExpr.Type underlying next
                                            | _ -> addAssignableConstraint typedExpr.Type declaredTy next
                                        declaredTy, updatedState
                                | None ->
                                    let updatedState =
                                        if preboundFresh then addConstraint preboundTy typedExpr.Type next else next
                                    typedExpr.Type, updatedState
                            current <- finalState
                            let scheme = TypeEnv.generalize env' declaredTy
                            env' <- TypeEnv.extend name scheme env'
                            typedStatements <- typedStatements @ [ TypedDefStatement(isExport, name, typeOpt, typedExpr) ]
                            lastExpr <- typedExpr
                        | Error err -> errors <- err
            | ImportStatement items ->
                typedStatements <- typedStatements @ [ TypedImportStatement items ]
            | TypeDefStatement(name, modifiers, parameters, body) ->
                current <- registerTypeDef current name modifiers parameters body
                typedStatements <- typedStatements @ [ TypedTypeDefStatement(name, modifiers, parameters, body) ]
            | UseStatement binding ->
                if errors.IsEmpty then
                    match inferUseBinding env' current binding with
                    | Ok (typedExpr, next) ->
                        current <- next
                        env' <- extendEnvWithRecordFields env' typedExpr.Type
                        typedStatements <- typedStatements @ [ TypedUseStatement(binding, Some typedExpr) ]
                    | Error err -> errors <- err
            | ExprStatement expr ->
                if errors.IsEmpty then
                    match inferExpr env' current expr with
                    | Ok (typedExpr, next) ->
                        current <- next
                        typedStatements <- typedStatements @ [ TypedExprStatement typedExpr ]
                        lastExpr <- typedExpr
                    | Error err -> errors <- err
        if errors.IsEmpty then
            let blockExpr = mkTypedExpr (Block statements) lastExpr.Type (Some lastExpr) (Some typedStatements) None
            Ok (blockExpr, current)
        else
            Error errors

let inferModuleWithEnv (initialEnv: TypeEnv) (initialState: InferState) (module': Module) : Result<Map<string, Ty> * TypedTopLevelItem list * InferState, Diagnostic list> =
    let mutable env = initialEnv
    let mutable state = initialState
    let mutable currentModule: string option = None
    let mutable types = Map.empty
    let mutable items: TypedTopLevelItem list = []
    let mutable errors: Diagnostic list = []

    for item in module' do
        match item with
        | Def (ValueDef(isExport, name, typeOpt, expr)) when errors.IsEmpty ->
            match ensureAttachedTypeExists state currentModule name with
            | Error err -> errors <- err
            | Ok () ->
                let contextOpt, declaredTypeExprOpt = extractContextFromTypeExpr typeOpt
                let envWithCtx, nextState =
                    match contextOpt with
                    | Some contexts -> extendEnvWithContexts env state contexts
                    | None -> env, state
                let expectedInputOpt, expectedReturnOpt, expectedDeclaredOpt, nextState2 =
                    match declaredTypeExprOpt with
                    | Some declaredExpr ->
                        let ty, st = typeExprToTy nextState declaredExpr
                        match ty with
                        | TyFunc(inputTy, returnTy) -> Some inputTy, Some returnTy, Some ty, st
                        | _ -> None, None, Some ty, st
                    | None -> None, None, None, nextState
                let effectiveExpectedInputOpt =
                    match expectedInputOpt with
                    | Some _ -> expectedInputOpt
                    | None when expectedDeclaredOpt.IsNone -> tryInferAttachedLambdaInput nextState2 name expr
                    | None -> None
                let preboundTy, preboundState, envWithRec, preboundFresh =
                    match expectedDeclaredOpt with
                    | Some declaredTy -> declaredTy, nextState2, TypeEnv.extend name (TypeEnv.mono declaredTy) envWithCtx, false
                    | None ->
                        let ty, st = freshVar nextState2
                        ty, st, TypeEnv.extend name (TypeEnv.mono ty) envWithCtx, true
                let nextStateWithReq =
                    match contextOpt with
                    | Some contexts -> registerContextRequirement preboundState name contexts
                    | None -> preboundState
                let inferResult =
                    match expr with
                    | Lambda(args, body) when effectiveExpectedInputOpt.IsSome || expectedReturnOpt.IsSome ->
                        inferLambdaWithExpected envWithRec nextStateWithReq args body effectiveExpectedInputOpt expectedReturnOpt
                    | _ -> inferExpr envWithRec nextStateWithReq expr
                match inferResult with
                | Ok (typedExpr, next) ->
                    let declaredTy, finalState =
                        match expectedDeclaredOpt with
                        | Some declaredTy ->
                            match expr with
                            | Lambda _ -> declaredTy, next
                            | _ ->
                                let updatedState =
                                    match declaredTy with
                                    | TyNominal(name, underlying, _) when state.LocalNominals.Contains name ->
                                        match typedExpr.Type with
                                        | TyNominal _ -> addAssignableConstraint typedExpr.Type declaredTy next
                                        | _ -> addAssignableConstraint typedExpr.Type underlying next
                                    | _ -> addAssignableConstraint typedExpr.Type declaredTy next
                                declaredTy, updatedState
                        | None ->
                            let updatedState =
                                if preboundFresh then addConstraint preboundTy typedExpr.Type next else next
                            typedExpr.Type, updatedState
                    state <- finalState
                    let scheme = TypeEnv.generalize env declaredTy
                    env <- TypeEnv.extend name scheme env
                    types <- types |> Map.add name declaredTy
                    items <- items @ [ TypedDef (TypedValueDef(isExport, name, typeOpt, typedExpr)) ]
                | Error err -> errors <- err
        | Expr expr when errors.IsEmpty ->
            match inferExpr env state expr with
            | Ok (typedExpr, next) ->
                state <- next
                items <- items @ [ TypedExprItem typedExpr ]
            | Error err -> errors <- err
        | Expr _ -> ()
        | Import itemsList -> items <- items @ [ TypedImport itemsList ]
        | ModuleDecl name ->
            currentModule <- Some name
            items <- items @ [ TypedModuleDecl name ]
        | ModuleBlock _ -> ()
        | Def (TypeDef(name, modifiers, parameters, body)) ->
            state <- registerTypeDef state name modifiers parameters body
            items <- items @ [ TypedDef (TypedTypeDef(name, modifiers, parameters, body)) ]
        | Def (ValueDef _) -> ()

    if errors.IsEmpty then Ok (types, items, state) else Error errors

let inferModule (module': Module) : Result<Map<string, Ty> * TypedTopLevelItem list * InferState, Diagnostic list> =
    inferModuleWithEnv TypeEnv.empty emptyState module'
