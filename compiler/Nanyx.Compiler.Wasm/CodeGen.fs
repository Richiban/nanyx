module Transpiler.Wasm.CodeGen

open System
open System.Text
open Parser.Program

type private ContextFn = string * int * int

type private TranspileEnv = { KnownFunctions: Set<string>; FunctionArities: Map<string, int>; FunctionContextRequirements: Map<string, string list>; Locals: Map<string, string>; DbgTempLocal: string option; TagIds: Map<string, int>; MatchTempNames: string list; NextMatchTemp: int; StringLiterals: Map<string, int>; ContextFunctions: Map<string, ContextFn>; ContextParams: Map<string, string>; PendingUseBindings: Map<string, ContextFn> list ref; RecordLayouts: Map<string, string list>; TypeLayouts: Map<string, string list>; TypeFieldTypes: Map<string, Map<string, string>>; RecordFieldTypes: Map<string, Map<string, string>>; ParamTypes: Map<string, string> }

let private ctxFnName (name: string, _, _) = name
let private ctxFnSlot (_, slot: int, _) = slot
let private ctxFnArity (_, _, arity: int) = arity

// Convert a TypeExpr to a string key (e.g., TypeApply("ToString", [TypeVar "a"]) -> "ToString_a")
let rec private typeExprToKey (t: TypeExpr) =
    match t with
    | TypeName name -> name
    | TypeVar name -> name
    | TypeApply(name, typeArgs) -> 
        let argsStr = typeArgs |> List.map typeExprToKey |> String.concat "_"
        $"{name}_{argsStr}"
    | _ -> "unknown"

// Extract context type expressions from a TypeWithContext or TypeContext annotation
let private extractContextTypes (typeExpr: TypeExpr option) : TypeExpr list =
    match typeExpr with
    | Some (TypeWithContext(contexts, _)) -> contexts
    | Some (TypeContext contexts) -> contexts
    | _ -> []

// Extract context type names from a TypeWithContext or TypeContext annotation
let private extractContextTypeNames (typeExpr: TypeExpr option) : string list =
    match typeExpr with
    | Some (TypeWithContext(contexts, _)) ->
        contexts |> List.choose (function
            | TypeName name -> Some name
            | TypeApply(name, _) -> Some name
            | _ -> None)
    | Some (TypeContext contexts) ->
        contexts |> List.choose (function
            | TypeName name -> Some name
            | TypeApply(name, _) -> Some name
            | _ -> None)
    | _ -> []

// Get the inner type (without context) from a TypeWithContext or TypeContext
let private unwrapContextType (typeExpr: TypeExpr option) : TypeExpr option =
    match typeExpr with
    | Some (TypeWithContext(_, inner)) -> Some inner
    | Some (TypeContext _) -> None  // TypeContext alone has no inner type
    | other -> other

let private sanitizeIdentifier (name: string) =
    let sanitized =
        name
        |> Seq.map (fun c -> if Char.IsLetterOrDigit c || c = '_' then c else '_')
        |> Seq.toArray
        |> String
    if String.IsNullOrWhiteSpace sanitized then "_"
    elif Char.IsDigit sanitized.[0] then "_" + sanitized
    else sanitized

let private wasmBinaryInstruction op =
    match op with
    | "+" -> Some "i32.add"
    | "-" -> Some "i32.sub"
    | "*" -> Some "i32.mul"
    | "/" -> Some "i32.div_s"
    | "%" -> Some "i32.rem_s"
    | "==" -> Some "i32.eq"
    | "!=" -> Some "i32.ne"
    | "<" -> Some "i32.lt_s"
    | "<=" -> Some "i32.le_s"
    | ">" -> Some "i32.gt_s"
    | ">=" -> Some "i32.ge_s"
    | "&&" -> Some "i32.and"
    | "||" -> Some "i32.or"
    | _ -> None

let private flattenCallArgs (args: Expression list) =
    match args with
    | [TupleExpr items] -> items
    | _ -> args

let rec private collectExprLocalNames (expr: Expression) : string list =
    match expr with
    | Block statements -> collectStatementLocalNames statements
    | BinaryOp(_, left, right) -> (collectExprLocalNames left) @ (collectExprLocalNames right)
    | IfExpr(cond, thenExpr, elseExpr) ->
        (collectExprLocalNames cond) @ (collectExprLocalNames thenExpr) @ (collectExprLocalNames elseExpr)
    | FunctionCall(_, _, args) -> args |> List.collect collectExprLocalNames
    | Lambda(_, body) -> collectExprLocalNames body
    | Pipe(value, _, _, args) -> (collectExprLocalNames value) @ (args |> List.collect collectExprLocalNames)
    | TupleExpr items
    | ListExpr items -> items |> List.collect collectExprLocalNames
    | RecordExpr fields ->
        fields
        |> List.collect (function
            | NamedField(_, value) -> collectExprLocalNames value
            | PositionalField value -> collectExprLocalNames value)
    | Match(values, arms) ->
        let valueLocals = values |> List.collect collectExprLocalNames
        let armLocals =
            arms
            |> List.collect (fun (_, armExpr) -> collectExprLocalNames armExpr)
        valueLocals @ armLocals
    | UseIn(_, body) -> collectExprLocalNames body
    | MemberAccess(inner, _, _) -> collectExprLocalNames inner
    | ContextMemberAccess(_, _) -> []
    | ContextMemberCall(_, _, args) -> args |> List.collect collectExprLocalNames
    | TagExpr(_, payload) -> payload |> Option.map collectExprLocalNames |> Option.defaultValue []
    | InterpolatedString parts ->
        parts
        |> List.collect (function
            | StringText _ -> []
            | StringExpr inner -> collectExprLocalNames inner)
    | WorkflowBindExpr(_, value)
    | WorkflowReturnExpr value -> collectExprLocalNames value
    | UnitExpr
    | LiteralExpr _
    | IdentifierExpr _ -> []

// Collect names of defs that are used in a `use` statement (context-only defs)
and private collectContextOnlyDefNames (statements: Statement list) : Set<string> =
    statements
    |> List.choose (function
        | UseStatement (UseValue (IdentifierExpr (name, _))) -> Some name
        | _ -> None)
    |> Set.ofList

and private collectStatementLocalNames (statements: Statement list) : string list =
    let contextOnlyDefs = collectContextOnlyDefNames statements
    statements
    |> List.collect (function
        | DefStatement(_, name, _, rhs) ->
            if contextOnlyDefs |> Set.contains name then
                // Skip context-only defs - they don't need locals
                []
            else
                name :: collectExprLocalNames rhs
        | ExprStatement expr -> collectExprLocalNames expr
        | ImportStatement _
        | TypeDefStatement _
        | UseStatement _ -> [])

let private collectBlockLocalNames (statements: Statement list) =
    collectStatementLocalNames statements |> List.distinct

let rec private collectPayloadLocalsExpr expr =
    match expr with
    | Match(_, arms) ->
        arms |> List.collect (fun (patterns, armExpr) ->
            let payloads = patterns |> List.choose (function
                | TagPattern(_, Some (IdentifierPattern ident)) -> Some ident
                | _ -> None)
            List.append payloads (collectPayloadLocalsExpr armExpr))
    | FunctionCall(_, _, args) -> args |> List.collect collectPayloadLocalsExpr
    | BinaryOp(_, left, right) -> List.append (collectPayloadLocalsExpr left) (collectPayloadLocalsExpr right)
    | IfExpr(cond, thenExpr, elseExpr) ->
        List.append (collectPayloadLocalsExpr cond) (List.append (collectPayloadLocalsExpr thenExpr) (collectPayloadLocalsExpr elseExpr))
    | Lambda(_, body) -> collectPayloadLocalsExpr body
    | Pipe(value, _, _, args) -> List.append (collectPayloadLocalsExpr value) (args |> List.collect collectPayloadLocalsExpr)
    | Block statements ->
        statements |> List.collect (function
            | DefStatement(_, _, _, rhs) -> collectPayloadLocalsExpr rhs
            | ExprStatement value -> collectPayloadLocalsExpr value
            | _ -> [])
    | TupleExpr values
    | ListExpr values -> values |> List.collect collectPayloadLocalsExpr
    | RecordExpr fields ->
        fields |> List.collect (function
            | NamedField(_, value) -> collectPayloadLocalsExpr value
            | PositionalField value -> collectPayloadLocalsExpr value)
    | UseIn(_, body) -> collectPayloadLocalsExpr body
    | MemberAccess(value, _, _) -> collectPayloadLocalsExpr value
    | TagExpr(_, payload) -> payload |> Option.map collectPayloadLocalsExpr |> Option.defaultValue []
    | InterpolatedString parts ->
        parts |> List.collect (function
            | StringText _ -> []
            | StringExpr value -> collectPayloadLocalsExpr value)
    | WorkflowBindExpr(_, value)
    | WorkflowReturnExpr value -> collectPayloadLocalsExpr value
    | ContextMemberAccess(_, _)
    | ContextMemberCall(_, _, _)
    | UnitExpr
    | LiteralExpr _
    | IdentifierExpr _ -> []

let private createNameMap (names: string list) =
    names
    |> List.fold (fun (acc, used) name ->
        let baseName = sanitizeIdentifier name
        let rec findAvailable candidate index =
            if used |> Set.contains candidate then
                findAvailable ($"{baseName}_{index}") (index + 1)
            else
                candidate
        let chosen = findAvailable baseName 1
        (acc |> Map.add name chosen, used |> Set.add chosen)) (Map.empty, Set.empty)
    |> fst

let private sanitizeImportSuffix (name: string) = sanitizeIdentifier name

let private tagPayloadMask = 65535
let private tagDiscriminantMask = -65536

let private tagDiscriminantBase (id: int) = id <<< 16

let private stringDataBaseOffset = 256

let private heapBaseOffset = 4096  // Start heap after string data region

let private dbgDataBaseOffset = 128

let private dbgDigitBufferOffset = dbgDataBaseOffset + 64  // dbg writes digits starting here (192)
let private dbgStrNewlineOffset = dbgDigitBufferOffset + 32  // dbg_str newline at 224 (safe from dbg's buffer)

let private escapeWasmData (bytes: byte array) =
    let sb = StringBuilder()
    for b in bytes do
        let c = char b
        if b >= 0x20uy && b <= 0x7Euy && c <> '"' && c <> '\\' then
            sb.Append c |> ignore
        else
            sb.Append(sprintf "\\%02x" b) |> ignore
    sb.ToString()

let private buildStringLiteralMap (literals: string list) =
    let mutable offset = stringDataBaseOffset
    let entries =
        literals
        |> List.map (fun value ->
            let bytes = Encoding.UTF8.GetBytes value
            let lengthBytes = BitConverter.GetBytes bytes.Length
            let dataBytes = Array.concat [ lengthBytes; bytes; [| 0uy |] ]
            let currentOffset = offset
            offset <- offset + dataBytes.Length
            value, currentOffset, dataBytes)
    let map = entries |> List.map (fun (value, baseOffset, _) -> value, baseOffset + 4) |> Map.ofList
    map, entries

let private collectContextUseBindings (expr: Expression) : (string * Expression) list list =
    // Helper to extract bindings from a record expression
    let extractRecordBindings (fields: RecordField list) =
        fields
        |> List.choose (function
            | NamedField(name, value) -> Some (name, value)
            | PositionalField _ -> None)

    // Collect def bindings in a block that might be used in `use identifier`
    let rec collectDefsInBlock (statements: Statement list) : Map<string, Expression> =
        statements
        |> List.choose (function
            | DefStatement(_, name, _, rhs) -> Some (name, rhs)
            | _ -> None)
        |> Map.ofList

    let rec typeExprToKey (t: TypeExpr) =
        match t with
        | TypeName name -> name
        | TypeVar name -> name
        | TypeApply(name, typeArgs) -> 
            let argsStr = typeArgs |> List.map typeExprToKey |> String.concat "_"
            $"{name}_{argsStr}"
        | _ -> "unknown"

    let rec collectFromStatement (localDefs: Map<string, Expression>) (stmt: Statement) =
        match stmt with
        | UseStatement (UseValue (RecordExpr fields)) ->
            // Direct record expression: use (println = { msg -> () })
            [extractRecordBindings fields]
        | UseStatement (UseValue (IdentifierExpr (name, _))) ->
            // use identifier - look up in local defs
            match localDefs |> Map.tryFind name with
            | Some (RecordExpr fields) -> [extractRecordBindings fields]
            | _ -> []
        | UseStatement (UseValue (FunctionCall(_, _, args))) ->
            match args with
            | [RecordExpr fields] -> [extractRecordBindings fields]
            | _ -> []
        | UseStatement (UseContextInstance(ctxType, RecordExpr fields)) ->
            // Parameterized context instance: use ToString(int) = (toString = ...)
            let ctxKey = typeExprToKey ctxType
            let qualifiedFields = 
                fields
                |> List.choose (function
                    | NamedField(name, value) -> Some ($"{ctxKey}_{name}", value)
                    | PositionalField _ -> None)
            [qualifiedFields]
        | UseStatement (UseContextInstance(_, _)) ->
            // Other parameterized context forms not yet supported
            []
        | DefStatement(_, _, _, rhs) -> collectFromExpr localDefs rhs
        | ExprStatement value -> collectFromExpr localDefs value
        | ImportStatement _
        | TypeDefStatement _ -> []
        | _ -> []
    and collectFromExpr (localDefs: Map<string, Expression>) value =
        match value with
        | Block statements ->
            // Collect local defs first for this block scope
            let blockDefs = collectDefsInBlock statements
            let mergedDefs = Map.fold (fun acc k v -> Map.add k v acc) localDefs blockDefs
            statements |> List.collect (collectFromStatement mergedDefs)
        | IfExpr(cond, thenExpr, elseExpr) ->
            collectFromExpr localDefs cond @ collectFromExpr localDefs thenExpr @ collectFromExpr localDefs elseExpr
        | Match(values, arms) ->
            let valueBindings = values |> List.collect (collectFromExpr localDefs)
            let armBindings = arms |> List.collect (fun (_, armExpr) -> collectFromExpr localDefs armExpr)
            valueBindings @ armBindings
        | Lambda(_, body) -> collectFromExpr localDefs body
        | Pipe(value, _, _, args) ->
            collectFromExpr localDefs value @ (args |> List.collect (collectFromExpr localDefs))
        | FunctionCall(_, _, args) -> args |> List.collect (collectFromExpr localDefs)
        | TupleExpr items
        | ListExpr items -> items |> List.collect (collectFromExpr localDefs)
        | RecordExpr fields ->
            fields
            |> List.collect (function
                | NamedField(_, value) -> collectFromExpr localDefs value
                | PositionalField value -> collectFromExpr localDefs value)
        | UseIn(binding, body) ->
            let bindingBindings =
                match binding with
                | UseValue inner -> collectFromExpr localDefs inner
                | UseContextInstance(_, inner) -> collectFromExpr localDefs inner
            bindingBindings @ collectFromExpr localDefs body
        | TagExpr(_, payload) -> payload |> Option.map (collectFromExpr localDefs) |> Option.defaultValue []
        | MemberAccess(inner, _, _) -> collectFromExpr localDefs inner
        | ContextMemberAccess(_, _) -> []
        | ContextMemberCall(_, _, args) -> args |> List.collect (collectFromExpr localDefs)
        | InterpolatedString parts ->
            parts
            |> List.collect (function
                | StringText _ -> []
                | StringExpr inner -> collectFromExpr localDefs inner)
        | WorkflowBindExpr(_, value)
        | WorkflowReturnExpr value -> collectFromExpr localDefs value
        | UnitExpr
        | LiteralExpr _
        | IdentifierExpr _ -> []
        | _ -> []

    collectFromExpr Map.empty expr

let private maxOrZero list =
    match list with
    | [] -> 0
    | _ -> List.max list

let rec private countMatchExpressions (expr: Expression) : int =
    match expr with
    | Match(values, arms) ->
        let arity = values.Length
        let maxArityInArms =
            arms |> List.map (fun (_, armExpr) -> countMatchArity armExpr) |> List.max
        max arity maxArityInArms
    | FunctionCall(_, _, args) -> args |> List.sumBy countMatchExpressions
    | BinaryOp(_, left, right) -> countMatchExpressions left + countMatchExpressions right
    | IfExpr(cond, thenExpr, elseExpr) ->
        countMatchExpressions cond + countMatchExpressions thenExpr + countMatchExpressions elseExpr
    | Lambda(_, body) -> countMatchExpressions body
    | Pipe(value, _, _, args) -> countMatchExpressions value + (args |> List.sumBy countMatchExpressions)
    | Block statements ->
        statements
        |> List.sumBy (function
            | DefStatement(_, _, _, rhs) -> countMatchExpressions rhs
            | ExprStatement value -> countMatchExpressions value
            | ImportStatement _
            | TypeDefStatement _
            | UseStatement _ -> 0)
    | TupleExpr values -> values |> List.sumBy countMatchExpressions
    | ListExpr values -> values |> List.sumBy countMatchExpressions
    | RecordExpr fields ->
        fields
        |> List.sumBy (function
            | NamedField(_, value) -> countMatchExpressions value
            | PositionalField value -> countMatchExpressions value)
    | UseIn(_, body) -> countMatchExpressions body
    | MemberAccess(value, _, _) -> countMatchExpressions value
    | ContextMemberAccess(_, _) -> 0
    | ContextMemberCall(_, _, args) -> args |> List.sumBy countMatchExpressions
    | TagExpr(_, payload) -> payload |> Option.map countMatchExpressions |> Option.defaultValue 0
    | InterpolatedString parts ->
        parts
        |> List.sumBy (function
            | StringText _ -> 0
            | StringExpr value -> countMatchExpressions value)
    | WorkflowBindExpr(_, value) -> countMatchExpressions value
    | WorkflowReturnExpr value -> countMatchExpressions value
    | UnitExpr -> 0
    | LiteralExpr _ -> 0
    | IdentifierExpr _ -> 0

and countMatchArity (expr: Expression) : int =
    match expr with
    | Match(values, arms) ->
        let arity = values.Length
        let maxArityInArms =
            arms |> List.map (fun (_, armExpr) -> countMatchArity armExpr) |> maxOrZero
        max arity maxArityInArms
    | FunctionCall(_, _, args) -> args |> List.map countMatchArity |> maxOrZero
    | BinaryOp(_, left, right) -> max (countMatchArity left) (countMatchArity right)
    | IfExpr(cond, thenExpr, elseExpr) ->
        [countMatchArity cond; countMatchArity thenExpr; countMatchArity elseExpr] |> List.max
    | Lambda(_, body) -> countMatchArity body
    | Pipe(value, _, _, args) -> max (countMatchArity value) (args |> List.map countMatchArity |> maxOrZero)
    | Block statements ->
        statements
        |> List.map (function
            | DefStatement(_, _, _, rhs) -> countMatchArity rhs
            | ExprStatement value -> countMatchArity value
            | ImportStatement _
            | TypeDefStatement _
            | UseStatement _ -> 0)
        |> maxOrZero
    | TupleExpr values
    | ListExpr values -> values |> List.map countMatchArity |> maxOrZero
    | RecordExpr fields ->
        fields
        |> List.map (function
            | NamedField(_, value) -> countMatchArity value
            | PositionalField value -> countMatchArity value)
        |> maxOrZero
    | UseIn(_, body) -> countMatchArity body
    | MemberAccess(value, _, _) -> countMatchArity value
    | TagExpr(_, payload) -> payload |> Option.map countMatchArity |> Option.defaultValue 0
    | InterpolatedString parts ->
        parts
        |> List.map (function
            | StringText _ -> 0
            | StringExpr value -> countMatchArity value)
        |> maxOrZero
    | WorkflowBindExpr(_, value)
    | WorkflowReturnExpr value -> countMatchArity value
    | ContextMemberAccess(_, _) -> 0
    | ContextMemberCall(_, _, args) -> args |> List.map countMatchArity |> maxOrZero
    | UnitExpr
    | LiteralExpr _
    | IdentifierExpr _ -> 0

let rec private containsDbgCallExpr (expr: Expression) : bool =
    match expr with
    | FunctionCall(name, _, _) when name = "dbg" -> true
    | FunctionCall(_, _, args) -> args |> List.exists containsDbgCallExpr
    | BinaryOp(_, left, right) -> containsDbgCallExpr left || containsDbgCallExpr right
    | IfExpr(cond, thenExpr, elseExpr) ->
        containsDbgCallExpr cond || containsDbgCallExpr thenExpr || containsDbgCallExpr elseExpr
    | Block statements -> containsDbgCallStatements statements
    | Lambda(_, body) -> containsDbgCallExpr body
    | Pipe(value, _, _, args) -> containsDbgCallExpr value || (args |> List.exists containsDbgCallExpr)
    | TupleExpr items
    | ListExpr items -> items |> List.exists containsDbgCallExpr
    | RecordExpr fields ->
        fields
        |> List.exists (function
            | NamedField(_, value) -> containsDbgCallExpr value
            | PositionalField value -> containsDbgCallExpr value)
    | Match(values, arms) ->
        (values |> List.exists containsDbgCallExpr)
        || (arms |> List.exists (fun (_, armExpr) -> containsDbgCallExpr armExpr))
    | UseIn(_, body) -> containsDbgCallExpr body
    | MemberAccess(inner, _, _) -> containsDbgCallExpr inner
    | TagExpr(_, payload) -> payload |> Option.exists containsDbgCallExpr
    | InterpolatedString parts ->
        parts
        |> List.exists (function
            | StringText _ -> false
            | StringExpr inner -> containsDbgCallExpr inner)
    | WorkflowBindExpr(_, value)
    | WorkflowReturnExpr value -> containsDbgCallExpr value
    | ContextMemberAccess(_, _) -> false
    | ContextMemberCall(_, _, args) -> args |> List.exists containsDbgCallExpr
    | UnitExpr
    | LiteralExpr _
    | IdentifierExpr _ -> false

and private containsDbgCallStatements (statements: Statement list) : bool =
    statements
    |> List.exists (function
        | DefStatement(_, _, _, rhs) -> containsDbgCallExpr rhs
        | ExprStatement expr -> containsDbgCallExpr expr
        | ImportStatement _
        | TypeDefStatement _
        | UseStatement _ -> false)

let rec private usesHeapAllocationExpr (expr: Expression) : bool =
    match expr with
    | TupleExpr _
    | ListExpr _
    | RecordExpr _ -> true
    | FunctionCall(_, _, args) -> flattenCallArgs args |> List.exists usesHeapAllocationExpr
    | BinaryOp(_, left, right) -> usesHeapAllocationExpr left || usesHeapAllocationExpr right
    | IfExpr(cond, thenExpr, elseExpr) ->
        usesHeapAllocationExpr cond || usesHeapAllocationExpr thenExpr || usesHeapAllocationExpr elseExpr
    | Block statements -> usesHeapAllocationStatements statements
    | Lambda(_, body) -> usesHeapAllocationExpr body
    | Pipe(value, _, _, args) -> usesHeapAllocationExpr value || (flattenCallArgs args |> List.exists usesHeapAllocationExpr)
    | Match(values, arms) ->
        (values |> List.exists usesHeapAllocationExpr)
        || (arms |> List.exists (fun (_, armExpr) -> usesHeapAllocationExpr armExpr))
    | UseIn(_, body) -> usesHeapAllocationExpr body
    | MemberAccess(inner, _, _) -> usesHeapAllocationExpr inner
    | TagExpr(_, payload) -> payload |> Option.exists usesHeapAllocationExpr
    | InterpolatedString parts ->
        parts
        |> List.exists (function
            | StringText _ -> false
            | StringExpr inner -> usesHeapAllocationExpr inner)
    | WorkflowBindExpr(_, value)
    | WorkflowReturnExpr value -> usesHeapAllocationExpr value
    | ContextMemberAccess(_, _) -> false
    | ContextMemberCall(_, _, args) -> args |> List.exists usesHeapAllocationExpr
    | UnitExpr
    | LiteralExpr _
    | IdentifierExpr _ -> false

and private usesHeapAllocationStatements (statements: Statement list) : bool =
    statements
    |> List.exists (function
        | DefStatement(_, _, _, rhs) -> usesHeapAllocationExpr rhs
        | ExprStatement expr -> usesHeapAllocationExpr expr
        | ImportStatement _
        | TypeDefStatement _
        | UseStatement _ -> false)

let rec private collectDbgTagNamesExpr (expr: Expression) : string list =
    let flattenDbgArgs args =
        match args with
        | [TupleExpr items] -> items
        | _ -> args

    match expr with
    | FunctionCall(name, _, args) when name = "dbg" ->
        match flattenDbgArgs args with
        | [TagExpr(tagName, None)] -> [ tagName ]
        | _ -> []
    | FunctionCall(_, _, args) -> args |> List.collect collectDbgTagNamesExpr
    | BinaryOp(_, left, right) -> collectDbgTagNamesExpr left @ collectDbgTagNamesExpr right
    | IfExpr(cond, thenExpr, elseExpr) ->
        collectDbgTagNamesExpr cond @ collectDbgTagNamesExpr thenExpr @ collectDbgTagNamesExpr elseExpr
    | Lambda(_, body) -> collectDbgTagNamesExpr body
    | Pipe(value, _, _, args) -> collectDbgTagNamesExpr value @ (args |> List.collect collectDbgTagNamesExpr)
    | Block statements -> collectDbgTagNamesStatements statements
    | TupleExpr values
    | ListExpr values -> values |> List.collect collectDbgTagNamesExpr
    | RecordExpr fields ->
        fields
        |> List.collect (function
            | NamedField(_, value) -> collectDbgTagNamesExpr value
            | PositionalField value -> collectDbgTagNamesExpr value)
    | Match(values, arms) ->
        let valueTags = values |> List.collect collectDbgTagNamesExpr
        let armTags = arms |> List.collect (fun (_, armExpr) -> collectDbgTagNamesExpr armExpr)
        valueTags @ armTags
    | UseIn(_, body) -> collectDbgTagNamesExpr body
    | MemberAccess(value, _, _) -> collectDbgTagNamesExpr value
    | TagExpr(_, payload) -> payload |> Option.map collectDbgTagNamesExpr |> Option.defaultValue []
    | InterpolatedString parts ->
        parts
        |> List.collect (function
            | StringText _ -> []
            | StringExpr value -> collectDbgTagNamesExpr value)
    | WorkflowBindExpr(_, value)
    | WorkflowReturnExpr value -> collectDbgTagNamesExpr value
    | ContextMemberAccess(_, _) -> []
    | ContextMemberCall(_, _, args) -> args |> List.collect collectDbgTagNamesExpr
    | UnitExpr
    | LiteralExpr _
    | IdentifierExpr _ -> []

and private collectStringLiteralsExpr (expr: Expression) : string list =
    match expr with
    | LiteralExpr(StringLit value) -> [ value ]
    | Block statements -> collectStringLiteralsStatements statements
    | BinaryOp(_, left, right) -> collectStringLiteralsExpr left @ collectStringLiteralsExpr right
    | IfExpr(cond, thenExpr, elseExpr) ->
        collectStringLiteralsExpr cond @ collectStringLiteralsExpr thenExpr @ collectStringLiteralsExpr elseExpr
    | FunctionCall(_, _, args) -> args |> List.collect collectStringLiteralsExpr
    | Lambda(_, body) -> collectStringLiteralsExpr body
    | Pipe(value, _, _, args) -> collectStringLiteralsExpr value @ (args |> List.collect collectStringLiteralsExpr)
    | TupleExpr items
    | ListExpr items -> items |> List.collect collectStringLiteralsExpr
    | RecordExpr fields ->
        fields
        |> List.collect (function
            | NamedField(_, value) -> collectStringLiteralsExpr value
            | PositionalField value -> collectStringLiteralsExpr value)
    | Match(values, arms) ->
        let valueStrings = values |> List.collect collectStringLiteralsExpr
        let armStrings = arms |> List.collect (fun (_, armExpr) -> collectStringLiteralsExpr armExpr)
        valueStrings @ armStrings
    | UseIn(_, body) -> collectStringLiteralsExpr body
    | MemberAccess(inner, _, _) -> collectStringLiteralsExpr inner
    | TagExpr(_, payload) -> payload |> Option.map collectStringLiteralsExpr |> Option.defaultValue []
    | InterpolatedString _
    | WorkflowBindExpr _
    | WorkflowReturnExpr _
    | ContextMemberAccess(_, _) -> []
    | ContextMemberCall(_, _, args) -> args |> List.collect collectStringLiteralsExpr
    | UnitExpr
    | LiteralExpr _
    | IdentifierExpr _ -> []

and private collectStringLiteralsStatements (statements: Statement list) : string list =
    statements
    |> List.collect (function
        | DefStatement(_, _, _, rhs) -> collectStringLiteralsExpr rhs
        | ExprStatement expr -> collectStringLiteralsExpr expr
        | ImportStatement _
        | TypeDefStatement _
        | UseStatement _ -> [])

and private collectDbgTagNamesStatements (statements: Statement list) : string list =
    statements
    |> List.collect (function
        | DefStatement(_, _, _, value) -> collectDbgTagNamesExpr value
        | ExprStatement value -> collectDbgTagNamesExpr value
        | ImportStatement _
        | TypeDefStatement _
        | UseStatement _ -> [])

let rec private collectDbgTagPayloadNamesExpr (expr: Expression) : string list =
    let flattenDbgArgs args =
        match args with
        | [TupleExpr items] -> items
        | _ -> args

    match expr with
    | FunctionCall(name, _, args) when name = "dbg" ->
        match flattenDbgArgs args with
        | [TagExpr(tagName, Some _)] -> [ tagName ]
        | _ -> []
    | FunctionCall(_, _, args) -> args |> List.collect collectDbgTagPayloadNamesExpr
    | BinaryOp(_, left, right) -> collectDbgTagPayloadNamesExpr left @ collectDbgTagPayloadNamesExpr right
    | IfExpr(cond, thenExpr, elseExpr) ->
        collectDbgTagPayloadNamesExpr cond @ collectDbgTagPayloadNamesExpr thenExpr @ collectDbgTagPayloadNamesExpr elseExpr
    | Lambda(_, body) -> collectDbgTagPayloadNamesExpr body
    | Pipe(value, _, _, args) -> collectDbgTagPayloadNamesExpr value @ (args |> List.collect collectDbgTagPayloadNamesExpr)
    | Block statements -> collectDbgTagPayloadNamesStatements statements
    | TupleExpr values
    | ListExpr values -> values |> List.collect collectDbgTagPayloadNamesExpr
    | RecordExpr fields ->
        fields
        |> List.collect (function
            | NamedField(_, value) -> collectDbgTagPayloadNamesExpr value
            | PositionalField value -> collectDbgTagPayloadNamesExpr value)
    | Match(values, arms) ->
        let valueTags = values |> List.collect collectDbgTagPayloadNamesExpr
        let armTags = arms |> List.collect (fun (_, armExpr) -> collectDbgTagPayloadNamesExpr armExpr)
        valueTags @ armTags
    | UseIn(_, body) -> collectDbgTagPayloadNamesExpr body
    | MemberAccess(value, _, _) -> collectDbgTagPayloadNamesExpr value
    | TagExpr(_, payload) -> payload |> Option.map collectDbgTagPayloadNamesExpr |> Option.defaultValue []
    | InterpolatedString parts ->
        parts
        |> List.collect (function
            | StringText _ -> []
            | StringExpr value -> collectDbgTagPayloadNamesExpr value)
    | WorkflowBindExpr(_, value)
    | WorkflowReturnExpr value -> collectDbgTagPayloadNamesExpr value
    | ContextMemberAccess(_, _) -> []
    | ContextMemberCall(_, _, args) -> args |> List.collect collectDbgTagPayloadNamesExpr
    | UnitExpr
    | LiteralExpr _
    | IdentifierExpr _ -> []

and private collectDbgTagPayloadNamesStatements (statements: Statement list) : string list =
    statements
    |> List.collect (function
        | DefStatement(_, _, _, value) -> collectDbgTagPayloadNamesExpr value
        | ExprStatement value -> collectDbgTagPayloadNamesExpr value
        | ImportStatement _
        | TypeDefStatement _
        | UseStatement _ -> [])

let rec private containsRegularDbgExpr (expr: Expression) : bool =
    let flattenDbgArgs args =
        match args with
        | [TupleExpr items] -> items
        | _ -> args

    match expr with
    | FunctionCall(name, _, args) when name = "dbg" ->
        match flattenDbgArgs args with
        | [TagExpr(_, None)] -> false
        | _ -> true
    | FunctionCall(_, _, args) -> args |> List.exists containsRegularDbgExpr
    | BinaryOp(_, left, right) -> containsRegularDbgExpr left || containsRegularDbgExpr right
    | IfExpr(cond, thenExpr, elseExpr) ->
        containsRegularDbgExpr cond || containsRegularDbgExpr thenExpr || containsRegularDbgExpr elseExpr
    | Lambda(_, body) -> containsRegularDbgExpr body
    | Pipe(value, _, _, args) -> containsRegularDbgExpr value || (args |> List.exists containsRegularDbgExpr)
    | Block statements -> containsRegularDbgStatements statements
    | TupleExpr values
    | ListExpr values -> values |> List.exists containsRegularDbgExpr
    | RecordExpr fields ->
        fields
        |> List.exists (function
            | NamedField(_, value) -> containsRegularDbgExpr value
            | PositionalField value -> containsRegularDbgExpr value)
    | Match(values, arms) ->
        (values |> List.exists containsRegularDbgExpr)
        || (arms |> List.exists (fun (_, armExpr) -> containsRegularDbgExpr armExpr))
    | UseIn(_, body) -> containsRegularDbgExpr body
    | MemberAccess(value, _, _) -> containsRegularDbgExpr value
    | TagExpr(_, payload) -> payload |> Option.exists containsRegularDbgExpr
    | InterpolatedString parts ->
        parts
        |> List.exists (function
            | StringText _ -> false
            | StringExpr value -> containsRegularDbgExpr value)
    | WorkflowBindExpr(_, value)
    | WorkflowReturnExpr value -> containsRegularDbgExpr value
    | ContextMemberAccess(_, _) -> false
    | ContextMemberCall(_, _, args) -> args |> List.exists containsRegularDbgExpr
    | UnitExpr
    | LiteralExpr _
    | IdentifierExpr _ -> false

and private containsDbgStringLiteralExpr (expr: Expression) : bool =
    let flattenDbgArgs args =
        match args with
        | [TupleExpr items] -> items
        | _ -> args

    match expr with
    | FunctionCall(name, _, args) when name = "dbg" ->
        match flattenDbgArgs args with
        | [LiteralExpr(StringLit _)] -> true
        | _ -> false
    | FunctionCall(_, _, args) -> args |> List.exists containsDbgStringLiteralExpr
    | BinaryOp(_, left, right) -> containsDbgStringLiteralExpr left || containsDbgStringLiteralExpr right
    | IfExpr(cond, thenExpr, elseExpr) ->
        containsDbgStringLiteralExpr cond || containsDbgStringLiteralExpr thenExpr || containsDbgStringLiteralExpr elseExpr
    | Lambda(_, body) -> containsDbgStringLiteralExpr body
    | Pipe(value, _, _, args) -> containsDbgStringLiteralExpr value || (args |> List.exists containsDbgStringLiteralExpr)
    | Block statements -> statements |> List.exists (function
        | DefStatement(_, _, _, rhs) -> containsDbgStringLiteralExpr rhs
        | ExprStatement expr -> containsDbgStringLiteralExpr expr
        | ImportStatement _
        | TypeDefStatement _
        | UseStatement _ -> false)
    | TupleExpr values
    | ListExpr values -> values |> List.exists containsDbgStringLiteralExpr
    | RecordExpr fields ->
        fields
        |> List.exists (function
            | NamedField(_, value) -> containsDbgStringLiteralExpr value
            | PositionalField value -> containsDbgStringLiteralExpr value)
    | Match(values, arms) ->
        (values |> List.exists containsDbgStringLiteralExpr)
        || (arms |> List.exists (fun (_, armExpr) -> containsDbgStringLiteralExpr armExpr))
    | UseIn(_, body) -> containsDbgStringLiteralExpr body
    | MemberAccess(value, _, _) -> containsDbgStringLiteralExpr value
    | TagExpr(_, payload) -> payload |> Option.exists containsDbgStringLiteralExpr
    | InterpolatedString parts ->
        parts
        |> List.exists (function
            | StringText _ -> false
            | StringExpr inner -> containsDbgStringLiteralExpr inner)
    | WorkflowBindExpr(_, value)
    | WorkflowReturnExpr value -> containsDbgStringLiteralExpr value
    | ContextMemberAccess(_, _) -> false
    | ContextMemberCall(_, _, args) -> args |> List.exists containsDbgStringLiteralExpr
    | UnitExpr
    | LiteralExpr _
    | IdentifierExpr _ -> false

and private containsRegularDbgStatements (statements: Statement list) : bool =
    statements
    |> List.exists (function
        | DefStatement(_, _, _, value) -> containsRegularDbgExpr value
        | ExprStatement value -> containsRegularDbgExpr value
        | ImportStatement _
        | TypeDefStatement _
        | UseStatement _ -> false)

let rec private collectTagNamesExpr (expr: Expression) : string list =
    match expr with
    | TagExpr(name, payload) ->
        match payload with
        | Some value -> name :: collectTagNamesExpr value
        | None -> [ name ]
    | BinaryOp(_, left, right) -> collectTagNamesExpr left @ collectTagNamesExpr right
    | IfExpr(cond, thenExpr, elseExpr) ->
        collectTagNamesExpr cond @ collectTagNamesExpr thenExpr @ collectTagNamesExpr elseExpr
    | FunctionCall(_, _, args) -> args |> List.collect collectTagNamesExpr
    | Lambda(_, body) -> collectTagNamesExpr body
    | Pipe(value, _, _, args) -> collectTagNamesExpr value @ (args |> List.collect collectTagNamesExpr)
    | Block statements -> collectTagNamesStatements statements
    | TupleExpr values
    | ListExpr values -> values |> List.collect collectTagNamesExpr
    | RecordExpr fields ->
        fields
        |> List.collect (function
            | NamedField(_, value) -> collectTagNamesExpr value
            | PositionalField value -> collectTagNamesExpr value)
    | Match(values, arms) ->
        let valueTags = values |> List.collect collectTagNamesExpr
        let armTags =
            arms
            |> List.collect (fun (patterns, armExpr) ->
                (patterns |> List.collect collectTagNamesPattern) @ (collectTagNamesExpr armExpr))
        valueTags @ armTags
    | UseIn(_, body) -> collectTagNamesExpr body
    | MemberAccess(value, _, _) -> collectTagNamesExpr value
    | InterpolatedString parts ->
        parts
        |> List.collect (function
            | StringText _ -> []
            | StringExpr value -> collectTagNamesExpr value)
    | WorkflowBindExpr(_, value)
    | WorkflowReturnExpr value -> collectTagNamesExpr value
    | ContextMemberAccess(_, _) -> []
    | ContextMemberCall(_, _, args) -> args |> List.collect collectTagNamesExpr
    | UnitExpr
    | LiteralExpr _
    | IdentifierExpr _ -> []

and private collectTagNamesStatements (statements: Statement list) : string list =
    statements
    |> List.collect (function
        | DefStatement(_, _, _, value) -> collectTagNamesExpr value
        | ExprStatement value -> collectTagNamesExpr value
        | ImportStatement _
        | TypeDefStatement _
        | UseStatement _ -> [])

and private collectTagNamesPattern (pattern: Pattern) : string list =
    match pattern with
    | TagPattern(tagName, payloadOpt) ->
        match payloadOpt with
        | Some payload -> tagName :: collectTagNamesPattern payload
        | None -> [ tagName ]
    | TuplePattern patterns
    | ListPattern(patterns, _) -> patterns |> List.collect collectTagNamesPattern
    | ListSplatMiddle(beforePatterns, afterPatterns) ->
        (beforePatterns |> List.collect collectTagNamesPattern)
        @ (afterPatterns |> List.collect collectTagNamesPattern)
    | RecordPattern(_, patterns) -> patterns |> List.collect collectTagNamesPattern
    | RecordMemberPattern members -> members |> List.collect (fun (_, p) -> collectTagNamesPattern p)
    | GuardPattern(_, value)
    | RangePattern(value, _) -> collectTagNamesExpr value
    | LiteralPattern _
    | IdentifierPattern _
    | WildcardPattern
    | ElsePattern -> []

let rec private transpileExpression (env: TranspileEnv) (expr: Expression) : string =
    match expr with
    | UnitExpr ->
        "i32.const 0"
    | LiteralExpr(IntLit value) ->
        $"i32.const {value}"
    | LiteralExpr(BoolLit value) ->
        if value then "i32.const 1" else "i32.const 0"
    | LiteralExpr(StringLit value) ->
        match env.StringLiterals |> Map.tryFind value with
        | Some offset -> $"i32.const {offset}"
        | None -> failwith $"Internal error: missing string literal offset for {value}"
    | IdentifierExpr(name, _) ->
        match env.Locals |> Map.tryFind name with
        | Some localName -> $"local.get ${localName}"
        | None when env.KnownFunctions.Contains name -> $"call ${sanitizeIdentifier name}"
        | None -> failwith $"Unsupported identifier for WASM MVP backend: {name}"
    | TagExpr(name, None) ->
        match env.TagIds |> Map.tryFind name with
        | Some id -> $"i32.const {tagDiscriminantBase id}"
        | None -> failwith $"Internal error: missing tag discriminant for #{name}"
    | TagExpr(name, Some payloadExpr) ->
        let tagBase =
            match env.TagIds |> Map.tryFind name with
            | Some id -> tagDiscriminantBase id
            | None -> failwith $"Internal error: missing tag discriminant for #{name}"
        let payloadCode = transpileExpression env payloadExpr
        $"{payloadCode}\n    i32.const {tagPayloadMask}\n    i32.and\n    i32.const {tagBase}\n    i32.or"
    | BinaryOp(op, left, right) ->
        match wasmBinaryInstruction op with
        | Some instr ->
            let leftCode = transpileExpression env left
            let rightCode = transpileExpression env right
            $"{leftCode}\n    {rightCode}\n    {instr}"
        | None ->
            failwith $"Unsupported binary operator for WASM MVP backend: {op}"
    | IfExpr(cond, thenExpr, elseExpr) ->
        let condCode = transpileExpression env cond
        let thenCode = transpileExpression env thenExpr
        let elseCode = transpileExpression env elseExpr
        $"{condCode}\n    (if (result i32)\n      (then\n        {thenCode}\n      )\n      (else\n        {elseCode}\n      )\n    )"
    | TupleExpr items ->
        // Allocate (4 * n) bytes on the heap for n i32 values
        // Store each item, return pointer to first element
        let n = items.Length
        let sizeBytes = n * 4
        let itemCodes = items |> List.mapi (fun i item ->
            let itemCode = transpileExpression env item
            let offset = i * 4
            // Get heap pointer, add offset, get value, store
            $"global.get $heap_ptr\n    i32.const {offset}\n    i32.add\n    {itemCode}\n    i32.store")
        let storeAll = itemCodes |> String.concat "\n    "
        // Store all items, then push return value (original ptr), then bump heap
        $"{storeAll}\n    global.get $heap_ptr\n    global.get $heap_ptr\n    i32.const {sizeBytes}\n    i32.add\n    global.set $heap_ptr"
    | RecordExpr fields ->
        // Sort fields alphabetically by name, store in that order on heap
        let sortedFields =
            fields
            |> List.mapi (fun i field ->
                match field with
                | NamedField(name, expr) -> (name, expr)
                | PositionalField expr -> (string i, expr))
            |> List.sortBy fst
        let n = sortedFields.Length
        let sizeBytes = n * 4
        let fieldCodes = sortedFields |> List.mapi (fun i (_, expr) ->
            let exprCode = transpileExpression env expr
            let offset = i * 4
            $"global.get $heap_ptr\n    i32.const {offset}\n    i32.add\n    {exprCode}\n    i32.store")
        let storeAll = fieldCodes |> String.concat "\n    "
        $"{storeAll}\n    global.get $heap_ptr\n    global.get $heap_ptr\n    i32.const {sizeBytes}\n    i32.add\n    global.set $heap_ptr"
    | MemberAccess(recordExpr, fieldName, _) ->
        // Support both numeric indices (t.0, t.1) and named fields (r.name)
        match System.Int32.TryParse(fieldName) with
        | true, index ->
            // Numeric index - direct offset calculation
            let recordCode = transpileExpression env recordExpr
            let offset = index * 4
            $"{recordCode}\n    i32.const {offset}\n    i32.add\n    i32.load"
        | false, _ ->
            // Named field - look up layout to find offset
            match recordExpr with
            | IdentifierExpr(varName, _) ->
                match env.RecordLayouts |> Map.tryFind varName with
                | Some fieldNames ->
                    match fieldNames |> List.tryFindIndex ((=) fieldName) with
                    | Some index ->
                        let recordCode = transpileExpression env recordExpr
                        let offset = index * 4
                        $"{recordCode}\n    i32.const {offset}\n    i32.add\n    i32.load"
                    | None ->
                        failwith $"Unknown field '{fieldName}' for record variable '{varName}'"
                | None ->
                    let qualifiedName = $"{varName}.{fieldName}"
                    if env.KnownFunctions.Contains qualifiedName then
                        $"call ${sanitizeIdentifier qualifiedName}"
                    else
                        failwith $"No record layout known for variable '{varName}' when accessing .{fieldName}"
            | RecordExpr fields ->
                // Inline record expression - compute layout directly
                let sortedFieldNames =
                    fields
                    |> List.mapi (fun i field ->
                        match field with
                        | NamedField(name, _) -> name
                        | PositionalField _ -> string i)
                    |> List.sort
                match sortedFieldNames |> List.tryFindIndex ((=) fieldName) with
                | Some index ->
                    let recordCode = transpileExpression env recordExpr
                    let offset = index * 4
                    $"{recordCode}\n    i32.const {offset}\n    i32.add\n    i32.load"
                | None ->
                    failwith $"Unknown field '{fieldName}' in inline record expression"
            | _ ->
                failwith $"Unsupported member access for WASM MVP backend: cannot determine layout for .{fieldName}"
    // ...existing code...
    | Match(values, arms) ->
        // Multi-scrutinee support
        let matchTempNames =
            env.MatchTempNames
            |> List.skip env.NextMatchTemp
            |> List.take values.Length
        if matchTempNames.Length <> values.Length then
            failwith "Internal error: not enough match temp locals for multi-scrutinee match"

        let envForNested = { env with NextMatchTemp = env.NextMatchTemp + values.Length }

        let scrutineeCodes = values |> List.map (transpileExpression envForNested)
        let setScrutineeLocals =
            List.zip matchTempNames scrutineeCodes
            |> List.map (fun (name, code) -> $"{code}\n    local.set ${name}")
            |> String.concat "\n    "

        let patternIsCatchAll pattern =
            match pattern with
            | WildcardPattern
            | ElsePattern
            | IdentifierPattern _ -> true
            | _ -> false

        let patternCondition pattern tempName =
            match pattern with
            | LiteralPattern(IntLit value) ->
                $"local.get ${tempName}\n      i32.const {value}\n      i32.eq"
            | LiteralPattern(BoolLit value) ->
                let asInt = if value then 1 else 0
                $"local.get ${tempName}\n      i32.const {asInt}\n      i32.eq"
            | TagPattern(tagName, payloadOpt) ->
                let tagBase =
                    match env.TagIds |> Map.tryFind tagName with
                    | Some id -> tagDiscriminantBase id
                    | None -> failwith $"Internal error: missing tag discriminant for pattern #{tagName}"
                match payloadOpt with
                | None ->
                    $"local.get ${tempName}\n      i32.const {tagDiscriminantMask}\n      i32.and\n      i32.const {tagBase}\n      i32.eq"
                | Some (LiteralPattern(IntLit payload)) ->
                    let payloadMasked = payload &&& tagPayloadMask
                    $"local.get ${tempName}\n      i32.const {tagDiscriminantMask}\n      i32.and\n      i32.const {tagBase}\n      i32.eq\n      local.get ${tempName}\n      i32.const {tagPayloadMask}\n      i32.and\n      i32.const {payloadMasked}\n      i32.eq\n      i32.and"
                | Some (LiteralPattern(BoolLit payload)) ->
                    let payloadMasked = if payload then 1 else 0
                    $"local.get ${tempName}\n      i32.const {tagDiscriminantMask}\n      i32.and\n      i32.const {tagBase}\n      i32.eq\n      local.get ${tempName}\n      i32.const {tagPayloadMask}\n      i32.and\n      i32.const {payloadMasked}\n      i32.eq\n      i32.and"
                | Some WildcardPattern
                | Some ElsePattern ->
                    $"local.get ${tempName}\n      i32.const {tagDiscriminantMask}\n      i32.and\n      i32.const {tagBase}\n      i32.eq"
                | Some (IdentifierPattern ident) ->
                    $"local.get ${tempName}\n      i32.const {tagDiscriminantMask}\n      i32.and\n      i32.const {tagBase}\n      i32.eq"
                | Some _ ->
                    failwith $"Unsupported tag payload pattern for WASM MVP backend: {tagName}"
            | GuardPattern(op, guardExpr) ->
                match wasmBinaryInstruction op with
                | Some instr ->
                    let guardCode = transpileExpression env guardExpr
                    $"local.get ${tempName}\n      {guardCode}\n      {instr}"
                | None ->
                    failwith $"Unsupported guard operator for WASM MVP backend: {op}"
            | WildcardPattern
            | ElsePattern
            | IdentifierPattern _ -> "i32.const 1"
            | _ -> failwith $"Unsupported match pattern for WASM MVP backend: {pattern}"

        let rec emitArms remainingArms =
            match remainingArms with
            | [] -> "unreachable"
            | (patterns, armExpr) :: rest ->
                if List.length patterns <> values.Length then
                    failwith "Unsupported match arm: pattern count does not match scrutinee count"
                let conds : string list =
                    List.zip patterns matchTempNames
                    |> List.map (fun (pat, temp) -> patternCondition pat temp)
                let allConds =
                    match conds with
                    | [] -> "i32.const 1"
                    | [single] -> single
                    | many ->
                        let many : string list = many
                        let n = many.Length
                        many |> String.concat "\n      " |> fun s -> $"{s}\n      " + (List.init (n - 1) (fun _ -> "i32.and") |> String.concat "\n      ")
                // Add identifier payloads to envForNested.Locals before binding
                let payloadIdents =
                    patterns
                    |> List.choose (function
                        | TagPattern(_, Some (IdentifierPattern ident)) -> Some ident
                        | _ -> None)
                let envWithPayloadLocals =
                    payloadIdents
                    |> List.fold (fun acc ident ->
                        let localName = $"__payload_{ident}"
                        { acc with Locals = acc.Locals |> Map.add ident localName }) envForNested
                let patternPayloadBinds =
                    List.zip patterns matchTempNames
                    |> List.choose (fun (pat, temp) ->
                        match pat with
                        | TagPattern(_, Some (IdentifierPattern ident)) ->
                            let payloadLocal =
                                match envWithPayloadLocals.Locals |> Map.tryFind ident with
                                | Some localName -> localName
                                | None -> failwith $"Internal error: missing local for payload variable {ident}"
                            Some $"local.get ${temp}\n        i32.const {tagPayloadMask}\n        i32.and\n        local.set ${payloadLocal}"
                        | _ -> None)
                    |> String.concat "\n    "
                let armCode =
                    if patternPayloadBinds = "" then
                        transpileExpression envWithPayloadLocals armExpr
                    else
                        $"{patternPayloadBinds}\n    {transpileExpression envWithPayloadLocals armExpr}"
                let elseCode = emitArms rest
                $"{allConds}\n    (if (result i32)\n      (then\n        {armCode}\n      )\n      (else\n        {elseCode}\n      )\n    )"

        let armsCode = emitArms arms
        $"{setScrutineeLocals}\n    {armsCode}"
    // ...existing code...
    | FunctionCall(name, _, args) when name = "dbg" ->
        let dbgLocal =
            match env.DbgTempLocal with
            | Some localName -> localName
            | None -> failwith "Internal error: dbg local is not configured for WASM backend"
        let argExpr =
            match flattenCallArgs args with
            | [single] -> single
            | _ -> failwith "dbg expects a single argument"
        // Helper to detect if expression is a string-typed member access or param
        let isStringTypedExpr expr =
            match expr with
            | IdentifierExpr(varName, _) ->
                match env.ParamTypes |> Map.tryFind varName with
                | Some "string" -> true
                | _ -> false
            | MemberAccess(IdentifierExpr(varName, _), fieldName, _) ->
                match env.RecordFieldTypes |> Map.tryFind varName with
                | Some fieldTypes ->
                    match fieldTypes |> Map.tryFind fieldName with
                    | Some "string" -> true
                    | _ -> false
                | None -> false
            | _ -> false
        let argCode =
            match argExpr with
            | TagExpr(tagName, None) ->
                let tagId =
                    match env.TagIds |> Map.tryFind tagName with
                    | Some id -> id
                    | None -> failwith $"Internal error: missing tag discriminant for #{tagName}"
                let importFn = $"dbg_tag_{sanitizeImportSuffix tagName}"
                let tagBase = tagDiscriminantBase tagId
                $"i32.const {tagBase}\n    local.set ${dbgLocal}\n    call ${importFn}\n    local.get ${dbgLocal}"
            | TagExpr(tagName, Some payloadExpr) ->
                let tagId =
                    match env.TagIds |> Map.tryFind tagName with
                    | Some id -> id
                    | None -> failwith $"Internal error: missing tag discriminant for #{tagName}"
                let importFn = $"dbg_tag_payload_{sanitizeImportSuffix tagName}"
                let tagBase = tagDiscriminantBase tagId
                let payloadCode = transpileExpression env payloadExpr
                $"{payloadCode}\n    local.tee ${dbgLocal}\n    call ${importFn}\n    local.get ${dbgLocal}\n    i32.const {tagPayloadMask}\n    i32.and\n    i32.const {tagBase}\n    i32.or"
            | LiteralExpr(BoolLit value) -> if value then "i32.const 1" else "i32.const 0"
            | _ -> transpileExpression env argExpr
        match argExpr with
        | TagExpr _ -> argCode
        | LiteralExpr(StringLit _) -> $"{argCode}\n    local.tee ${dbgLocal}\n    call $dbg_str\n    local.get ${dbgLocal}"
        | _ when isStringTypedExpr argExpr -> $"{argCode}\n    local.tee ${dbgLocal}\n    call $dbg_str\n    local.get ${dbgLocal}"
        | _ -> $"{argCode}\n    local.tee ${dbgLocal}\n    call $dbg\n    local.get ${dbgLocal}"
    | FunctionCall(name, _, args) ->
        // Check if this is a type constructor call (e.g., Person(...))
        match env.TypeLayouts |> Map.tryFind name with
        | Some typeFieldNames ->
            // Treat as record expression - extract fields from args
            let flatArgs = flattenCallArgs args
            match flatArgs with
            | [RecordExpr fields] ->
                // Sort fields alphabetically by name and store on heap
                let sortedFields =
                    fields
                    |> List.mapi (fun i field ->
                        match field with
                        | NamedField(fieldName, expr) -> (fieldName, expr)
                        | PositionalField expr -> (string i, expr))
                    |> List.sortBy fst
                let n = sortedFields.Length
                let sizeBytes = n * 4
                let fieldCodes = sortedFields |> List.mapi (fun i (_, expr) ->
                    let exprCode = transpileExpression env expr
                    let offset = i * 4
                    $"global.get $heap_ptr\n    i32.const {offset}\n    i32.add\n    {exprCode}\n    i32.store")
                let storeAll = fieldCodes |> String.concat "\n    "
                $"{storeAll}\n    global.get $heap_ptr\n    global.get $heap_ptr\n    i32.const {sizeBytes}\n    i32.add\n    global.set $heap_ptr"
            | _ ->
                failwith $"Type constructor {name} expects a record expression as argument"
        | None ->
            let flatArgs = flattenCallArgs args
            let expectedArity = env.FunctionArities |> Map.tryFind name |> Option.defaultValue flatArgs.Length
            
            // Check if we need to destructure a tuple/record argument
            let argsCode =
                match flatArgs with
                | [IdentifierExpr(varName, _)] when expectedArity > 1 ->
                    // Single identifier passed to multi-param function - try to destructure
                    match env.RecordLayouts |> Map.tryFind varName with
                    | Some fieldNames when fieldNames.Length = expectedArity ->
                        // Generate code to load each field from the record
                        let localName = env.Locals |> Map.tryFind varName |> Option.defaultValue (sanitizeIdentifier varName)
                        fieldNames
                        |> List.mapi (fun i _ ->
                            let offset = i * 4
                            $"local.get ${localName}\n    i32.const {offset}\n    i32.add\n    i32.load")
                    | _ ->
                        // Not a known record, just pass it
                        flatArgs |> List.map (transpileExpression env)
                | _ ->
                    flatArgs |> List.map (transpileExpression env)
            
            match env.ContextFunctions |> Map.tryFind name with
            | Some ctxFn ->
                let prefix = argsCode |> String.concat "\n    "
                let slot = ctxFnSlot ctxFn
                let slotCode = 
                    if slot = -1 then
                        // Slot comes from a parameter - look up in ContextParams
                        match env.ContextParams |> Map.tryFind name with
                        | Some paramName -> $"local.get ${paramName}"
                        | None -> failwith $"Context function {name} not found in params"
                    else
                        $"i32.const {slot}"
                if prefix = "" then
                    $"{slotCode}\n    call_indirect (type $ctx_fn_{ctxFnArity ctxFn})"
                else
                    $"{prefix}\n    {slotCode}\n    call_indirect (type $ctx_fn_{ctxFnArity ctxFn})"
            | None ->
                if not (env.KnownFunctions.Contains name) then
                    failwith $"Unsupported function call for WASM MVP backend: {name}"
                else
                    // Check if this function requires contexts - if so, pass them
                    let requiredCtxFns = env.FunctionContextRequirements |> Map.tryFind name |> Option.defaultValue []
                    
                    // Helper to find a compatible context for a required context function, excluding already-used ones
                    // Required might be "ToString_a_toString" (with type param), available might be "ToString_int_toString"
                    let findCompatibleContext (usedContexts: Set<string>) (requiredCtxFnName: string) : string option =
                        // Extract base context name and member from qualified key like "ToString_a_toString"
                        let parts = requiredCtxFnName.Split('_')
                        if parts.Length < 2 then None
                        else
                            let baseName = parts.[0]
                            let memberName = parts.[parts.Length - 1]
                            // Look for an available context with same base name and member name, not already used
                            let allAvailable = 
                                (env.ContextFunctions |> Map.toList |> List.map fst) @
                                (env.ContextParams |> Map.toList |> List.map fst)
                            allAvailable
                            |> List.tryFind (fun availKey ->
                                not (usedContexts.Contains availKey) &&
                                let availParts = availKey.Split('_')
                                availParts.Length >= 2 &&
                                availParts.[0] = baseName &&
                                availParts.[availParts.Length - 1] = memberName)
                    
                    // Use fold to process required contexts while tracking which have been used
                    let (ctxArgsCode, _) =
                        requiredCtxFns
                        |> List.fold (fun (codeAcc, usedCtxs) ctxFnName ->
                            // Try exact match first, then try compatible match
                            let resolvedName = 
                                match env.ContextFunctions |> Map.tryFind ctxFnName, env.ContextParams |> Map.tryFind ctxFnName with
                                | Some _, _ | _, Some _ -> ctxFnName
                                | None, None ->
                                    match findCompatibleContext usedCtxs ctxFnName with
                                    | Some compatible -> compatible
                                    | None -> ctxFnName  // Will fail below with better error
                            
                            // Get the context value - either from ContextFunctions (use statement) or ContextParams (passed in)
                            let code = 
                                match env.ContextFunctions |> Map.tryFind resolvedName with
                                | Some ctxFn when ctxFnSlot ctxFn >= 0 ->
                                    // Fixed slot from a use statement
                                    $"i32.const {ctxFnSlot ctxFn}"
                                | Some _ ->
                                    // Slot from param - look in ContextParams
                                    match env.ContextParams |> Map.tryFind resolvedName with
                                    | Some paramName -> $"local.get ${paramName}"
                                    | None -> failwith $"Context function {resolvedName} not found"
                                | None ->
                                    // Not in ContextFunctions - check ContextParams
                                    match env.ContextParams |> Map.tryFind resolvedName with
                                    | Some paramName -> $"local.get ${paramName}"
                                    | None -> 
                                        let available = 
                                            (env.ContextFunctions |> Map.toList |> List.map fst) @
                                            (env.ContextParams |> Map.toList |> List.map fst)
                                        let availableStr = String.concat ", " available
                                        failwith $"Context function {ctxFnName} not available in current scope. Available: [{availableStr}]"
                            (codeAcc @ [code], usedCtxs.Add resolvedName)) ([], Set.empty)
                    let allArgsCode = argsCode @ ctxArgsCode
                    match allArgsCode with
                    | [] -> $"call ${sanitizeIdentifier name}"
                    | _ ->
                        let prefix = allArgsCode |> String.concat "\n    "
                        $"{prefix}\n    call ${sanitizeIdentifier name}"
    | ContextMemberCall(ctxType, memberName, args) ->
        // Context member call: C(T).f(args) - look up the param for this specific context instantiation
        let rec typeExprToKey (t: TypeExpr) =
            match t with
            | TypeName name -> name
            | TypeVar name -> name
            | TypeApply(name, typeArgs) -> 
                let argsStr = typeArgs |> List.map typeExprToKey |> String.concat "_"
                $"{name}_{argsStr}"
            | _ -> failwith $"Unsupported type expression in context access: {t}"
        let ctxKey = typeExprToKey ctxType
        let paramKey = $"{ctxKey}_{memberName}"
        let flatArgs = flattenCallArgs args
        let argsCode = flatArgs |> List.map (transpileExpression env)
        // Look up the context param for this qualified member
        match env.ContextParams |> Map.tryFind paramKey with
        | Some paramName ->
            let prefix = argsCode |> String.concat "\n    "
            if prefix = "" then
                $"local.get ${paramName}\n    call_indirect (type $ctx_fn_{flatArgs.Length})"
            else
                $"{prefix}\n    local.get ${paramName}\n    call_indirect (type $ctx_fn_{flatArgs.Length})"
        | None ->
            // Also check in ContextFunctions for direct use statements
            match env.ContextFunctions |> Map.tryFind paramKey with
            | Some ctxFn ->
                let prefix = argsCode |> String.concat "\n    "
                let slot = ctxFnSlot ctxFn
                if prefix = "" then
                    $"i32.const {slot}\n    call_indirect (type $ctx_fn_{ctxFnArity ctxFn})"
                else
                    $"{prefix}\n    i32.const {slot}\n    call_indirect (type $ctx_fn_{ctxFnArity ctxFn})"
            | None ->
                failwith $"Context member {memberName} from {ctxKey} not found in scope. Available: {env.ContextParams |> Map.toList |> List.map fst}, {env.ContextFunctions |> Map.toList |> List.map fst}"
    | Pipe(value, funcName, _, args) ->
        // Pipe x \f(a, b) translates to f(x, a, b)
        let valueCode = transpileExpression env value
        let argsCode =
            flattenCallArgs args
            |> List.map (transpileExpression env)
        let allArgsCode = valueCode :: argsCode
        match env.ContextFunctions |> Map.tryFind funcName with
        | Some ctxFn ->
            let prefix = allArgsCode |> String.concat "\n    "
            $"{prefix}\n    i32.const {ctxFnSlot ctxFn}\n    call_indirect (type $ctx_fn_{ctxFnArity ctxFn})"
        | None ->
            if not (env.KnownFunctions.Contains funcName) then
                failwith $"Unsupported pipe target for WASM MVP backend: {funcName}"
            else
                let prefix = allArgsCode |> String.concat "\n    "
                $"{prefix}\n    call ${sanitizeIdentifier funcName}"
    | Lambda(args, body) when args.IsEmpty ->
        transpileExpression env body
    | Block statements ->
        // Identify defs that are only used in `use` statements - skip generating code for them
        let contextOnlyDefs = collectContextOnlyDefNames statements
        let rec transpileStatements env items =
            match items with
            | [] -> failwith "Unsupported block form for WASM MVP backend: block does not end in expression"
            | [ExprStatement expr] -> transpileExpression env expr
            | DefStatement(_, name, _, _) :: tail when contextOnlyDefs |> Set.contains name ->
                // Skip context-only defs - lambdas are hoisted to context functions
                transpileStatements env tail
            | DefStatement(_, name, _, rhs) :: tail ->
                match env.Locals |> Map.tryFind name with
                | Some localName ->
                    let rhsCode = transpileExpression env rhs
                    // Track record layout if rhs is a RecordExpr
                    // Helper to infer type from literal
                    let inferTypeFromExpr expr =
                        match expr with
                        | LiteralExpr(StringLit _) -> "string"
                        | LiteralExpr(IntLit _) -> "int"
                        | LiteralExpr(BoolLit _) -> "bool"
                        | _ -> "unknown"

                    let updatedEnv =
                        match rhs with
                        | RecordExpr fields ->
                            let fieldInfo =
                                fields
                                |> List.mapi (fun i field ->
                                    match field with
                                    | NamedField(n, expr) -> (n, inferTypeFromExpr expr)
                                    | PositionalField expr -> (string i, inferTypeFromExpr expr))
                            let sortedFieldNames = fieldInfo |> List.map fst |> List.sort
                            let fieldTypes = fieldInfo |> Map.ofList
                            { env with
                                RecordLayouts = env.RecordLayouts |> Map.add name sortedFieldNames
                                RecordFieldTypes = env.RecordFieldTypes |> Map.add name fieldTypes }
                        | TupleExpr items ->
                            let fieldNames = items |> List.mapi (fun i _ -> string i)
                            let fieldTypes = items |> List.mapi (fun i expr -> (string i, inferTypeFromExpr expr)) |> Map.ofList
                            { env with
                                RecordLayouts = env.RecordLayouts |> Map.add name fieldNames
                                RecordFieldTypes = env.RecordFieldTypes |> Map.add name fieldTypes }
                        | FunctionCall(typeName, _, _) when env.TypeLayouts |> Map.containsKey typeName ->
                            // Type constructor call - use the type's field types
                            let typeFieldTypes = env.TypeFieldTypes |> Map.tryFind typeName |> Option.defaultValue Map.empty
                            let typeLayout = env.TypeLayouts |> Map.tryFind typeName |> Option.defaultValue []
                            { env with
                                RecordLayouts = env.RecordLayouts |> Map.add name typeLayout
                                RecordFieldTypes = env.RecordFieldTypes |> Map.add name typeFieldTypes }
                        | _ -> env
                    let restCode = transpileStatements updatedEnv tail
                    $"{rhsCode}\n    local.set ${localName}\n    {restCode}"
                | None ->
                    failwith $"Unsupported block local for WASM MVP backend: {name}"
            | ExprStatement expr :: tail ->
                let exprCode = transpileExpression env expr
                let restCode = transpileStatements env tail
                $"{exprCode}\n    drop\n    {restCode}"
            | UseStatement _ :: tail ->
                let pending = env.PendingUseBindings.Value
                match pending with
                | [] ->
                    failwith "Use statement encountered without collected bindings"
                | nextBindings :: rest ->
                    env.PendingUseBindings.Value <- rest
                    let updatedEnv =
                        nextBindings
                        |> Map.fold (fun acc name ctxFn ->
                            { acc with ContextFunctions = acc.ContextFunctions |> Map.add name ctxFn }) env
                    transpileStatements updatedEnv tail
            | ImportStatement _ :: _
            | TypeDefStatement _ :: _ ->
                failwith "Unsupported statement form for WASM MVP backend"
        transpileStatements env statements
    | _ ->
        failwith $"Unsupported expression for WASM MVP backend: {expr}"

let private defBodyAndParameters (expr: Expression) =
    match expr with
    | Lambda(args, body) -> args, body
    | _ -> [], expr

let private extractTypeLayouts (module': Module) : Map<string, string list> * Map<string, Map<string, string>> =
    let rec typeExprToTypeString (t: TypeExpr) : string =
        match t with
        | TypeName name -> name
        | TypeFunc(param, ret) -> $"({typeExprToTypeString param}) -> ({typeExprToTypeString ret})"
        | TypeUnit -> "()"
        | TypeTuple items -> items |> List.map typeExprToTypeString |> String.concat ", " |> sprintf "(%s)"
        | _ -> "unknown"
    let results =
        module'
        |> List.choose (function
            | Def (TypeDef(name, _, _, TypeRecord fields)) ->
                let fieldInfo =
                    fields
                    |> List.choose (function
                        | TypeField(fieldName, _, _, typeOpt, _) ->
                            let typeName =
                                match typeOpt with
                                | Some t -> typeExprToTypeString t
                                | _ -> "unknown"
                            Some (fieldName, typeName)
                        | TypeMember(fieldName, typeOpt) ->
                            let typeName =
                                match typeOpt with
                                | Some t -> typeExprToTypeString t
                                | _ -> "unknown"
                            Some (fieldName, typeName))
                let sortedFieldNames = fieldInfo |> List.map fst |> List.sort
                let fieldTypeMap = fieldInfo |> Map.ofList
                Some (name, sortedFieldNames, fieldTypeMap)
            | _ -> None)
    let layouts = results |> List.map (fun (n, fields, _) -> (n, fields)) |> Map.ofList
    let fieldTypes = results |> List.map (fun (n, _, types) -> (n, types)) |> Map.ofList
    layouts, fieldTypes

let private getLayoutFromTypeExpr (typeLayouts: Map<string, string list>) (typeFieldTypes: Map<string, Map<string, string>>) (typeExpr: TypeExpr option) : (string list * Map<string, string>) option =
    match typeExpr with
    | Some (TypeName typeName) ->
        match typeLayouts |> Map.tryFind typeName, typeFieldTypes |> Map.tryFind typeName with
        | Some layout, Some types -> Some (layout, types)
        | Some layout, None -> Some (layout, Map.empty)
        | _ -> None
    | Some (TypeRecord fields) ->
        let fieldInfo =
            fields
            |> List.choose (function
                | TypeField(fieldName, _, _, typeOpt, _) ->
                    let typeName =
                        match typeOpt with
                        | Some (TypeName t) -> t
                        | _ -> "unknown"
                    Some (fieldName, typeName)
                | TypeMember(fieldName, typeOpt) ->
                    let typeName =
                        match typeOpt with
                        | Some (TypeName t) -> t
                        | _ -> "unknown"
                    Some (fieldName, typeName))
        let fieldNames = fieldInfo |> List.map fst |> List.sort
        let fieldTypes = fieldInfo |> Map.ofList
        Some (fieldNames, fieldTypes)
    | _ -> None

let transpileModuleToWat (module': Module) : string =
    let defs =
        module'
        |> List.choose (function
            | Def (ValueDef(_, name, typeOpt, expr)) -> Some(name, typeOpt, expr)
            | _ -> None)

    let topLevelExprs =
        module'
        |> List.choose (function
            | Expr expr -> Some expr
            | _ -> None)

    let typeLayouts, typeFieldTypes = extractTypeLayouts module'

    let mutable nextContextIndex = 0
    let mutable contextDefs: (string * TypeExpr option * Expression) list = []
    let mutable contextFnList: ContextFn list = []
    let contextBindingsByDef =
        defs
        |> List.map (fun (defName, _, expr) ->
            let bindings = collectContextUseBindings expr
            let bindingMaps =
                bindings
                |> List.map (fun bindingList ->
                    bindingList
                    |> List.map (fun (bindingName, valueExpr) ->
                        match valueExpr with
                        | Lambda(args, body) ->
                            let fnName = $"__ctx_{sanitizeIdentifier defName}_{sanitizeIdentifier bindingName}_{nextContextIndex}"
                            let ctxFn: ContextFn = fnName, nextContextIndex, args.Length
                            nextContextIndex <- nextContextIndex + 1
                            // Lookup the type of this context function from type definitions
                            let ctxTypeOpt =
                                typeFieldTypes
                                |> Map.toSeq
                                |> Seq.tryPick (fun (_, fields) ->
                                    fields |> Map.tryFind bindingName)
                                |> Option.bind (fun typeString ->
                                    // Parse the type string like "(string) -> (())" into a TypeExpr
                                    // Check if the param type contains "string"
                                    if typeString.Contains("(string)") then Some (TypeFunc(TypeName "string", TypeUnit))
                                    elif typeString.Contains("(int)") then Some (TypeFunc(TypeName "int", TypeUnit))
                                    else None)
                            contextDefs <- contextDefs @ [fnName, ctxTypeOpt, Lambda(args, body)]
                            contextFnList <- contextFnList @ [ctxFn]
                            bindingName, ctxFn
                        | _ ->
                            failwith $"Unsupported context binding for {bindingName}: only lambdas are supported in WASM MVP")
                    |> Map.ofList)
            defName, bindingMaps)
        |> Map.ofList

    let knownFunctions =
        (defs |> List.map (fun (n, _, _) -> n)) @ (contextDefs |> List.map (fun (n, _, _) -> n))
        |> Set.ofList

    let functionArities =
        defs
        |> List.map (fun (name, _, expr) ->
            let fnParams, _ = defBodyAndParameters expr
            name, fnParams.Length)
        |> Map.ofList

    // Calculate context requirements for each function based on TypeWithContext annotations
    // For parameterized contexts like ToString(a), generate qualified keys like "ToString_a_toString"
    // For simple contexts like Println, keep simple member names like "println"
    let functionContextRequirements =
        defs
        |> List.choose (fun (name, typeOpt, _) ->
            let contextTypes = extractContextTypes typeOpt
            if contextTypes.IsEmpty then None
            else
                // For each context type, get its field names and generate appropriate keys
                let contextFnNames =
                    contextTypes
                    |> List.collect (fun ctxType ->
                        let baseName = match ctxType with
                                       | TypeName n -> n
                                       | TypeApply(n, _) -> n
                                       | _ -> "unknown"
                        let needsQualified = match ctxType with
                                             | TypeApply _ -> true  // Parameterized - need qualified
                                             | _ -> false           // Simple - keep simple names
                        typeLayouts |> Map.tryFind baseName 
                        |> Option.defaultValue []
                        |> List.map (fun memberName ->
                            if needsQualified then
                                let ctxKey = typeExprToKey ctxType
                                $"{ctxKey}_{memberName}"
                            else
                                memberName))
                if contextFnNames.IsEmpty then None
                else Some (name, contextFnNames))
        |> Map.ofList

    let moduleUsesDbg = 
        (defs |> List.exists (fun (_, _, expr) -> containsDbgCallExpr expr)) ||
        (contextDefs |> List.exists (fun (_, _, expr) -> containsDbgCallExpr expr)) ||
        (topLevelExprs |> List.exists containsDbgCallExpr)
    let moduleUsesRegularDbg = 
        (defs |> List.exists (fun (_, _, expr) -> containsRegularDbgExpr expr)) ||
        (contextDefs |> List.exists (fun (_, _, expr) -> containsRegularDbgExpr expr)) ||
        (topLevelExprs |> List.exists containsRegularDbgExpr)
    let moduleUsesDbgString = 
        (defs |> List.exists (fun (_, _, expr) -> containsDbgStringLiteralExpr expr)) ||
        (contextDefs |> List.exists (fun (_, _, expr) -> containsDbgStringLiteralExpr expr)) ||
        (topLevelExprs |> List.exists containsDbgStringLiteralExpr)
    let moduleUsesHeap = 
        (defs |> List.exists (fun (_, _, expr) -> usesHeapAllocationExpr expr)) ||
        (contextDefs |> List.exists (fun (_, _, expr) -> usesHeapAllocationExpr expr)) ||
        (topLevelExprs |> List.exists usesHeapAllocationExpr)
    let dbgTagNames =
        (defs |> List.collect (fun (_, _, expr) -> collectDbgTagNamesExpr expr)) @
        (contextDefs |> List.collect (fun (_, _, expr) -> collectDbgTagNamesExpr expr))
        @ (topLevelExprs |> List.collect collectDbgTagNamesExpr)
        |> List.distinct
        |> List.sort
    let dbgTagPayloadNames =
        (defs |> List.collect (fun (_, _, expr) -> collectDbgTagPayloadNamesExpr expr)) @
        (contextDefs |> List.collect (fun (_, _, expr) -> collectDbgTagPayloadNamesExpr expr))
        @ (topLevelExprs |> List.collect collectDbgTagPayloadNamesExpr)
        |> List.distinct
        |> List.sort
    let tagIdMap =
        (defs |> List.collect (fun (_, _, expr) -> collectTagNamesExpr expr)) @
        (contextDefs |> List.collect (fun (_, _, expr) -> collectTagNamesExpr expr))
        @ (topLevelExprs |> List.collect collectTagNamesExpr)
        |> List.distinct
        |> List.sort
        |> List.mapi (fun index name -> name, index + 1)
        |> Map.ofList

    let stringLiterals =
        (defs |> List.collect (fun (_, _, expr) -> collectStringLiteralsExpr expr)) @
        (contextDefs |> List.collect (fun (_, _, expr) -> collectStringLiteralsExpr expr))
        @ (topLevelExprs |> List.collect collectStringLiteralsExpr)
        |> List.distinct

    let stringLiteralMap, stringLiteralData = buildStringLiteralMap stringLiterals

    let renderFunction (name: string) (defTypeOpt: TypeExpr option) (expr: Expression) (exportFn: bool) (pendingUseBindings: Map<string, ContextFn> list) =
            let parameters, bodyExpr = defBodyAndParameters expr
            let parameterNames = parameters |> List.map fst
            let parameterMap = createNameMap parameterNames
            let localNames =
                match bodyExpr with
                | Block statements ->
                    collectBlockLocalNames statements
                    |> List.filter (fun n -> not (parameterMap |> Map.containsKey n))
                | _ -> []
            let localMap =
                localNames
                |> List.fold (fun acc name ->
                    if acc |> Map.containsKey name then acc
                    else
                        let baseName = sanitizeIdentifier name
                        let mutable candidate = baseName
                        let mutable index = 1
                        while (parameterMap |> Map.exists (fun _ v -> v = candidate)) || (acc |> Map.exists (fun _ v -> v = candidate)) do
                            candidate <- $"{baseName}_{index}"
                            index <- index + 1
                        acc |> Map.add name candidate) Map.empty
            let usesDbg = containsDbgCallExpr bodyExpr
            let dbgLocalName =
                if usesDbg then
                    let baseName = "__dbg_tmp"
                    let mutable candidate = baseName
                    let mutable index = 1
                    while (parameterMap |> Map.exists (fun _ v -> v = candidate)) || (localMap |> Map.exists (fun _ v -> v = candidate)) do
                        candidate <- $"{baseName}_{index}"
                        index <- index + 1
                    Some candidate
                else
                    None
            let locals = parameterMap |> Map.fold (fun acc k v -> acc |> Map.add k v) localMap
            let mutable usedLocalNames =
                locals
                |> Map.toList
                |> List.map snd
                |> Set.ofList

            let matchArity = countMatchArity bodyExpr
            let matchTempNames =
                [ for index in 0 .. (matchArity - 1) do
                    let baseName = $"__match_tmp_{index}"
                    let mutable candidate = baseName
                    let mutable suffix = 1
                    while usedLocalNames |> Set.contains candidate do
                        candidate <- $"{baseName}_{suffix}"
                        suffix <- suffix + 1
                    usedLocalNames <- usedLocalNames |> Set.add candidate
                    yield candidate ]

            // Initialize RecordLayouts from def-level type annotations
            // Extract parameter types from TypeFunc chain: (A -> B -> C) means params are A, B
            let rec extractParamTypes (t: TypeExpr) : TypeExpr list =
                match t with
                | TypeFunc(param, ret) -> param :: extractParamTypes ret
                | _ -> []

            let defParamTypes =
                match defTypeOpt with
                | Some t -> extractParamTypes t
                | None -> []

            let initialRecordInfo =
                List.zip parameterNames (defParamTypes @ List.replicate (parameterNames.Length - defParamTypes.Length) (TypeUnit))
                |> List.choose (fun (paramName, paramType) ->
                    match getLayoutFromTypeExpr typeLayouts typeFieldTypes (Some paramType) with
                    | Some (fieldNames, fieldTypes) -> Some (paramName, fieldNames, fieldTypes)
                    | None ->
                        // Also try lambda parameter type annotation
                        match parameters |> List.tryFind (fun (n, _) -> n = paramName) with
                        | Some (_, paramTypeOpt) ->
                            getLayoutFromTypeExpr typeLayouts typeFieldTypes paramTypeOpt
                            |> Option.map (fun (fieldNames, fieldTypes) -> (paramName, fieldNames, fieldTypes))
                        | None -> None)

            let initialRecordLayouts =
                initialRecordInfo
                |> List.map (fun (n, fields, _) -> (n, fields))
                |> Map.ofList

            let initialRecordFieldTypes =
                initialRecordInfo
                |> List.map (fun (n, _, types) -> (n, types))
                |> Map.ofList

            // Build param types map for string detection during dbg calls
            let rec typeExprToString (t: TypeExpr) =
                match t with
                | TypeName "string" -> Some "string"
                | TypeName "int" -> Some "int"
                | TypeName "bool" -> Some "bool"
                | TypeTuple _ -> Some "tuple"
                | TypeRecord _ -> Some "record"
                | TypeFunc _ -> Some "func"
                | _ -> None
            let initialParamTypes =
                List.zip parameterNames (defParamTypes @ List.replicate (parameterNames.Length - defParamTypes.Length) TypeUnit)
                |> List.choose (fun (paramName, paramType) ->
                    typeExprToString paramType |> Option.map (fun t -> paramName, t))
                |> Map.ofList

            // Check if this function requires contexts (from TypeWithContext annotation)
            let requiredContextFns = functionContextRequirements |> Map.tryFind name |> Option.defaultValue []
            let contextParamNames =
                requiredContextFns
                |> List.map (fun ctxFnName -> (ctxFnName, $"__ctx_{sanitizeIdentifier ctxFnName}"))

            // Context functions from params - they don't have fixed slots, the slot comes from the param value
            // We'll use ContextParams to store the param name for each context fn
            let contextParamsMap =
                contextParamNames
                |> Map.ofList

            // Determine arity of each context function from type definitions
            let contextFunctionsFromParams =
                contextParamNames
                |> List.mapi (fun _ (ctxFnName, paramName) ->
                    // Look up the context function's arity from the type field definitions
                    // ctxFnName is qualified like "ToString_a_toString" - extract base context name and member
                    let arity =
                        // Try to find by splitting on last underscore to get member name
                        let lastUnderscoreIdx = ctxFnName.LastIndexOf('_')
                        let memberName = if lastUnderscoreIdx > 0 then ctxFnName.Substring(lastUnderscoreIdx + 1) else ctxFnName
                        typeFieldTypes
                        |> Map.toList
                        |> List.tryPick (fun (_, fieldTypes) ->
                            fieldTypes |> Map.tryFind memberName
                            |> Option.bind (fun typeStr ->
                                // Parse arity from type like "(string) -> (())" or "(int, int) -> (int)"
                                // Count commas in the param section to determine arity
                                let arrowIdx = typeStr.IndexOf("->")
                                if arrowIdx < 0 then None
                                else
                                    let paramPart = typeStr.Substring(0, arrowIdx).Trim()
                                    // Count params by counting commas + 1, but handle empty params "()"
                                    if paramPart = "()" || paramPart = "(())" then Some 0
                                    else
                                        let commas = paramPart |> Seq.filter ((=) ',') |> Seq.length
                                        Some (commas + 1)))
                        |> Option.defaultValue 1
                    // The slot isn't known at compile time - it's in the param
                    // We use -1 as a marker meaning "read from ContextParams"
                    ctxFnName, (paramName, -1, arity))
                |> Map.ofList

            let env =
                { KnownFunctions = knownFunctions
                  FunctionArities = functionArities
                  FunctionContextRequirements = functionContextRequirements
                  Locals = locals
                  DbgTempLocal = dbgLocalName
                  TagIds = tagIdMap
                  MatchTempNames = matchTempNames
                  NextMatchTemp = 0
                  StringLiterals = stringLiteralMap
                  ContextFunctions = contextFunctionsFromParams
                  ContextParams = contextParamsMap
                  PendingUseBindings = ref pendingUseBindings
                  RecordLayouts = initialRecordLayouts
                  TypeLayouts = typeLayouts
                  TypeFieldTypes = typeFieldTypes
                  RecordFieldTypes = initialRecordFieldTypes
                  ParamTypes = initialParamTypes }
            let fn = sanitizeIdentifier name
            
            // Build parameter signature including context params
            let regularParamSig =
                parameters
                |> List.map (fun (paramName, _) -> $"(param ${parameterMap.[paramName]} i32)")
                |> String.concat " "
            let contextParamSig =
                contextParamNames
                |> List.map (fun (_, paramName) -> $"(param ${paramName} i32)")
                |> String.concat " "
            let parameterSig =
                [regularParamSig; contextParamSig]
                |> List.filter (String.IsNullOrWhiteSpace >> not)
                |> String.concat " "
            let payloadLocals = collectPayloadLocalsExpr bodyExpr |> List.distinct
            let payloadLocalNames = payloadLocals |> List.map (fun ident -> $"__payload_{ident}")
            let localsSig =
                [ yield! (localNames |> List.map (fun localName -> localMap.[localName]))
                  yield! matchTempNames
                  yield! payloadLocalNames
                  match dbgLocalName with
                  | Some name -> yield name
                  | None -> () ]
                |> List.map (fun localName -> $"(local ${localName} i32)")
                |> String.concat " "
            let functionHeader =
                [ if not (String.IsNullOrWhiteSpace parameterSig) then parameterSig
                  "(result i32)"
                  if not (String.IsNullOrWhiteSpace localsSig) then localsSig ]
                |> String.concat " "
            let bodyCode = transpileExpression env bodyExpr
            if exportFn then
                $"  (func ${fn} {functionHeader}\n    {bodyCode}\n  )\n  (export \"{name}\" (func ${fn}))"
            else
                $"  (func ${fn} {functionHeader}\n    {bodyCode}\n  )"

    let renderedUserFunctions =
        defs
        |> List.map (fun (name, typeOpt, expr) ->
            let pending = contextBindingsByDef |> Map.tryFind name |> Option.defaultValue []
            renderFunction name typeOpt expr true pending)

    let renderedContextFunctions =
        contextDefs
        |> List.map (fun (name, typeOpt, expr) -> renderFunction name typeOpt expr false [])

    let renderedFunctions =
        renderedUserFunctions @ renderedContextFunctions
        |> String.concat "\n\n"

    let renderedStartFunction, startDirective =
        if topLevelExprs.IsEmpty then
            "", ""
        else
            let localNames =
                topLevelExprs
                |> List.collect collectExprLocalNames
                |> List.distinct
            let localMap = createNameMap localNames
            let dbgLocalName =
                if topLevelExprs |> List.exists containsDbgCallExpr then Some "__dbg_tmp"
                else None
            let mutable usedLocalNames =
                ([ yield! localMap |> Map.toList |> List.map snd
                   match dbgLocalName with
                   | Some name -> yield name
                   | None -> () ]
                 |> Set.ofList)
            let matchArity = topLevelExprs |> List.map countMatchArity |> maxOrZero
            let matchTempNames =
                [ for index in 0 .. (matchArity - 1) do
                    let baseName = $"__match_tmp_{index}"
                    let mutable candidate = baseName
                    let mutable suffix = 1
                    while usedLocalNames |> Set.contains candidate do
                        candidate <- $"{baseName}_{suffix}"
                        suffix <- suffix + 1
                    usedLocalNames <- usedLocalNames |> Set.add candidate
                    yield candidate ]
            let payloadLocalNames =
                topLevelExprs
                |> List.collect collectPayloadLocalsExpr
                |> List.distinct
                |> List.map (fun ident -> $"__payload_{ident}")
            let localsSig =
                [ yield! (localNames |> List.map (fun localName -> localMap.[localName]))
                  yield! matchTempNames
                  yield! payloadLocalNames
                  match dbgLocalName with
                  | Some name -> yield name
                  | None -> () ]
                |> List.distinct
                |> List.map (fun localName -> $"(local ${localName} i32)")
                |> String.concat " "
            let env =
                { KnownFunctions = knownFunctions
                  FunctionArities = functionArities
                  FunctionContextRequirements = functionContextRequirements
                  Locals = localMap
                  DbgTempLocal = dbgLocalName
                  TagIds = tagIdMap
                  MatchTempNames = matchTempNames
                  NextMatchTemp = 0
                  StringLiterals = stringLiteralMap
                  ContextFunctions = Map.empty
                  ContextParams = Map.empty
                  PendingUseBindings = ref []
                  RecordLayouts = Map.empty
                  TypeLayouts = typeLayouts
                  TypeFieldTypes = typeFieldTypes
                  RecordFieldTypes = Map.empty
                  ParamTypes = Map.empty }
            let bodyCode =
                topLevelExprs
                |> List.map (fun expr -> $"{transpileExpression env expr}\n    drop")
                |> String.concat "\n    "
            let header =
                if String.IsNullOrWhiteSpace localsSig then ""
                else " " + localsSig
            $"  (func $__nanyx_start{header}\n    {bodyCode}\n  )", "  (start $__nanyx_start)"

    let moduleUsesDbgHelpers = moduleUsesRegularDbg || moduleUsesDbgString

    let dbgPrelude =
        if moduleUsesDbgHelpers then
            [ "  (import \"wasi_snapshot_preview1\" \"fd_write\" (func $fd_write (param i32 i32 i32 i32) (result i32)))"
              "  (memory 1)"
              "  (export \"memory\" (memory 0))"
              $"  (data (i32.const {dbgDataBaseOffset}) \"dbg: \")"
              $"  (data (i32.const {dbgStrNewlineOffset}) \"\\0a\")"
              "  (func $dbg (param $x i32)"
              "    (local $n i32)"
              "    (local $isneg i32)"
              "    (local $pos i32)"
              "    (local $start i32)"
              "    (local $lo i32)"
              "    (local $hi i32)"
              "    (local $tmp i32)"
              "    local.get $x"
              "    local.set $n"
              "    local.get $n"
              "    i32.const 0"
              "    i32.lt_s"
              "    if"
              "      i32.const 1"
              "      local.set $isneg"
              "      local.get $n"
              "      i32.const -1"
              "      i32.mul"
              "      local.set $n"
              "    end"
              $"    i32.const {dbgDigitBufferOffset}"
              "    local.set $pos"
              "    local.get $isneg"
              "    if"
              "      local.get $pos"
              "      i32.const 45"
              "      i32.store8"
              "      local.get $pos"
              "      i32.const 1"
              "      i32.add"
              "      local.set $pos"
              "    end"
              "    local.get $n"
              "    i32.const 0"
              "    i32.eq"
              "    if"
              "      local.get $pos"
              "      i32.const 48"
              "      i32.store8"
              "      local.get $pos"
              "      i32.const 1"
              "      i32.add"
              "      local.set $pos"
              "    else"
              "      local.get $pos"
              "      local.set $start"
              "      loop $digits"
              "        local.get $pos"
              "        local.get $n"
              "        i32.const 10"
              "        i32.rem_u"
              "        i32.const 48"
              "        i32.add"
              "        i32.store8"
              "        local.get $pos"
              "        i32.const 1"
              "        i32.add"
              "        local.set $pos"
              "        local.get $n"
              "        i32.const 10"
              "        i32.div_u"
              "        local.set $n"
              "        local.get $n"
              "        i32.const 0"
              "        i32.ne"
              "        br_if $digits"
              "      end"
              "      local.get $start"
              "      local.set $lo"
              "      local.get $pos"
              "      i32.const 1"
              "      i32.sub"
              "      local.set $hi"
              "      block $rev_done"
              "        loop $rev"
              "          local.get $lo"
              "          local.get $hi"
              "          i32.ge_u"
              "          br_if $rev_done"
              "          local.get $lo"
              "          i32.load8_u"
              "          local.set $tmp"
              "          local.get $lo"
              "          local.get $hi"
              "          i32.load8_u"
              "          i32.store8"
              "          local.get $hi"
              "          local.get $tmp"
              "          i32.store8"
              "          local.get $lo"
              "          i32.const 1"
              "          i32.add"
              "          local.set $lo"
              "          local.get $hi"
              "          i32.const 1"
              "          i32.sub"
              "          local.set $hi"
              "          br $rev"
              "        end"
              "      end"
              "    end"
              "    local.get $pos"
              "    i32.const 10"
              "    i32.store8"
              "    local.get $pos"
              "    i32.const 1"
              "    i32.add"
              "    local.set $pos"
              "    i32.const 0"
              $"    i32.const {dbgDataBaseOffset}"
              "    i32.store"
              "    i32.const 4"
              "    local.get $pos"
              $"    i32.const {dbgDataBaseOffset}"
              "    i32.sub"
              "    i32.store"
              "    i32.const 1"
              "    i32.const 0"
              "    i32.const 1"
              "    i32.const 8"
              "    call $fd_write"
              "    drop"
              "  )"
              "  (func $dbg_str (param $ptr i32)"
              "    (local $len i32)"
              "    local.get $ptr"
              "    i32.const 4"
              "    i32.sub"
              "    i32.load"
              "    local.set $len"
              "    i32.const 0"
              $"    i32.const {dbgDataBaseOffset}"
              "    i32.store"
              "    i32.const 4"
              "    i32.const 5"
              "    i32.store"
              "    i32.const 1"
              "    i32.const 0"
              "    i32.const 1"
              "    i32.const 8"
              "    call $fd_write"
              "    drop"
              "    i32.const 0"
              "    local.get $ptr"
              "    i32.store"
              "    i32.const 4"
              "    local.get $len"
              "    i32.store"
              "    i32.const 1"
              "    i32.const 0"
              "    i32.const 1"
              "    i32.const 8"
              "    call $fd_write"
              "    drop"
              "    i32.const 0"
              $"    i32.const {dbgStrNewlineOffset}"
              "    i32.store"
              "    i32.const 4"
              "    i32.const 1"
              "    i32.store"
              "    i32.const 1"
              "    i32.const 0"
              "    i32.const 1"
              "    i32.const 8"
              "    call $fd_write"
              "    drop"
              "  )" ]
            |> String.concat "\n"
        else
            ""

    let contextTypeDefs =
        contextFnList
        |> List.map ctxFnArity
        |> Set.ofList
        |> Set.toList
        |> List.sort
        |> List.map (fun arity ->
            let paramSig =
                [ for _ in 1 .. arity -> "i32" ]
                |> String.concat " "
            if String.IsNullOrWhiteSpace paramSig then
                $"  (type $ctx_fn_{arity} (func (result i32)))"
            else
                $"  (type $ctx_fn_{arity} (func (param {paramSig}) (result i32)))")
        |> String.concat "\n"

    let contextTable =
        if contextFnList.IsEmpty then
            ""
        else
            $"  (table $ctx_table {contextFnList.Length} funcref)"

    let contextElem =
        if contextFnList.IsEmpty then
            ""
        else
            let ordered = contextFnList |> List.sortBy ctxFnSlot
            let elems = ordered |> List.map (fun ctxFn -> $"${ctxFnName ctxFn}") |> String.concat " "
            $"  (elem (i32.const 0) {elems})"

    let contextPrelude =
        [ contextTypeDefs; contextTable; contextElem ]
        |> List.filter (fun line -> not (String.IsNullOrWhiteSpace line))
        |> String.concat "\n"

    let dbgTagImports =
        dbgTagNames
        |> List.map (fun tagName ->
            let importName = $"dbg_tag_{sanitizeImportSuffix tagName}"
            $"  (import \"env\" \"{importName}\" (func ${importName}))")

    let dbgTagPayloadImports =
        dbgTagPayloadNames
        |> List.map (fun tagName ->
            let importName = $"dbg_tag_payload_{sanitizeImportSuffix tagName}"
            $"  (import \"env\" \"{importName}\" (func ${importName} (param i32)))")

    let dataSegments =
        stringLiteralData
        |> List.map (fun (_, offset, bytes) ->
            $"  (data (i32.const {offset}) \"{escapeWasmData bytes}\")")
        |> String.concat "\n"

    // Add memory if we have string literals or heap allocation, but no dbg helpers (dbgPrelude already includes memory)
    let needsMemory = (not (List.isEmpty stringLiteralData) || moduleUsesHeap) && not moduleUsesDbgHelpers
    let memorySection =
        if needsMemory then
            "  (memory 1)\n  (export \"memory\" (memory 0))"
        else
            ""

    // Add heap global if we use heap allocation
    let heapSection =
        if moduleUsesHeap then
            $"  (global $heap_ptr (mut i32) (i32.const {heapBaseOffset}))"
        else
            ""

    let allImports =
        [ if not (String.IsNullOrWhiteSpace dbgPrelude) then dbgPrelude
          if not (String.IsNullOrWhiteSpace memorySection) then memorySection
          if not (String.IsNullOrWhiteSpace heapSection) then heapSection
          if not (String.IsNullOrWhiteSpace contextPrelude) then contextPrelude
          yield! dbgTagImports
          yield! dbgTagPayloadImports ]
        |> List.filter (fun line -> not (String.IsNullOrWhiteSpace line))
        |> String.concat "\n\n"

    if String.IsNullOrWhiteSpace renderedFunctions && String.IsNullOrWhiteSpace allImports && String.IsNullOrWhiteSpace dataSegments then
        "(module)"
    else
        let parts =
            [ if not (String.IsNullOrWhiteSpace allImports) then allImports
              if not (String.IsNullOrWhiteSpace dataSegments) then dataSegments
              if not (String.IsNullOrWhiteSpace renderedFunctions) then renderedFunctions
              if not (String.IsNullOrWhiteSpace renderedStartFunction) then renderedStartFunction
              if not (String.IsNullOrWhiteSpace startDirective) then startDirective ]
        let body = String.concat "\n\n" parts
        $"(module\n{body}\n)"
