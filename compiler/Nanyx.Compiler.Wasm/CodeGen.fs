module Transpiler.Wasm.CodeGen

open System
open System.Text
open NyxCompiler
open Parser.Program

type private ContextFn = string * int * int

/// The module's function table (`$ctx_table`), as far as functions used as values are concerned: a top-level
/// function referenced without being called, or a lifted lambda, gets a slot here and is called through
/// `call_indirect`. Shared by every function of a module and read when the table is rendered.
type private FunctionTable =
    { /// Function name -> (WebAssembly name, slot, arity).
      Values: System.Collections.Generic.Dictionary<string, ContextFn>
      /// The next free slot; starts after the slots the context machinery assigned.
      mutable NextSlot: int
      /// The arities of every indirect call made through a local, so a `$ctx_fn_N` type exists for each.
      IndirectArities: System.Collections.Generic.HashSet<int> }

type private TranspileEnv =
    { KnownFunctions: Set<string>
      FunctionArities: Map<string, int>
      FunctionContextRequirements: Map<string, string list>
      /// The simple result type ("string", "int" or "bool") of the functions whose annotation or inferred type says.
      FunctionReturnTypes: Map<string, string>
      Locals: Map<string, string>
      DbgTempLocal: string option
      TagIds: Map<string, int>
      MatchTempNames: string list
      NextMatchTemp: int
      StringLiterals: Map<string, int>
      ContextFunctions: Map<string, ContextFn>
      ContextParams: Map<string, string>
      /// Module-level functions that stand in for a context member of the same name when a callee needs a
      /// context and nothing in scope supplies it: the ambient instance (e.g. the runtime's `Console`).
      AmbientContextFunctions: Map<string, ContextFn>
      PendingUseBindings: Map<string, ContextFn> list ref
      RecordLayouts: Map<string, string list>
      TypeLayouts: Map<string, string list>
      TypeFieldTypes: Map<string, Map<string, string>>
      RecordFieldTypes: Map<string, Map<string, string>>
      ParamTypes: Map<string, string>
      ValueTypes: Map<string, string>
      /// The full inferred types of top-level defs, parameters and locals, when the module was type-checked.
      /// Record layouts are derived from these, so field access works on any expression whose type is known,
      /// not only on locals the backend saw being assigned a record literal.
      ValueTys: Map<string, Ty>
      /// The runtime helpers (see `runtimeHelpers`) the lowered code calls; shared by every function of a module.
      UsedHelpers: System.Collections.Generic.HashSet<string>
      FunctionTable: FunctionTable }

/// The names of the WebAssembly helper functions the backend emits on demand (see `runtimeHelpers`).
module private Helpers =
    let alloc = "__nanyx_alloc"
    let copyBytes = "__nanyx_copy_bytes"
    let stringAlloc = "__nanyx_string_alloc"
    let stringConcat = "__nanyx_string_concat"
    let stringEquals = "__nanyx_string_equals"
    let listLength = "__nanyx_list_length"
    let listConcat = "__nanyx_list_concat"
    let recordSet = "__nanyx_record_set"
    let writeStdout = NyxCompiler.Intrinsics.writeStdout
    let stringLength = NyxCompiler.Intrinsics.stringLength
    let stringByteAt = NyxCompiler.Intrinsics.stringByteAt
    let stringSlice = NyxCompiler.Intrinsics.stringSlice
    let stringFromByte = NyxCompiler.Intrinsics.stringFromByte
    let listCons = NyxCompiler.Intrinsics.listCons

/// The WebAssembly helper functions, keyed by name. The ones named after a compiler intrinsic implement
/// that intrinsic; the rest back language operators (`+` and `==` on strings, list literals and patterns)
/// and the allocator they share. Only the helpers a module uses are emitted.
///
/// Memory model: a string is a pointer to its UTF-8 bytes, with its byte length in the 4 bytes before the
/// pointer and a 0 byte after; a list is 0 (empty) or a pointer to an 8-byte cell of (head, tail). The heap is
/// a bump allocator from `$heap_ptr` that grows the memory when it runs out; nothing is ever freed.
let private runtimeHelpers : Map<string, string> =
    let lines (items: string list) = items |> List.map (fun line -> "  " + line) |> String.concat "\n"
    [ Helpers.alloc,
      lines
          [ $"(func ${Helpers.alloc} (param $size i32) (result i32)"
            "  (local $ptr i32)"
            "  global.get $heap_ptr"
            "  local.set $ptr"
            "  local.get $ptr"
            "  local.get $size"
            "  i32.add"
            "  global.set $heap_ptr"
            "  block $fits"
            "    loop $grow"
            "      global.get $heap_ptr"
            "      memory.size"
            "      i32.const 65536"
            "      i32.mul"
            "      i32.le_u"
            "      br_if $fits"
            "      i32.const 1"
            "      memory.grow"
            "      i32.const -1"
            "      i32.eq"
            "      if"
            "        unreachable"
            "      end"
            "      br $grow"
            "    end"
            "  end"
            "  local.get $ptr"
            ")" ]
      Helpers.copyBytes,
      lines
          [ $"(func ${Helpers.copyBytes} (param $dst i32) (param $src i32) (param $len i32)"
            "  (local $i i32)"
            "  block $done"
            "    loop $copy"
            "      local.get $i"
            "      local.get $len"
            "      i32.ge_s"
            "      br_if $done"
            "      local.get $dst"
            "      local.get $i"
            "      i32.add"
            "      local.get $src"
            "      local.get $i"
            "      i32.add"
            "      i32.load8_u"
            "      i32.store8"
            "      local.get $i"
            "      i32.const 1"
            "      i32.add"
            "      local.set $i"
            "      br $copy"
            "    end"
            "  end"
            ")" ]
      Helpers.stringLength,
      lines
          [ $"(func ${Helpers.stringLength} (param $s i32) (result i32)"
            "  local.get $s"
            "  i32.const 4"
            "  i32.sub"
            "  i32.load"
            ")" ]
      Helpers.stringAlloc,
      lines
          [ $"(func ${Helpers.stringAlloc} (param $len i32) (result i32)"
            "  (local $base i32)"
            "  local.get $len"
            "  i32.const 5"
            "  i32.add"
            $"  call ${Helpers.alloc}"
            "  local.set $base"
            "  local.get $base"
            "  local.get $len"
            "  i32.store"
            "  local.get $base"
            "  i32.const 4"
            "  i32.add"
            "  local.get $len"
            "  i32.add"
            "  i32.const 0"
            "  i32.store8"
            "  local.get $base"
            "  i32.const 4"
            "  i32.add"
            ")" ]
      Helpers.stringByteAt,
      lines
          [ $"(func ${Helpers.stringByteAt} (param $s i32) (param $i i32) (result i32)"
            "  local.get $i"
            "  i32.const 0"
            "  i32.lt_s"
            "  local.get $i"
            "  local.get $s"
            $"  call ${Helpers.stringLength}"
            "  i32.ge_s"
            "  i32.or"
            "  if (result i32)"
            "    i32.const -1"
            "  else"
            "    local.get $s"
            "    local.get $i"
            "    i32.add"
            "    i32.load8_u"
            "  end"
            ")" ]
      Helpers.stringSlice,
      lines
          [ $"(func ${Helpers.stringSlice} (param $s i32) (param $start i32) (param $len i32) (result i32)"
            "  (local $total i32)"
            "  (local $out i32)"
            "  local.get $s"
            $"  call ${Helpers.stringLength}"
            "  local.set $total"
            "  local.get $start"
            "  i32.const 0"
            "  i32.lt_s"
            "  if"
            "    i32.const 0"
            "    local.set $start"
            "  end"
            "  local.get $start"
            "  local.get $total"
            "  i32.gt_s"
            "  if"
            "    local.get $total"
            "    local.set $start"
            "  end"
            "  local.get $len"
            "  i32.const 0"
            "  i32.lt_s"
            "  if"
            "    i32.const 0"
            "    local.set $len"
            "  end"
            "  local.get $len"
            "  local.get $total"
            "  local.get $start"
            "  i32.sub"
            "  i32.gt_s"
            "  if"
            "    local.get $total"
            "    local.get $start"
            "    i32.sub"
            "    local.set $len"
            "  end"
            "  local.get $len"
            $"  call ${Helpers.stringAlloc}"
            "  local.set $out"
            "  local.get $out"
            "  local.get $s"
            "  local.get $start"
            "  i32.add"
            "  local.get $len"
            $"  call ${Helpers.copyBytes}"
            "  local.get $out"
            ")" ]
      Helpers.stringFromByte,
      lines
          [ $"(func ${Helpers.stringFromByte} (param $byte i32) (result i32)"
            "  (local $out i32)"
            "  i32.const 1"
            $"  call ${Helpers.stringAlloc}"
            "  local.set $out"
            "  local.get $out"
            "  local.get $byte"
            "  i32.store8"
            "  local.get $out"
            ")" ]
      Helpers.stringConcat,
      lines
          [ $"(func ${Helpers.stringConcat} (param $a i32) (param $b i32) (result i32)"
            "  (local $lenA i32)"
            "  (local $lenB i32)"
            "  (local $out i32)"
            "  local.get $a"
            $"  call ${Helpers.stringLength}"
            "  local.set $lenA"
            "  local.get $b"
            $"  call ${Helpers.stringLength}"
            "  local.set $lenB"
            "  local.get $lenA"
            "  local.get $lenB"
            "  i32.add"
            $"  call ${Helpers.stringAlloc}"
            "  local.set $out"
            "  local.get $out"
            "  local.get $a"
            "  local.get $lenA"
            $"  call ${Helpers.copyBytes}"
            "  local.get $out"
            "  local.get $lenA"
            "  i32.add"
            "  local.get $b"
            "  local.get $lenB"
            $"  call ${Helpers.copyBytes}"
            "  local.get $out"
            ")" ]
      Helpers.stringEquals,
      lines
          [ $"(func ${Helpers.stringEquals} (param $a i32) (param $b i32) (result i32)"
            "  (local $len i32)"
            "  (local $i i32)"
            "  local.get $a"
            $"  call ${Helpers.stringLength}"
            "  local.set $len"
            "  local.get $len"
            "  local.get $b"
            $"  call ${Helpers.stringLength}"
            "  i32.ne"
            "  if"
            "    i32.const 0"
            "    return"
            "  end"
            "  block $done"
            "    loop $compare"
            "      local.get $i"
            "      local.get $len"
            "      i32.ge_s"
            "      br_if $done"
            "      local.get $a"
            "      local.get $i"
            "      i32.add"
            "      i32.load8_u"
            "      local.get $b"
            "      local.get $i"
            "      i32.add"
            "      i32.load8_u"
            "      i32.ne"
            "      if"
            "        i32.const 0"
            "        return"
            "      end"
            "      local.get $i"
            "      i32.const 1"
            "      i32.add"
            "      local.set $i"
            "      br $compare"
            "    end"
            "  end"
            "  i32.const 1"
            ")" ]
      // Writes a string to stdout (fd 1) with the iovec at 0 and nwritten at 8, like the dbg helpers.
      Helpers.writeStdout,
      lines
          [ $"(func ${Helpers.writeStdout} (param $s i32)"
            "  i32.const 0"
            "  local.get $s"
            "  i32.store"
            "  i32.const 4"
            "  local.get $s"
            $"  call ${Helpers.stringLength}"
            "  i32.store"
            "  i32.const 1"
            "  i32.const 0"
            "  i32.const 1"
            "  i32.const 8"
            "  call $fd_write"
            "  drop"
            ")" ]
      // Stores one field of a freshly allocated record and hands the record pointer back, so a record
      // literal is built entirely on the operand stack: `base value offset -> base`. That keeps nested
      // literals safe, since an inner allocation can never disturb an outer one held on the stack.
      Helpers.recordSet,
      lines
          [ $"(func ${Helpers.recordSet} (param $base i32) (param $value i32) (param $offset i32) (result i32)"
            "  local.get $base"
            "  local.get $offset"
            "  i32.add"
            "  local.get $value"
            "  i32.store"
            "  local.get $base"
            ")" ]
      Helpers.listCons,
      lines
          [ $"(func ${Helpers.listCons} (param $head i32) (param $tail i32) (result i32)"
            "  (local $cell i32)"
            "  i32.const 8"
            $"  call ${Helpers.alloc}"
            "  local.set $cell"
            "  local.get $cell"
            "  local.get $head"
            "  i32.store"
            "  local.get $cell"
            "  i32.const 4"
            "  i32.add"
            "  local.get $tail"
            "  i32.store"
            "  local.get $cell"
            ")" ]
      Helpers.listLength,
      lines
          [ $"(func ${Helpers.listLength} (param $list i32) (result i32)"
            "  (local $count i32)"
            "  block $done"
            "    loop $walk"
            "      local.get $list"
            "      i32.eqz"
            "      br_if $done"
            "      local.get $count"
            "      i32.const 1"
            "      i32.add"
            "      local.set $count"
            "      local.get $list"
            "      i32.const 4"
            "      i32.add"
            "      i32.load"
            "      local.set $list"
            "      br $walk"
            "    end"
            "  end"
            "  local.get $count"
            ")" ]
      // a ++ b copies the cells of a in front of b (b itself is shared, lists being immutable).
      Helpers.listConcat,
      lines
          [ $"(func ${Helpers.listConcat} (param $a i32) (param $b i32) (result i32)"
            "  local.get $a"
            "  i32.eqz"
            "  if (result i32)"
            "    local.get $b"
            "  else"
            "    local.get $a"
            "    i32.load"
            "    local.get $a"
            "    i32.const 4"
            "    i32.add"
            "    i32.load"
            "    local.get $b"
            $"    call ${Helpers.listConcat}"
            $"    call ${Helpers.listCons}"
            "  end"
            ")" ] ]
    |> Map.ofList

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
    | "and" -> Some "i32.and"
    | "or" -> Some "i32.or"
    | _ -> None

let private flattenCallArgs (args: Expression list) =
    match args with
    | [TupleExpr items] -> items
    | _ -> args

/// The function-table slot of a top-level function used as a value, assigning one on first use.
let private functionValueSlot (env: TranspileEnv) (name: string) (arity: int) : int =
    match env.FunctionTable.Values.TryGetValue name with
    | true, ctxFn -> ctxFnSlot ctxFn
    | _ ->
        let slot = env.FunctionTable.NextSlot
        env.FunctionTable.NextSlot <- slot + 1
        env.FunctionTable.Values.[name] <- (sanitizeIdentifier name, slot, arity)
        slot

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

/// The identifiers a pattern binds that live in `__payload_<name>` locals: tag payloads, list elements and list
/// rests. (Plain identifier patterns alias the scrutinee local and need no local of their own.)
let private patternBoundIdentifiers (pattern: Pattern) : string list =
    match pattern with
    | TagPattern(_, Some (IdentifierPattern ident)) -> [ ident ]
    | ListPattern(elements, restOpt) ->
        let elementIdents =
            elements
            |> List.choose (function
                | IdentifierPattern ident -> Some ident
                | _ -> None)
        let restIdents =
            match restOpt with
            | Some (IdentifierPattern ident) -> [ ident ]
            | _ -> []
        elementIdents @ restIdents
    | _ -> []

let rec private collectPayloadLocalsExpr expr =
    match expr with
    | Match(_, arms) ->
        arms |> List.collect (fun (patterns, armExpr) ->
            let payloads = patterns |> List.collect patternBoundIdentifiers
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

// dbg writes an integer's sign and digits directly after the "dbg: " prefix (at 133), so that one
// fd_write from the prefix to the end of the digits prints the whole line.
let private dbgDigitBufferOffset = dbgDataBaseOffset + 5
let private dbgStrNewlineOffset = dbgDataBaseOffset + 96  // newline byte at 224 (clear of dbg's buffer)

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

let private trySimpleTypeName (ty: Ty) =
    match ty with
    | TyPrimitive "string" -> Some "string"
    | TyPrimitive "int" -> Some "int"
    | TyPrimitive "bool" -> Some "bool"
    | TyApply("list", _) -> Some "list"
    | _ -> None

/// The payload type of `#tagName(...)` within `ty`, which may be the tag itself, a union containing it, or a
/// nominal type wrapping either.
let rec private tryTagPayloadType (tagName: string) (ty: Ty) : Ty option =
    match ty with
    | TyTag(name, Some payloadTy) when name = tagName -> Some payloadTy
    | TyUnion options -> options |> List.tryPick (tryTagPayloadType tagName)
    | TyNominal(_, underlying, _) -> tryTagPayloadType tagName underlying
    | _ -> None

let rec private tryListElementType (ty: Ty) : Ty option =
    match ty with
    | TyApply("list", [ elementTy ]) -> Some elementTy
    | TyNominal(_, underlying, _) -> tryListElementType underlying
    | _ -> None

/// The simple types of the identifiers a typed pattern binds: `#some(n)` binds `n` at the tag's payload
/// type, `[x, ...rest]` binds `x` at the element type and `rest` as a list, and a bare `n` binds at the
/// scrutinee's type.
let private patternBoundValueTypes (typedPattern: TypedPattern) : (string * string) list =
    match typedPattern.Pattern with
    | IdentifierPattern ident ->
        trySimpleTypeName typedPattern.Type |> Option.map (fun tyName -> ident, tyName) |> Option.toList
    | TagPattern(tagName, Some (IdentifierPattern ident)) ->
        tryTagPayloadType tagName typedPattern.Type
        |> Option.bind trySimpleTypeName
        |> Option.map (fun tyName -> ident, tyName)
        |> Option.toList
    | ListPattern(elements, restOpt) ->
        let elementTyName = tryListElementType typedPattern.Type |> Option.bind trySimpleTypeName
        let elementBindings =
            elements
            |> List.choose (function
                | IdentifierPattern ident -> elementTyName |> Option.map (fun tyName -> ident, tyName)
                | _ -> None)
        let restBinding =
            match restOpt with
            | Some (IdentifierPattern ident) -> [ ident, "list" ]
            | _ -> []
        elementBindings @ restBinding
    | _ -> []

/// The full inferred types of a function's parameters and the locals defined anywhere in its body.
let private collectTypedValueTys (typedExpr: TypedExpr) : Map<string, Ty> =
    let parameterTys =
        match typedExpr.Expr, typedExpr.Type with
        | Lambda([ (name, _) ], _), TyFunc(inputTy, _) -> [ name, inputTy ]
        | Lambda(parameters, _), TyFunc(TyRecord fields, _) ->
            parameters
            |> List.mapi (fun index (name, _) -> fields |> Map.tryFind (string index) |> Option.map (fun ty -> name, ty))
            |> List.choose id
        | _ -> []
    let rec collectExpr (acc: Map<string, Ty>) (expr: TypedExpr) =
        let accWithBody =
            match expr.Body with
            | Some body -> collectExpr acc body
            | None -> acc
        let accWithStatements =
            match expr.Statements with
            | Some statements -> statements |> List.fold collectStatement accWithBody
            | None -> accWithBody
        match expr.MatchArms with
        | Some arms -> arms |> List.fold (fun state (_, armExpr) -> collectExpr state armExpr) accWithStatements
        | None -> accWithStatements
    and collectStatement (acc: Map<string, Ty>) statement =
        match statement with
        | TypedDefStatement(_, name, _, expr) -> collectExpr (acc |> Map.add name expr.Type) expr
        | TypedExprStatement expr -> collectExpr acc expr
        | TypedUseStatement(_, Some expr) -> collectExpr acc expr
        | TypedUseStatement(_, None)
        | TypedImportStatement _
        | TypedTypeDefStatement _ -> acc
    collectExpr (Map.ofList parameterTys) typedExpr

/// The simple types inference found for a function's parameters and the locals of its body.
let private collectTypedValueTypes (typedExpr: TypedExpr) =
    let parameterTypes =
        match typedExpr.Expr, typedExpr.Type with
        | Lambda([ (name, _) ], _), TyFunc(inputTy, _) ->
            trySimpleTypeName inputTy |> Option.map (fun tyName -> name, tyName) |> Option.toList
        | Lambda(parameters, _), TyFunc(TyRecord fields, _) ->
            parameters
            |> List.mapi (fun index (name, _) ->
                fields
                |> Map.tryFind (string index)
                |> Option.bind trySimpleTypeName
                |> Option.map (fun tyName -> name, tyName))
            |> List.choose id
        | _ -> []
    let rec collectExpr (acc: Map<string, string>) (expr: TypedExpr) =
        let accWithBody =
            match expr.Body with
            | Some body -> collectExpr acc body
            | None -> acc
        let accWithStatements =
            match expr.Statements with
            | Some statements -> statements |> List.fold collectStatement accWithBody
            | None -> accWithBody
        match expr.MatchArms with
        | Some arms ->
            arms
            |> List.fold (fun state (typedPatterns, armExpr) ->
                let withBindings =
                    typedPatterns
                    |> List.collect patternBoundValueTypes
                    |> List.fold (fun acc (ident, tyName) -> acc |> Map.add ident tyName) state
                collectExpr withBindings armExpr) accWithStatements
        | None -> accWithStatements
    and collectStatement (acc: Map<string, string>) statement =
        match statement with
        | TypedDefStatement(_, name, _, expr) ->
            let updated =
                match trySimpleTypeName expr.Type with
                | Some tyName -> acc |> Map.add name tyName
                | None -> acc
            collectExpr updated expr
        | TypedExprStatement expr -> collectExpr acc expr
        | TypedUseStatement(_, exprOpt) ->
            match exprOpt with
            | Some expr -> collectExpr acc expr
            | None -> acc
        | TypedImportStatement _
        | TypedTypeDefStatement _ -> acc
    collectExpr (Map.ofList parameterTypes) typedExpr

/// The fields of a record type, looking through nominal wrappers.
let rec private tryRecordFieldTys (ty: Ty) : Map<string, Ty> option =
    match ty with
    | TyRecord fields -> Some fields
    | TyNominal(_, underlying, _) -> tryRecordFieldTys underlying
    | _ -> None

/// The inferred type of an expression, when `env.ValueTys` can tell: a named value, a field of a record-typed
/// expression, or the result of calling a known function.
let rec private tryExprTy (env: TranspileEnv) (expr: Expression) : Ty option =
    match expr with
    | IdentifierExpr(name, _) -> env.ValueTys |> Map.tryFind name
    | MemberAccess(inner, fieldName, _) ->
        tryExprTy env inner |> Option.bind tryRecordFieldTys |> Option.bind (Map.tryFind fieldName)
    | FunctionCall(name, _, _)
    | Pipe(_, name, _, _) ->
        env.ValueTys
        |> Map.tryFind name
        |> Option.bind (function
            | TyFunc(_, returnTy) -> Some returnTy
            | _ -> None)
    | _ -> None

/// The layout (field names in storage order) of a record-typed expression, from its inferred type. Records are
/// stored with their fields sorted by name, which is also the order of a `Map`'s keys.
let private tryRecordLayoutOfExpr (env: TranspileEnv) (expr: Expression) : string list option =
    tryExprTy env expr
    |> Option.bind tryRecordFieldTys
    |> Option.map (fun fields -> fields |> Map.toList |> List.map fst)

/// The simple type ("string", "int" or "bool") of an expression, when the backend can tell. Strings matter most:
/// `+` and `==` are lowered differently when an operand is known to be a string.
let rec private tryGetExprTypeName (env: TranspileEnv) (expr: Expression) =
    let fromInferredType () = tryExprTy env expr |> Option.bind trySimpleTypeName
    match expr with
    | LiteralExpr(StringLit _) -> Some "string"
    | LiteralExpr(IntLit _) -> Some "int"
    | LiteralExpr(BoolLit _) -> Some "bool"
    | InterpolatedString _ -> Some "string"
    | IdentifierExpr(name, _) ->
        env.ValueTypes |> Map.tryFind name
        |> Option.orElseWith (fun () -> env.ParamTypes |> Map.tryFind name)
        |> Option.orElseWith fromInferredType
    | MemberAccess(IdentifierExpr(varName, _), fieldName, _) when env.RecordFieldTypes |> Map.containsKey fieldName |> not && env.RecordFieldTypes |> Map.containsKey varName ->
        env.RecordFieldTypes.[varName] |> Map.tryFind fieldName |> Option.orElseWith fromInferredType
    | MemberAccess _ -> fromInferredType ()
    | BinaryOp(op, _, _) when [ "=="; "!="; "<"; "<="; ">"; ">="; "and"; "or" ] |> List.contains op -> Some "bool"
    | FunctionCall("not", _, _) -> Some "bool"
    | BinaryOp(_, left, right) ->
        tryGetExprTypeName env left |> Option.orElseWith (fun () -> tryGetExprTypeName env right)
    | ListExpr _ -> Some "list"
    | FunctionCall(name, _, _)
    | Pipe(_, name, _, _) -> env.FunctionReturnTypes |> Map.tryFind name
    | IfExpr(_, thenExpr, elseExpr) ->
        tryGetExprTypeName env thenExpr |> Option.orElseWith (fun () -> tryGetExprTypeName env elseExpr)
    | Block statements ->
        match List.tryLast statements with
        | Some (ExprStatement last) -> tryGetExprTypeName env last
        | _ -> None
    | _ -> fromInferredType ()

let private isStringExpr (env: TranspileEnv) (expr: Expression) =
    tryGetExprTypeName env expr = Some "string"

let private collectInterpolatedStringExpr (expr: Expression) : bool =
    match expr with
    | InterpolatedString _ -> true
    | _ -> false

/// The immediate sub-expressions of `expr`, for traversals that only need to look inside.
let private exprChildren (expr: Expression) : Expression list =
    match expr with
    | InterpolatedString parts ->
        parts |> List.choose (function StringExpr value -> Some value | StringText _ -> None)
    | Block statements ->
        statements
        |> List.choose (function
            | DefStatement(_, _, _, rhs) -> Some rhs
            | ExprStatement value -> Some value
            | _ -> None)
    | BinaryOp(_, left, right) -> [ left; right ]
    | IfExpr(cond, thenExpr, elseExpr) -> [ cond; thenExpr; elseExpr ]
    | FunctionCall(_, _, args) -> args
    | Lambda(_, body) -> [ body ]
    | Pipe(value, _, _, args) -> value :: args
    | TupleExpr items
    | ListExpr items -> items
    | RecordExpr fields ->
        fields
        |> List.map (function
            | NamedField(_, value) -> value
            | PositionalField value -> value)
    | Match(values, arms) -> values @ (arms |> List.map snd)
    | UseIn(_, body) -> [ body ]
    | MemberAccess(inner, _, _) -> [ inner ]
    | TagExpr(_, payload) -> Option.toList payload
    | WorkflowBindExpr(_, value)
    | WorkflowReturnExpr value -> [ value ]
    | ContextMemberCall(_, _, args) -> args
    | ContextMemberAccess(_, _)
    | UnitExpr
    | LiteralExpr _
    | IdentifierExpr _ -> []

/// Whether lowering `expr` needs the interpolation scratch locals (see `interpolationLocalNames`).
/// Used for function bodies and for the script start function alike, so both declare the same locals.
let rec private containsInterpolation (expr: Expression) : bool =
    match expr with
    | InterpolatedString _ -> true
    | _ -> exprChildren expr |> List.exists containsInterpolation

/// The most `{...}` holes any single interpolated string in `expr` has: each hole is evaluated once
/// into its own `__interp_hole_N` local, so the function has to declare that many.
let rec private maxInterpolationHoles (expr: Expression) : int =
    let own =
        match expr with
        | InterpolatedString parts -> parts |> List.filter (function StringExpr _ -> true | _ -> false) |> List.length
        | _ -> 0
    exprChildren expr |> List.fold (fun acc child -> max acc (maxInterpolationHoles child)) own

let private interpolationHoleLocalName (index: int) = $"__interp_hole_{index}"

let private interpolationLocalNames =
    [ "__interp_len"
      "__interp_base"
      "__interp_cursor"
      "__interp_ptr"
      "__interp_copy_len"
      "__interp_src"
      "__interp_tmp"
      "__interp_start"
      "__interp_lo"
      "__interp_hi"
      "__interp_swap" ]

/// Every local that lowering the interpolations in `expr` needs, or none when it has no interpolation.
let private interpolationLocalsFor (expr: Expression) : string list =
    if containsInterpolation expr then
        interpolationLocalNames
        @ [ for index in 0 .. maxInterpolationHoles expr - 1 -> interpolationHoleLocalName index ]
    else
        []

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

/// True when `predicate` holds for `expr` or any expression nested inside it.
let rec private existsExpr (predicate: Expression -> bool) (expr: Expression) : bool =
    let recurse = existsExpr predicate
    predicate expr
    || match expr with
       | FunctionCall(_, _, args) -> args |> List.exists recurse
       | BinaryOp(_, left, right) -> recurse left || recurse right
       | IfExpr(cond, thenExpr, elseExpr) -> recurse cond || recurse thenExpr || recurse elseExpr
       | Block statements -> existsStatements predicate statements
       | Lambda(_, body) -> recurse body
       | Pipe(value, _, _, args) -> recurse value || (args |> List.exists recurse)
       | TupleExpr items
       | ListExpr items -> items |> List.exists recurse
       | RecordExpr fields ->
           fields
           |> List.exists (function
               | NamedField(_, value) -> recurse value
               | PositionalField value -> recurse value)
       | Match(values, arms) ->
           (values |> List.exists recurse) || (arms |> List.exists (fun (_, armExpr) -> recurse armExpr))
       | UseIn(_, body) -> recurse body
       | MemberAccess(inner, _, _) -> recurse inner
       | TagExpr(_, payload) -> payload |> Option.exists recurse
       | InterpolatedString parts ->
           parts
           |> List.exists (function
               | StringText _ -> false
               | StringExpr inner -> recurse inner)
       | WorkflowBindExpr(_, value)
       | WorkflowReturnExpr value -> recurse value
       | ContextMemberCall(_, _, args) -> args |> List.exists recurse
       | ContextMemberAccess(_, _)
       | UnitExpr
       | LiteralExpr _
       | IdentifierExpr _ -> false

and private existsStatements (predicate: Expression -> bool) (statements: Statement list) : bool =
    statements
    |> List.exists (function
        | DefStatement(_, _, _, rhs) -> existsExpr predicate rhs
        | ExprStatement expr -> existsExpr predicate expr
        | UseStatement (UseValue value)
        | UseStatement (UseContextInstance(_, value)) -> existsExpr predicate value
        | ImportStatement _
        | TypeDefStatement _ -> false)

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
    | InterpolatedString _ -> true
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
        let armStrings =
            arms
            |> List.collect (fun (patterns, armExpr) ->
                (patterns |> List.collect collectStringLiteralsPattern) @ collectStringLiteralsExpr armExpr)
        valueStrings @ armStrings
    | UseIn(_, body) -> collectStringLiteralsExpr body
    | MemberAccess(inner, _, _) -> collectStringLiteralsExpr inner
    | TagExpr(_, payload) -> payload |> Option.map collectStringLiteralsExpr |> Option.defaultValue []
    | InterpolatedString parts ->
        // Literals inside `{...}` holes need data-segment offsets like any other literal.
        parts
        |> List.collect (function
            | StringText _ -> []
            | StringExpr value -> collectStringLiteralsExpr value)
    | WorkflowBindExpr(_, value)
    | WorkflowReturnExpr value -> collectStringLiteralsExpr value
    | ContextMemberAccess(_, _) -> []
    | ContextMemberCall(_, _, args) -> args |> List.collect collectStringLiteralsExpr
    | UnitExpr
    | LiteralExpr _
    | IdentifierExpr _ -> []

/// String literals that appear inside patterns (`| "Sat" -> ...`, `| #name("x") -> ...`) need data-segment
/// offsets just like literals in expressions.
and private collectStringLiteralsPattern (pattern: Pattern) : string list =
    match pattern with
    | LiteralPattern(StringLit value) -> [ value ]
    | TagPattern(_, Some payload) -> collectStringLiteralsPattern payload
    | TuplePattern patterns
    | ListPattern(patterns, _) -> patterns |> List.collect collectStringLiteralsPattern
    | ListSplatMiddle(beforePatterns, afterPatterns) ->
        (beforePatterns |> List.collect collectStringLiteralsPattern)
        @ (afterPatterns |> List.collect collectStringLiteralsPattern)
    | RecordPattern(_, patterns) -> patterns |> List.collect collectStringLiteralsPattern
    | RecordMemberPattern members -> members |> List.collect (fun (_, p) -> collectStringLiteralsPattern p)
    | GuardPattern(_, value)
    | RangePattern(value, _) -> collectStringLiteralsExpr value
    | TagPattern(_, None)
    | LiteralPattern _
    | IdentifierPattern _
    | WildcardPattern
    | ElsePattern -> []

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
    /// The pointer to a string literal's bytes in the data section.
    let stringLiteralCode (value: string) =
        match env.StringLiterals |> Map.tryFind value with
        | Some offset -> $"i32.const {offset}"
        | None -> failwith $"Internal error: missing string literal offset for {value}"

    /// Allocates a block of 4-byte slots and fills them with `values` in order, leaving the block's pointer on
    /// the stack. The block is reserved before any value is evaluated and the pointer stays on the operand
    /// stack throughout, so a value that allocates (a nested record, a string) cannot overwrite the block.
    let allocateSlots (values: Expression list) =
        env.UsedHelpers.Add Helpers.alloc |> ignore
        env.UsedHelpers.Add Helpers.recordSet |> ignore
        let stores =
            values
            |> List.mapi (fun index value ->
                $"{transpileExpression env value}\n    i32.const {index * 4}\n    call ${Helpers.recordSet}")
        ($"i32.const {values.Length * 4}\n    call ${Helpers.alloc}" :: stores) |> String.concat "\n    "

    let renderStaticBytes bytes =
        bytes
        |> Array.toList
        |> List.map (fun b ->
            $"local.get $__interp_cursor\n    i32.const {int b}\n    i32.store8\n    local.get $__interp_cursor\n    i32.const 1\n    i32.add\n    local.set $__interp_cursor")
        |> String.concat "\n    "

    let renderIntLength exprCode =
        $"{exprCode}\n    local.set $__interp_tmp\n    local.get $__interp_tmp\n    i32.const 0\n    i32.lt_s\n    if\n      local.get $__interp_len\n      i32.const 1\n      i32.add\n      local.set $__interp_len\n      local.get $__interp_tmp\n      i32.const -1\n      i32.mul\n      local.set $__interp_tmp\n    end\n    local.get $__interp_tmp\n    i32.const 0\n    i32.eq\n    if\n      local.get $__interp_len\n      i32.const 1\n      i32.add\n      local.set $__interp_len\n    else\n      loop $interp_int_len\n        local.get $__interp_len\n        i32.const 1\n        i32.add\n        local.set $__interp_len\n        local.get $__interp_tmp\n        i32.const 10\n        i32.div_u\n        local.set $__interp_tmp\n        local.get $__interp_tmp\n        i32.const 0\n        i32.ne\n        br_if $interp_int_len\n      end\n    end"

    let renderIntWrite exprCode =
        $"{exprCode}\n    local.set $__interp_tmp\n    local.get $__interp_tmp\n    i32.const 0\n    i32.lt_s\n    if\n      local.get $__interp_cursor\n      i32.const 45\n      i32.store8\n      local.get $__interp_cursor\n      i32.const 1\n      i32.add\n      local.set $__interp_cursor\n      local.get $__interp_tmp\n      i32.const -1\n      i32.mul\n      local.set $__interp_tmp\n    end\n    local.get $__interp_tmp\n    i32.const 0\n    i32.eq\n    if\n      local.get $__interp_cursor\n      i32.const 48\n      i32.store8\n      local.get $__interp_cursor\n      i32.const 1\n      i32.add\n      local.set $__interp_cursor\n    else\n      local.get $__interp_cursor\n      local.set $__interp_start\n      loop $interp_digits\n        local.get $__interp_cursor\n        local.get $__interp_tmp\n        i32.const 10\n        i32.rem_u\n        i32.const 48\n        i32.add\n        i32.store8\n        local.get $__interp_cursor\n        i32.const 1\n        i32.add\n        local.set $__interp_cursor\n        local.get $__interp_tmp\n        i32.const 10\n        i32.div_u\n        local.set $__interp_tmp\n        local.get $__interp_tmp\n        i32.const 0\n        i32.ne\n        br_if $interp_digits\n      end\n      local.get $__interp_start\n      local.set $__interp_lo\n      local.get $__interp_cursor\n      i32.const 1\n      i32.sub\n      local.set $__interp_hi\n      block $interp_rev_done\n        loop $interp_rev\n          local.get $__interp_lo\n          local.get $__interp_hi\n          i32.ge_u\n          br_if $interp_rev_done\n          local.get $__interp_lo\n          i32.load8_u\n          local.set $__interp_swap\n          local.get $__interp_lo\n          local.get $__interp_hi\n          i32.load8_u\n          i32.store8\n          local.get $__interp_hi\n          local.get $__interp_swap\n          i32.store8\n          local.get $__interp_lo\n          i32.const 1\n          i32.add\n          local.set $__interp_lo\n          local.get $__interp_hi\n          i32.const 1\n          i32.sub\n          local.set $__interp_hi\n          br $interp_rev\n        end\n      end\n    end"

    let renderStringCopy exprCode =
        $"{exprCode}\n    local.set $__interp_ptr\n    local.get $__interp_ptr\n    i32.const 4\n    i32.sub\n    i32.load\n    local.set $__interp_copy_len\n    local.get $__interp_ptr\n    local.set $__interp_src\n    block $interp_copy_done\n      loop $interp_copy\n        local.get $__interp_copy_len\n        i32.const 0\n        i32.eq\n        br_if $interp_copy_done\n        local.get $__interp_cursor\n        local.get $__interp_src\n        i32.load8_u\n        i32.store8\n        local.get $__interp_cursor\n        i32.const 1\n        i32.add\n        local.set $__interp_cursor\n        local.get $__interp_src\n        i32.const 1\n        i32.add\n        local.set $__interp_src\n        local.get $__interp_copy_len\n        i32.const 1\n        i32.sub\n        local.set $__interp_copy_len\n        br $interp_copy\n      end\n    end"

    let renderBoolLength exprCode =
        $"{exprCode}\n    if\n      local.get $__interp_len\n      i32.const 4\n      i32.add\n      local.set $__interp_len\n    else\n      local.get $__interp_len\n      i32.const 5\n      i32.add\n      local.set $__interp_len\n    end"

    let renderBoolWrite exprCode =
        let trueBytes = renderStaticBytes (Encoding.UTF8.GetBytes "true")
        let falseBytes = renderStaticBytes (Encoding.UTF8.GetBytes "false")
        $"{exprCode}\n    if\n      {trueBytes}\n    else\n      {falseBytes}\n    end"

    // Each `{...}` hole is evaluated exactly once, up front, into `__interp_hole_N`. The length and
    // write passes then only read that local. Evaluating a hole in the write pass would be wrong twice
    // over: its side effects would run again, and anything it allocated would land on top of the
    // output buffer, which is only reserved once the whole string has been written.
    let interpolationHoleType inner =
        match tryGetExprTypeName env inner with
        | Some ("string" | "int" | "bool" as typeName) -> typeName
        | Some other -> failwith $"Unsupported interpolated value type for WASM MVP backend: {other}"
        | None -> failwith $"Unable to determine interpolated value type for WASM MVP backend: {inner}"

    let renderInterpolationHoleEval (index: int) (inner: Expression) =
        $"{transpileExpression env inner}\n    local.set ${interpolationHoleLocalName index}"

    let renderInterpolationPartLength (part, holeIndex) =
        match part with
        | StringText text ->
            let len = Encoding.UTF8.GetByteCount text
            $"local.get $__interp_len\n    i32.const {len}\n    i32.add\n    local.set $__interp_len"
        | StringExpr inner ->
            let valueCode = $"local.get ${interpolationHoleLocalName holeIndex}"
            match interpolationHoleType inner with
            | "string" ->
                $"{valueCode}\n    i32.const 4\n    i32.sub\n    i32.load\n    local.get $__interp_len\n    i32.add\n    local.set $__interp_len"
            | "int" -> renderIntLength valueCode
            | _ -> renderBoolLength valueCode

    let renderInterpolationPartWrite (part, holeIndex) =
        match part with
        | StringText text -> renderStaticBytes (Encoding.UTF8.GetBytes text)
        | StringExpr inner ->
            let valueCode = $"local.get ${interpolationHoleLocalName holeIndex}"
            match interpolationHoleType inner with
            | "string" -> renderStringCopy valueCode
            | "int" -> renderIntWrite valueCode
            | _ -> renderBoolWrite valueCode

    match expr with
    | UnitExpr ->
        "i32.const 0"
    | LiteralExpr(IntLit value) ->
        $"i32.const {value}"
    | LiteralExpr(BoolLit value) ->
        if value then "i32.const 1" else "i32.const 0"
    | LiteralExpr(StringLit value) -> stringLiteralCode value
    | InterpolatedString parts ->
        // Pair every part with the index of the hole local it reads (text parts get a dummy index).
        let indexedParts =
            parts
            |> List.fold (fun (acc, nextHole) part ->
                match part with
                | StringExpr _ -> ((part, nextHole) :: acc, nextHole + 1)
                | StringText _ -> ((part, -1) :: acc, nextHole)) ([], 0)
            |> fst
            |> List.rev
        let evalCode =
            indexedParts
            |> List.choose (function
                | (StringExpr inner, holeIndex) -> Some (renderInterpolationHoleEval holeIndex inner)
                | _ -> None)
            |> String.concat "\n    "
        let lengthCode = indexedParts |> List.map renderInterpolationPartLength |> String.concat "\n    "
        let writeCode = indexedParts |> List.map renderInterpolationPartWrite |> String.concat "\n    "
        $"{evalCode}\n    i32.const 0\n    local.set $__interp_len\n    {lengthCode}\n    global.get $heap_ptr\n    local.tee $__interp_base\n    local.get $__interp_len\n    i32.store\n    local.get $__interp_base\n    i32.const 4\n    i32.add\n    local.set $__interp_cursor\n    {writeCode}\n    local.get $__interp_cursor\n    i32.const 0\n    i32.store8\n    local.get $__interp_base\n    local.get $__interp_len\n    i32.const 5\n    i32.add\n    i32.add\n    global.set $heap_ptr\n    local.get $__interp_base\n    i32.const 4\n    i32.add"
    | IdentifierExpr(name, _) ->
        match env.Locals |> Map.tryFind name with
        | Some localName -> $"local.get ${localName}"
        | None when env.KnownFunctions.Contains name ->
            match env.FunctionArities |> Map.tryFind name with
            | Some arity when arity > 0 ->
                // A function with parameters, referenced rather than called: the value is its table slot.
                if env.FunctionContextRequirements.ContainsKey name then
                    failwith $"Unsupported for WASM MVP backend: '{name}' requires contexts and cannot be used as a value"
                $"i32.const {functionValueSlot env name arity}"
            | _ ->
                // A top-level value (or a parameterless function): evaluate it.
                $"call ${sanitizeIdentifier name}"
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
    | BinaryOp("++", left, right) ->
        // Concatenation: the backend has to know whether it is joining strings or lists.
        let leftCode = transpileExpression env left
        let rightCode = transpileExpression env right
        let operandType =
            tryGetExprTypeName env left |> Option.orElseWith (fun () -> tryGetExprTypeName env right)
        match operandType with
        | Some "string" ->
            env.UsedHelpers.Add Helpers.stringConcat |> ignore
            $"{leftCode}\n    {rightCode}\n    call ${Helpers.stringConcat}"
        | Some "list" ->
            env.UsedHelpers.Add Helpers.listConcat |> ignore
            $"{leftCode}\n    {rightCode}\n    call ${Helpers.listConcat}"
        | Some other -> failwith $"Unsupported operand type for ++ in WASM MVP backend: {other}"
        | None -> failwith $"Unable to tell whether ++ joins strings or lists for WASM MVP backend: {expr}"
    | BinaryOp(op, left, right) when (op = "==" || op = "!=") && (isStringExpr env left || isStringExpr env right) ->
        // Strings are pointers, so equality calls into the runtime helper instead of comparing the pointers.
        let leftCode = transpileExpression env left
        let rightCode = transpileExpression env right
        match op with
        | "==" ->
            env.UsedHelpers.Add Helpers.stringEquals |> ignore
            $"{leftCode}\n    {rightCode}\n    call ${Helpers.stringEquals}"
        | _ ->
            env.UsedHelpers.Add Helpers.stringEquals |> ignore
            $"{leftCode}\n    {rightCode}\n    call ${Helpers.stringEquals}\n    i32.eqz"
    | BinaryOp(op, left, right) ->
        match wasmBinaryInstruction op with
        | Some instr ->
            let leftCode = transpileExpression env left
            let rightCode = transpileExpression env right
            $"{leftCode}\n    {rightCode}\n    {instr}"
        | None ->
            failwith $"Unsupported binary operator for WASM MVP backend: {op}"
    | ListExpr items ->
        // A list is 0 (empty) or a pointer to a cell of (head, tail). Evaluate the items left to right, push
        // the empty list, then cons from the back.
        env.UsedHelpers.Add Helpers.listCons |> ignore
        let itemCodes = items |> List.map (transpileExpression env)
        let consCalls = items |> List.map (fun _ -> $"call ${Helpers.listCons}")
        (itemCodes @ [ "i32.const 0" ] @ consCalls) |> String.concat "\n    "
    | IfExpr(cond, thenExpr, elseExpr) ->
        let condCode = transpileExpression env cond
        let thenCode = transpileExpression env thenExpr
        let elseCode = transpileExpression env elseExpr
        $"{condCode}\n    (if (result i32)\n      (then\n        {thenCode}\n      )\n      (else\n        {elseCode}\n      )\n    )"
    | TupleExpr items ->
        // A tuple is a block of one 4-byte slot per item, in order.
        allocateSlots items
    | RecordExpr fields ->
        // Sort fields alphabetically by name, store in that order on heap
        let sortedFields =
            fields
            |> List.mapi (fun i field ->
                match field with
                | NamedField(name, expr) -> (name, expr)
                | PositionalField expr -> (string i, expr))
            |> List.sortBy fst
        // A record is a block of one 4-byte slot per field, in field-name order.
        allocateSlots (sortedFields |> List.map snd)
    | MemberAccess(recordExpr, fieldName, _) ->
        // Support both numeric indices (t.0, t.1) and named fields (r.name)
        match System.Int32.TryParse(fieldName) with
        | true, index ->
            // Numeric index - direct offset calculation
            let recordCode = transpileExpression env recordExpr
            let offset = index * 4
            $"{recordCode}\n    i32.const {offset}\n    i32.add\n    i32.load"
        | false, _ ->
            // Named field: find its offset from the record's layout. The inferred type is the most general
            // source (it covers nested records, constructor results and top-level values); the syntactic
            // bookkeeping below remains for modules compiled without type information.
            let loadField (fieldNames: string list) =
                match fieldNames |> List.tryFindIndex ((=) fieldName) with
                | Some index ->
                    let recordCode = transpileExpression env recordExpr
                    let offset = index * 4
                    Some $"{recordCode}\n    i32.const {offset}\n    i32.add\n    i32.load"
                | None -> None
            match tryRecordLayoutOfExpr env recordExpr |> Option.bind loadField with
            | Some code -> code
            | None ->
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

        // The code that loads element `index` of the list in `tempName`; `listTailCode` the tail after `count` cells.
        let listTailCode tempName count =
            $"local.get ${tempName}" + String.replicate count "\n      i32.const 4\n      i32.add\n      i32.load"
        let listElementCode tempName index =
            $"{listTailCode tempName index}\n      i32.load"

        let patternCondition pattern tempName =
            match pattern with
            | ListPattern(elements, restOpt) ->
                env.UsedHelpers.Add Helpers.listLength |> ignore
                let lengthInstr = if restOpt.IsSome then "i32.ge_s" else "i32.eq"
                let lengthCheck =
                    $"local.get ${tempName}\n      call ${Helpers.listLength}\n      i32.const {elements.Length}\n      {lengthInstr}"
                let elementChecks =
                    elements
                    |> List.mapi (fun index element ->
                        match element with
                        | IdentifierPattern _
                        | WildcardPattern
                        | ElsePattern -> None
                        | LiteralPattern(IntLit value) ->
                            Some $"{listElementCode tempName index}\n      i32.const {value}\n      i32.eq"
                        | LiteralPattern(BoolLit value) ->
                            Some $"{listElementCode tempName index}\n      i32.const {(if value then 1 else 0)}\n      i32.eq"
                        | other -> failwith $"Unsupported list element pattern for WASM MVP backend: {other}")
                    |> List.choose id
                match restOpt with
                | None
                | Some (IdentifierPattern _)
                | Some WildcardPattern
                | Some ElsePattern -> ()
                | Some other -> failwith $"Unsupported list rest pattern for WASM MVP backend: {other}"
                (lengthCheck :: elementChecks) |> String.concat "\n      " |> fun code ->
                    code + String.replicate elementChecks.Length "\n      i32.and"
            | LiteralPattern(IntLit value) ->
                $"local.get ${tempName}\n      i32.const {value}\n      i32.eq"
            | LiteralPattern(BoolLit value) ->
                let asInt = if value then 1 else 0
                $"local.get ${tempName}\n      i32.const {asInt}\n      i32.eq"
            | LiteralPattern(StringLit value) ->
                env.UsedHelpers.Add Helpers.stringEquals |> ignore
                $"local.get ${tempName}\n      {stringLiteralCode value}\n      call ${Helpers.stringEquals}"
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
                | Some (LiteralPattern(StringLit payload)) ->
                    // The payload is a string pointer; compare contents, not addresses. Only compare once
                    // the discriminant matches: another tag's payload bits are not a valid pointer.
                    env.UsedHelpers.Add Helpers.stringEquals |> ignore
                    $"local.get ${tempName}\n      i32.const {tagDiscriminantMask}\n      i32.and\n      i32.const {tagBase}\n      i32.eq\n      (if (result i32)\n        (then\n          local.get ${tempName}\n          i32.const {tagPayloadMask}\n          i32.and\n          {stringLiteralCode payload}\n          call ${Helpers.stringEquals})\n        (else i32.const 0))"
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
                let payloadIdents = patterns |> List.collect patternBoundIdentifiers
                let envWithPayloadLocals =
                    payloadIdents
                    |> List.fold (fun acc ident ->
                        let localName = $"__payload_{ident}"
                        { acc with Locals = acc.Locals |> Map.add ident localName }) envForNested
                // A bare identifier pattern (`| n -> ...`, `| 0, y -> ...`) needs no local of its own: it
                // is an alias for the scrutinee temp it was matched against.
                let envWithPayloadLocals =
                    List.zip patterns matchTempNames
                    |> List.fold (fun acc (pat, temp) ->
                        match pat with
                        | IdentifierPattern ident -> { acc with Locals = acc.Locals |> Map.add ident temp }
                        | _ -> acc) envWithPayloadLocals
                let payloadLocal ident =
                    match envWithPayloadLocals.Locals |> Map.tryFind ident with
                    | Some localName -> localName
                    | None -> failwith $"Internal error: missing local for payload variable {ident}"
                let patternPayloadBinds =
                    List.zip patterns matchTempNames
                    |> List.collect (fun (pat, temp) ->
                        match pat with
                        | TagPattern(_, Some (IdentifierPattern ident)) ->
                            [ $"local.get ${temp}\n        i32.const {tagPayloadMask}\n        i32.and\n        local.set ${payloadLocal ident}" ]
                        | ListPattern(elements, restOpt) ->
                            let elementBinds =
                                elements
                                |> List.mapi (fun index element ->
                                    match element with
                                    | IdentifierPattern ident ->
                                        Some $"{listElementCode temp index}\n        local.set ${payloadLocal ident}"
                                    | _ -> None)
                                |> List.choose id
                            let restBinds =
                                match restOpt with
                                | Some (IdentifierPattern ident) ->
                                    [ $"{listTailCode temp elements.Length}\n        local.set ${payloadLocal ident}" ]
                                | _ -> []
                            elementBinds @ restBinds
                        | _ -> [])
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
    | FunctionCall("not", _, [ operand ]) ->
        $"{transpileExpression env operand}\n    i32.eqz"
    | FunctionCall(name, _, args) when name = "dbg" ->
        let dbgLocal =
            match env.DbgTempLocal with
            | Some localName -> localName
            | None -> failwith "Internal error: dbg local is not configured for WASM backend"
        let argExpr =
            match flattenCallArgs args with
            | [single] -> single
            | _ -> failwith "dbg expects a single argument"
        let isStringTypedExpr expr =
            match tryGetExprTypeName env expr with
            | Some "string" -> true
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
                allocateSlots (sortedFields |> List.map snd)
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
            | None when env.Locals.ContainsKey name ->
                // Calling a parameter or local that holds a function value: its value is a table slot.
                env.FunctionTable.IndirectArities.Add flatArgs.Length |> ignore
                let callee = $"local.get ${env.Locals.[name]}\n    call_indirect (type $ctx_fn_{flatArgs.Length})"
                let prefix = argsCode |> String.concat "\n    "
                if prefix = "" then callee else $"{prefix}\n    {callee}"
            | None when NyxCompiler.Intrinsics.isIntrinsic name ->
                // A compiler intrinsic (only the standard library can call these): a runtime helper of the same
                // name. Every Nanyx expression leaves an i32, so a unit result is materialised as 0.
                env.UsedHelpers.Add name |> ignore
                let unitResult = if NyxCompiler.Intrinsics.returnsUnit name then "\n    i32.const 0" else ""
                let prefix = argsCode |> String.concat "\n    "
                if prefix = "" then $"call ${name}{unitResult}" else $"{prefix}\n    call ${name}{unitResult}"
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
                                    // Not in ContextFunctions - check ContextParams, then the ambient instance
                                    match env.ContextParams |> Map.tryFind resolvedName, env.AmbientContextFunctions |> Map.tryFind resolvedName with
                                    | Some paramName, _ -> $"local.get ${paramName}"
                                    | None, Some ambientFn -> $"i32.const {ctxFnSlot ambientFn}"
                                    | None, None ->
                                        let available =
                                            (env.ContextFunctions |> Map.toList |> List.map fst) @
                                            (env.ContextParams |> Map.toList |> List.map fst) @
                                            (env.AmbientContextFunctions |> Map.toList |> List.map fst)
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
    | Pipe(value, funcName, rangeOpt, args) ->
        // Pipe x \f(a, b) is the call f(x, a, b), with everything a call gets (contexts, intrinsics, ...).
        transpileExpression env (FunctionCall(funcName, rangeOpt, value :: flattenCallArgs args))
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
                                RecordFieldTypes = env.RecordFieldTypes |> Map.add name fieldTypes
                                ValueTypes = env.ValueTypes |> Map.add name "record" }
                        | TupleExpr items ->
                            let fieldNames = items |> List.mapi (fun i _ -> string i)
                            let fieldTypes = items |> List.mapi (fun i expr -> (string i, inferTypeFromExpr expr)) |> Map.ofList
                            { env with
                                RecordLayouts = env.RecordLayouts |> Map.add name fieldNames
                                RecordFieldTypes = env.RecordFieldTypes |> Map.add name fieldTypes
                                ValueTypes = env.ValueTypes |> Map.add name "tuple" }
                        | FunctionCall(typeName, _, _) when env.TypeLayouts |> Map.containsKey typeName ->
                            // Type constructor call - use the type's field types
                            let typeFieldTypes = env.TypeFieldTypes |> Map.tryFind typeName |> Option.defaultValue Map.empty
                            let typeLayout = env.TypeLayouts |> Map.tryFind typeName |> Option.defaultValue []
                            { env with
                                RecordLayouts = env.RecordLayouts |> Map.add name typeLayout
                                RecordFieldTypes = env.RecordFieldTypes |> Map.add name typeFieldTypes
                                ValueTypes = env.ValueTypes |> Map.add name typeName }
                        | _ ->
                            match tryGetExprTypeName env rhs with
                            | Some tyName -> { env with ValueTypes = env.ValueTypes |> Map.add name tyName }
                            | None -> env
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

/// Lambda lifting. A lambda with parameters that appears as a value (a call or pipe argument, the right-hand
/// side of a local `def`, ...) becomes a top-level function `__lambda_N`, and the expression becomes a reference
/// to that name, so the rest of the backend only ever meets lambdas as definitions. Only a lambda that captures
/// nothing from its enclosing function is lifted; a capturing one is left in place and reported as unsupported
/// when it is lowered (closures are not supported yet).
module private LambdaLifting =
    /// Every name an expression refers to: identifiers, callees and pipe targets.
    let rec private referencedNames (expr: Expression) : string list =
        match expr with
        | IdentifierExpr(name, _) -> [ name ]
        | FunctionCall(name, _, args) -> name :: (args |> List.collect referencedNames)
        | Pipe(value, name, _, args) -> name :: referencedNames value @ (args |> List.collect referencedNames)
        | Lambda(_, body) -> referencedNames body
        | BinaryOp(_, left, right) -> referencedNames left @ referencedNames right
        | IfExpr(cond, thenExpr, elseExpr) -> referencedNames cond @ referencedNames thenExpr @ referencedNames elseExpr
        | Block statements -> statements |> List.collect referencedNamesStatement
        | Match(values, arms) ->
            (values |> List.collect referencedNames)
            @ (arms |> List.collect (fun (patterns, body) -> (patterns |> List.collect referencedNamesPattern) @ referencedNames body))
        | TupleExpr items
        | ListExpr items -> items |> List.collect referencedNames
        | RecordExpr fields ->
            fields |> List.collect (function NamedField(_, value) | PositionalField value -> referencedNames value)
        | TagExpr(_, payload) -> payload |> Option.map referencedNames |> Option.defaultValue []
        | MemberAccess(inner, _, _) -> referencedNames inner
        | UseIn(binding, body) -> referencedNamesBinding binding @ referencedNames body
        | WorkflowBindExpr(_, value)
        | WorkflowReturnExpr value -> referencedNames value
        | InterpolatedString parts ->
            parts |> List.collect (function StringExpr value -> referencedNames value | StringText _ -> [])
        | ContextMemberCall(_, _, args) -> args |> List.collect referencedNames
        | ContextMemberAccess _
        | UnitExpr
        | LiteralExpr _ -> []

    and private referencedNamesStatement (statement: Statement) : string list =
        match statement with
        | DefStatement(_, _, _, rhs) -> referencedNames rhs
        | ExprStatement value -> referencedNames value
        | UseStatement binding -> referencedNamesBinding binding
        | ImportStatement _
        | TypeDefStatement _ -> []

    and private referencedNamesBinding (binding: UseBinding) : string list =
        match binding with
        | UseValue value
        | UseContextInstance(_, value) -> referencedNames value

    and private referencedNamesPattern (pattern: Pattern) : string list =
        match pattern with
        | GuardPattern(_, value) -> referencedNames value
        | RangePattern(startValue, endValue) -> referencedNames startValue @ referencedNames endValue
        | TagPattern(_, Some payload) -> referencedNamesPattern payload
        | TuplePattern patterns
        | ListPattern(patterns, _) -> patterns |> List.collect referencedNamesPattern
        | ListSplatMiddle(before, after) -> (before @ after) |> List.collect referencedNamesPattern
        | RecordPattern(_, patterns) -> patterns |> List.collect referencedNamesPattern
        | RecordMemberPattern members -> members |> List.collect (snd >> referencedNamesPattern)
        | TagPattern(_, None)
        | LiteralPattern _
        | IdentifierPattern _
        | WildcardPattern
        | ElsePattern -> []

    /// Every name a pattern binds.
    let rec private boundNamesPattern (pattern: Pattern) : string list =
        match pattern with
        | IdentifierPattern name -> [ name ]
        | TagPattern(_, Some payload) -> boundNamesPattern payload
        | TuplePattern patterns -> patterns |> List.collect boundNamesPattern
        | ListPattern(patterns, rest) ->
            (patterns |> List.collect boundNamesPattern) @ (rest |> Option.map boundNamesPattern |> Option.defaultValue [])
        | ListSplatMiddle(before, after) -> (before @ after) |> List.collect boundNamesPattern
        | RecordPattern(_, patterns) -> patterns |> List.collect boundNamesPattern
        | RecordMemberPattern members -> members |> List.collect (fun (name, p) -> name :: boundNamesPattern p)
        | TagPattern(_, None)
        | LiteralPattern _
        | GuardPattern _
        | RangePattern _
        | WildcardPattern
        | ElsePattern -> []

    /// Every name bound anywhere inside an expression: local defs, lambda parameters and pattern variables.
    /// (An over-approximation of what a nested lambda could capture, which errs on the side of not lifting.)
    let rec private boundNames (expr: Expression) : string list =
        match expr with
        | Lambda(args, body) -> (args |> List.map fst) @ boundNames body
        | Block statements ->
            statements
            |> List.collect (function
                | DefStatement(_, name, _, rhs) -> name :: boundNames rhs
                | ExprStatement value -> boundNames value
                | UseStatement _
                | ImportStatement _
                | TypeDefStatement _ -> [])
        | Match(values, arms) ->
            (values |> List.collect boundNames)
            @ (arms |> List.collect (fun (patterns, body) -> (patterns |> List.collect boundNamesPattern) @ boundNames body))
        | FunctionCall(_, _, args)
        | ContextMemberCall(_, _, args) -> args |> List.collect boundNames
        | Pipe(value, _, _, args) -> boundNames value @ (args |> List.collect boundNames)
        | BinaryOp(_, left, right) -> boundNames left @ boundNames right
        | IfExpr(cond, thenExpr, elseExpr) -> boundNames cond @ boundNames thenExpr @ boundNames elseExpr
        | TupleExpr items
        | ListExpr items -> items |> List.collect boundNames
        | RecordExpr fields -> fields |> List.collect (function NamedField(_, value) | PositionalField value -> boundNames value)
        | TagExpr(_, payload) -> payload |> Option.map boundNames |> Option.defaultValue []
        | MemberAccess(inner, _, _) -> boundNames inner
        | UseIn(_, body) -> boundNames body
        | WorkflowBindExpr(name, value) -> name :: boundNames value
        | WorkflowReturnExpr value -> boundNames value
        | InterpolatedString parts -> parts |> List.collect (function StringExpr value -> boundNames value | StringText _ -> [])
        | ContextMemberAccess _
        | UnitExpr
        | LiteralExpr _
        | IdentifierExpr _ -> []

    /// Lifts the liftable lambdas of a module into top-level definitions appended at the end.
    let lift (module': Module) : Module =
        let lifted = System.Collections.Generic.List<TopLevelItem>()
        let mutable counter = 0

        // `scope` is every name the enclosing function binds; a lambda referring to one of them captures it.
        let rec liftExpr (scope: Set<string>) (expr: Expression) : Expression =
            let recur = liftExpr scope
            match expr with
            | Lambda(args, body) when not args.IsEmpty ->
                let parameterNames = args |> List.map fst |> Set.ofList
                let innerScope = Set.union parameterNames (boundNames body |> Set.ofList)
                let liftedBody = liftExpr innerScope body
                let free =
                    referencedNames liftedBody
                    |> Set.ofList
                    |> fun names -> Set.difference names innerScope
                if Set.intersect free scope |> Set.isEmpty then
                    let name = $"__lambda_{counter}"
                    counter <- counter + 1
                    lifted.Add(Def (ValueDef(false, name, None, Lambda(args, liftedBody))))
                    IdentifierExpr(name, None)
                else
                    Lambda(args, liftedBody)
            | Lambda(args, body) -> Lambda(args, recur body)
            | FunctionCall(name, range, args) -> FunctionCall(name, range, args |> List.map recur)
            | Pipe(value, name, range, args) -> Pipe(recur value, name, range, args |> List.map recur)
            | BinaryOp(op, left, right) -> BinaryOp(op, recur left, recur right)
            | IfExpr(cond, thenExpr, elseExpr) -> IfExpr(recur cond, recur thenExpr, recur elseExpr)
            | Block statements ->
                statements
                |> List.map (function
                    | DefStatement(isExport, name, typeOpt, rhs) -> DefStatement(isExport, name, typeOpt, recur rhs)
                    | ExprStatement value -> ExprStatement(recur value)
                    // `use` bindings are hoisted by the context machinery; leave them to it.
                    | other -> other)
                |> Block
            | Match(values, arms) -> Match(values |> List.map recur, arms |> List.map (fun (patterns, body) -> patterns, recur body))
            | TupleExpr items -> TupleExpr(items |> List.map recur)
            | ListExpr items -> ListExpr(items |> List.map recur)
            | RecordExpr fields ->
                fields
                |> List.map (function
                    // A record of lambdas is how a context instance is written (`use (f = { ... })`), and the
                    // context machinery hoists those lambdas itself: leave them in place, lift inside only.
                    | NamedField(name, Lambda(args, body)) ->
                        let innerScope = Set.union (args |> List.map fst |> Set.ofList) (boundNames body |> Set.ofList)
                        NamedField(name, Lambda(args, liftExpr (Set.union scope innerScope) body))
                    | NamedField(name, value) -> NamedField(name, recur value)
                    | PositionalField value -> PositionalField(recur value))
                |> RecordExpr
            | TagExpr(name, payload) -> TagExpr(name, payload |> Option.map recur)
            | MemberAccess(inner, field, range) -> MemberAccess(recur inner, field, range)
            | UseIn(binding, body) -> UseIn(binding, recur body)
            | WorkflowBindExpr(name, value) -> WorkflowBindExpr(name, recur value)
            | WorkflowReturnExpr value -> WorkflowReturnExpr(recur value)
            | InterpolatedString parts ->
                parts
                |> List.map (function
                    | StringExpr value -> StringExpr(recur value)
                    | text -> text)
                |> InterpolatedString
            | ContextMemberCall(ctx, name, args) -> ContextMemberCall(ctx, name, args |> List.map recur)
            | ContextMemberAccess _
            | UnitExpr
            | LiteralExpr _
            | IdentifierExpr _ -> expr

        let liftItem (item: TopLevelItem) : TopLevelItem =
            match item with
            // A definition's own lambda is the function being defined; lift inside its body only.
            | Def (ValueDef(isExport, name, typeOpt, Lambda(args, body))) ->
                let scope = Set.union (args |> List.map fst |> Set.ofList) (boundNames body |> Set.ofList)
                Def (ValueDef(isExport, name, typeOpt, Lambda(args, liftExpr scope body)))
            | Def (ValueDef(isExport, name, typeOpt, rhs)) ->
                Def (ValueDef(isExport, name, typeOpt, liftExpr (boundNames rhs |> Set.ofList) rhs))
            | Expr expr -> Expr (liftExpr (boundNames expr |> Set.ofList) expr)
            | other -> other

        let items = module' |> List.map liftItem
        items @ List.ofSeq lifted

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

let private transpileModuleToWatCore (module': Module) (typedFunctionBodies: Map<string, TypedExpr>) (typedValueTypes: Map<string, string>) (typedReturnTypes: Map<string, string>) (typedTopLevelTys: Map<string, Ty>) : string =
    let module' = LambdaLifting.lift module'
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

    // The ambient context instance: a module-level function named like a member of a declared context stands
    // in for that member whenever a callee requires the context and the caller has nothing else to supply.
    // This is how the runtime-provided `Console` (the functions `nanyx/console` exports) reaches `@entry`.
    let contextMemberNames =
        module'
        |> List.collect (function
            | Def (TypeDef(_, modifiers, _, TypeRecord fields)) when modifiers |> List.contains Context ->
                fields
                |> List.map (function
                    | TypeField(fieldName, _, _, _, _) -> fieldName
                    | TypeMember(fieldName, _) -> fieldName)
            | _ -> [])
        |> List.distinct
    let ambientContextFunctions =
        // Only worth table slots when some function actually takes contexts.
        if functionContextRequirements.IsEmpty then [] else contextMemberNames
        |> List.choose (fun memberName ->
            defs
            |> List.tryFind (fun (defName, _, _) -> defName = memberName && not (functionContextRequirements.ContainsKey defName))
            |> Option.map (fun (defName, _, expr) ->
                let parameters, _ = defBodyAndParameters expr
                let ctxFn: ContextFn = sanitizeIdentifier defName, nextContextIndex, parameters.Length
                nextContextIndex <- nextContextIndex + 1
                contextFnList <- contextFnList @ [ ctxFn ]
                memberName, ctxFn))
        |> Map.ofList

    // Functions used as values take the table slots after the ones the context machinery assigned.
    let functionTable: FunctionTable =
        { Values = System.Collections.Generic.Dictionary<string, ContextFn>()
          NextSlot = nextContextIndex
          IndirectArities = System.Collections.Generic.HashSet<int>() }

    // The result types the backend can read off definitions' annotations, plus what inference told it.
    let functionReturnTypes =
        let fromAnnotations =
            defs
            |> List.choose (fun (name, typeOpt, _) ->
                match unwrapContextType typeOpt with
                | Some (TypeFunc(_, TypeName "string")) -> Some (name, "string")
                | Some (TypeFunc(_, TypeName "int")) -> Some (name, "int")
                | Some (TypeFunc(_, TypeName "bool")) -> Some (name, "bool")
                | Some (TypeFunc(_, TypeApply("list", _))) -> Some (name, "list")
                | _ -> None)
            |> Map.ofList
        let fromIntrinsics =
            [ Helpers.stringLength, "int"
              Helpers.stringByteAt, "int"
              Helpers.stringSlice, "string"
              Helpers.stringFromByte, "string" ]
            |> Map.ofList
        typedReturnTypes
        |> Map.fold (fun acc key value -> acc |> Map.add key value) fromAnnotations
        |> Map.fold (fun acc key value -> acc |> Map.add key value) fromIntrinsics

    let usedHelpers = System.Collections.Generic.HashSet<string>()

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

    let renderFunction (name: string) (defTypeOpt: TypeExpr option) (expr: Expression) (typedExprOpt: TypedExpr option) (exportFn: bool) (pendingUseBindings: Map<string, ContextFn> list) =
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

            let interpolationLocals = interpolationLocalsFor bodyExpr

            for localName in interpolationLocals do
                usedLocalNames <- usedLocalNames |> Set.add localName

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

            // `(a, b) -> r` annotates a two-parameter function with one tuple type: split it over the parameters.
            // The annotation can describe more inputs than the lambda declares: `() -> r` has a unit input but
            // no parameters, and `a -> (b -> c)` is a one-parameter function that returns a function. Only keep
            // as many types as there are parameters, so the padding below never needs a negative count.
            let defParamTypes =
                let annotated =
                    match unwrapContextType defTypeOpt with
                    | Some (TypeFunc(TypeTuple items, _)) when items.Length = parameterNames.Length -> items
                    | Some (TypeFunc(TypeUnit, _)) when parameterNames.IsEmpty -> []
                    | Some t -> extractParamTypes t
                    | None -> []
                annotated |> List.truncate parameterNames.Length

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
                | TypeApply("list", _) -> Some "list"
                | TypeTuple _ -> Some "tuple"
                | TypeRecord _ -> Some "record"
                | TypeFunc _ -> Some "func"
                | _ -> None
            let initialParamTypes =
                List.zip parameterNames (defParamTypes @ List.replicate (parameterNames.Length - defParamTypes.Length) TypeUnit)
                |> List.choose (fun (paramName, paramType) ->
                    typeExprToString paramType |> Option.map (fun t -> paramName, t))
                |> Map.ofList

            let typedLocalValueTypes =
                typedExprOpt
                |> Option.map collectTypedValueTypes
                |> Option.defaultValue Map.empty

            let valueTys =
                typedExprOpt
                |> Option.map collectTypedValueTys
                |> Option.defaultValue Map.empty
                |> Map.fold (fun acc key value -> acc |> Map.add key value) typedTopLevelTys

            let initialValueTypes =
                typedValueTypes
                |> Map.fold (fun acc key value -> acc |> Map.add key value) Map.empty
                |> Map.fold (fun acc key value -> acc |> Map.add key value) initialParamTypes
                |> Map.fold (fun acc key value -> acc |> Map.add key value) typedLocalValueTypes

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
                  FunctionReturnTypes = functionReturnTypes
                  Locals = locals
                  DbgTempLocal = dbgLocalName
                  TagIds = tagIdMap
                  MatchTempNames = matchTempNames
                  NextMatchTemp = 0
                  StringLiterals = stringLiteralMap
                  ContextFunctions = contextFunctionsFromParams
                  ContextParams = contextParamsMap
                  AmbientContextFunctions = ambientContextFunctions
                  PendingUseBindings = ref pendingUseBindings
                  RecordLayouts = initialRecordLayouts
                  TypeLayouts = typeLayouts
                  TypeFieldTypes = typeFieldTypes
                  RecordFieldTypes = initialRecordFieldTypes
                  ParamTypes = initialParamTypes
                  ValueTypes = initialValueTypes
                  ValueTys = valueTys
                  UsedHelpers = usedHelpers
                  FunctionTable = functionTable }
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
                [
                    yield! (localNames |> List.map (fun localName -> localMap.[localName]))
                    yield! matchTempNames
                    yield! payloadLocalNames
                    yield! interpolationLocals
                    match dbgLocalName with
                    | Some name -> yield name
                    | None -> ()
                ]
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
            let typedExprOpt = typedFunctionBodies |> Map.tryFind name
            renderFunction name typeOpt expr typedExprOpt true pending)

    let renderedContextFunctions =
        contextDefs
        |> List.map (fun (name, typeOpt, expr) -> renderFunction name typeOpt expr None false [])

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
                  yield! interpolationLocalsFor (Block (topLevelExprs |> List.map ExprStatement))
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
                  FunctionReturnTypes = functionReturnTypes
                  Locals = localMap
                  DbgTempLocal = dbgLocalName
                  TagIds = tagIdMap
                  MatchTempNames = matchTempNames
                  NextMatchTemp = 0
                  StringLiterals = stringLiteralMap
                  ContextFunctions = Map.empty
                  ContextParams = Map.empty
                  AmbientContextFunctions = ambientContextFunctions
                  PendingUseBindings = ref []
                  RecordLayouts = Map.empty
                  TypeLayouts = typeLayouts
                  TypeFieldTypes = typeFieldTypes
                  RecordFieldTypes = Map.empty
                  ParamTypes = Map.empty
                  ValueTypes = typedValueTypes
                  ValueTys = typedTopLevelTys
                  UsedHelpers = usedHelpers
                  FunctionTable = functionTable }
            let bodyCode =
                topLevelExprs
                |> List.map (fun expr -> $"{transpileExpression env expr}\n    drop")
                |> String.concat "\n    "
            let header =
                if String.IsNullOrWhiteSpace localsSig then ""
                else " " + localsSig
            $"  (func $__nanyx_start{header}\n    {bodyCode}\n  )", "  (start $__nanyx_start)"

    let moduleUsesDbgHelpers = moduleUsesRegularDbg || moduleUsesDbgString

    // The runtime helpers the lowered code asked for, closed over what they call themselves.
    let helperDependencies =
        [ Helpers.alloc, []
          Helpers.copyBytes, []
          Helpers.stringLength, []
          Helpers.stringAlloc, [ Helpers.alloc ]
          Helpers.stringByteAt, [ Helpers.stringLength ]
          Helpers.stringSlice, [ Helpers.stringLength; Helpers.stringAlloc; Helpers.copyBytes ]
          Helpers.stringFromByte, [ Helpers.stringAlloc ]
          Helpers.stringConcat, [ Helpers.stringLength; Helpers.stringAlloc; Helpers.copyBytes ]
          Helpers.stringEquals, [ Helpers.stringLength ]
          Helpers.writeStdout, [ Helpers.stringLength ]
          Helpers.listCons, [ Helpers.alloc ]
          Helpers.recordSet, []
          Helpers.listLength, []
          Helpers.listConcat, [ Helpers.listCons ] ]
    let rec closeHelpers (acc: Set<string>) (pending: string list) =
        match pending with
        | [] -> acc
        | name :: rest when acc.Contains name -> closeHelpers acc rest
        | name :: rest ->
            let deps =
                helperDependencies
                |> List.tryFind (fun (helper, _) -> helper = name)
                |> Option.map snd
                |> Option.defaultValue []
            closeHelpers (acc.Add name) (deps @ rest)
    let usedHelperSet = closeHelpers Set.empty (usedHelpers |> Seq.toList)
    let moduleUsesWriteStdout = usedHelperSet.Contains Helpers.writeStdout
    // Anything that writes to stdout shares the WASI import, the memory and the newline byte.
    let moduleUsesIoHelpers = moduleUsesDbgHelpers || moduleUsesWriteStdout
    let moduleUsesHeap = moduleUsesHeap || usedHelperSet.Contains Helpers.alloc
    let renderedHelpers =
        helperDependencies
        |> List.map fst
        |> List.filter usedHelperSet.Contains
        |> List.map (fun name -> runtimeHelpers |> Map.find name)
        |> String.concat "\n"

    // The `dbg` helpers: `$dbg` prints an integer, `$dbg_str` a string, each prefixed with "dbg: ".
    let dbgFunctions =
            [ "  (func $dbg (param $x i32)"
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

    let dbgPrelude =
        if moduleUsesIoHelpers then
            [ "  (import \"wasi_snapshot_preview1\" \"fd_write\" (func $fd_write (param i32 i32 i32 i32) (result i32)))"
              "  (memory 1)"
              "  (export \"memory\" (memory 0))"
              if moduleUsesDbgHelpers then
                  $"  (data (i32.const {dbgDataBaseOffset}) \"dbg: \")"
                  $"  (data (i32.const {dbgStrNewlineOffset}) \"\\0a\")"
                  yield! dbgFunctions ]
            |> String.concat "\n"
        else
            ""

    // Every function body has been rendered by now, so the function-value slots are final: add them to the table.
    contextFnList <- contextFnList @ (functionTable.Values.Values |> Seq.toList)

    let contextTypeDefs =
        (contextFnList |> List.map ctxFnArity) @ (functionTable.IndirectArities |> Seq.toList)
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
    let needsMemory = (not (List.isEmpty stringLiteralData) || moduleUsesHeap) && not moduleUsesIoHelpers
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

    // Imports must precede every function definition in a WebAssembly module, so the host imports for
    // `dbg(#tag)` come before the prelude (which defines $dbg and friends).
    let allImports =
        [ yield! dbgTagImports
          yield! dbgTagPayloadImports
          if not (String.IsNullOrWhiteSpace dbgPrelude) then dbgPrelude
          if not (String.IsNullOrWhiteSpace memorySection) then memorySection
          if not (String.IsNullOrWhiteSpace heapSection) then heapSection
          if not (String.IsNullOrWhiteSpace renderedHelpers) then renderedHelpers
          if not (String.IsNullOrWhiteSpace contextPrelude) then contextPrelude ]
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

let transpileModuleToWat (module': Module) : string =
    transpileModuleToWatCore module' Map.empty Map.empty Map.empty Map.empty

let transpileTypedModuleToWat (typed: TypedModule) : string =
    let typedBodies =
        typed.Items
        |> List.choose (function
            | TypedDef (TypedValueDef(_, name, _, expr)) -> Some (name, expr)
            | _ -> None)
        |> Map.ofList
    let typedValueTypes =
        typed.Types
        |> Map.toList
        |> List.choose (fun (name, ty) -> trySimpleTypeName ty |> Option.map (fun tyName -> name, tyName))
        |> Map.ofList
    let typedReturnTypes =
        typed.Types
        |> Map.toList
        |> List.choose (fun (name, ty) ->
            match ty with
            | TyFunc(_, returnTy) -> trySimpleTypeName returnTy |> Option.map (fun tyName -> name, tyName)
            | _ -> None)
        |> Map.ofList
    transpileModuleToWatCore typed.Module typedBodies typedValueTypes typedReturnTypes typed.Types
