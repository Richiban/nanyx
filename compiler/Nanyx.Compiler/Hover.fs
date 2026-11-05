/// What an editor shows when the pointer rests on an expression: its inferred type. Positions are character
/// offsets into the source as the parser saw it, with line endings normalised to `\n`.
module NyxCompiler.Hover

open Parser.Program

/// The type shown for a hover and the span of the syntax it describes.
type HoverInfo = { Type: string; Span: SourceSpan }

let private contains (offset: int) (span: SourceSpan) =
    not (SourceSpan.isNone span) && span.Start <= offset && offset < span.End

/// The expressions directly inside `expr`, including those in its statements, match arms and patterns.
let rec private children (expr: Expression) : Expression list =
    match expr.Kind with
    | LiteralExpr _
    | UnitExpr
    | IdentifierExpr _
    | ContextMemberAccess _ -> []
    | FunctionCall(_, _, args)
    | ContextMemberCall(_, _, args) -> args
    | Lambda(_, body) -> [ body ]
    | BinaryOp(_, left, right) -> [ left; right ]
    | Pipe(value, _, _, args) -> value :: args
    | Block statements -> statements |> List.collect statementExpressions
    | Match(values, arms) ->
        values @ (arms |> List.collect (fun (patterns, armExpr) -> (patterns |> List.collect patternExpressions) @ [ armExpr ]))
    | TupleExpr items
    | ListExpr items -> items
    | RecordExpr fields ->
        fields |> List.map (function NamedField(_, value) | PositionalField value -> value)
    | TagExpr(_, payload) -> Option.toList payload
    | IfExpr(cond, thenExpr, elseExpr) -> [ cond; thenExpr; elseExpr ]
    | MemberAccess(target, _, _) -> [ target ]
    | UseIn(binding, body) -> [ bindingExpression binding; body ]
    | WorkflowBindExpr(_, value)
    | WorkflowReturnExpr value -> [ value ]
    | InterpolatedString parts -> parts |> List.choose (function StringExpr value -> Some value | StringText _ -> None)

and private statementExpressions (statement: Statement) : Expression list =
    match statement with
    | DefStatement(_, _, _, value)
    | ExprStatement value -> [ value ]
    | UseStatement binding -> [ bindingExpression binding ]
    | TypeDefStatement _
    | ImportStatement _ -> []

and private bindingExpression (binding: UseBinding) =
    match binding with
    | UseValue value
    | UseContextInstance(_, value) -> value

and private patternExpressions (pattern: Pattern) : Expression list =
    match pattern with
    | RangePattern(low, high) -> [ low; high ]
    | GuardPattern(_, value) -> [ value ]
    | TuplePattern items
    | RecordPattern(_, items) -> items |> List.collect patternExpressions
    | RecordMemberPattern members -> members |> List.collect (snd >> patternExpressions)
    | ListPattern(items, rest) -> (items @ Option.toList rest) |> List.collect patternExpressions
    | ListSplatMiddle(before, after) -> (before @ after) |> List.collect patternExpressions
    | TagPattern(_, payload) -> payload |> Option.toList |> List.collect patternExpressions
    | LiteralPattern _
    | IdentifierPattern _
    | WildcardPattern
    | ElsePattern -> []

let rec private topLevelExpressions (item: TopLevelItem) : Expression list =
    match item with
    | Def (ValueDef(_, _, _, value))
    | Expr value -> [ value ]
    | ModuleBlock(_, items) -> items |> List.collect topLevelExpressions
    | Def (TypeDef _)
    | ModuleDecl _
    | Import _
    | Attribute _ -> []

/// The expressions whose spans contain `offset`, innermost first. A child is searched even when its parent's span
/// does not contain the offset, since some made-up parents have no span of their own.
let expressionsAt (offset: int) (module': Module) : Expression list =
    let rec search (expr: Expression) : Expression list =
        let inner = children expr |> List.collect search
        if contains offset expr.Span then inner @ [ expr ] else inner
    module' |> List.collect topLevelExpressions |> List.collect search

/// The span of a call's or pipe's function name, when it has one.
let private calleeSpan (expr: Expression) =
    match expr.Kind with
    | FunctionCall(_, nameSpan, _)
    | Pipe(_, _, nameSpan, _) -> nameSpan
    | _ -> None

/// The hover for the expression at `offset` in the compiled root module: the type of the innermost expression
/// there that has one, or of the function itself on a call's function name, where it is instantiated at that
/// call. `None` when the module did not type-check or no typed expression is at `offset`.
let typeAt (result: CompileResult) (offset: int) : HoverInfo option =
    match result.Syntax with
    | None -> None
    | Some syntax ->
        expressionsAt offset syntax
        |> List.tryPick (fun expr ->
            let callee =
                match calleeSpan expr with
                | Some nameSpan when contains offset nameSpan ->
                    result.CalleeTypes
                    |> Map.tryFind expr.Id
                    |> Option.map (fun ty -> { Type = Unifier.tyToDisplayString ty; Span = nameSpan })
                | _ -> None
            match callee with
            | Some _ -> callee
            | None ->
                result.ExprTypes
                |> Map.tryFind expr.Id
                |> Option.map (fun ty -> { Type = Unifier.tyToDisplayString ty; Span = expr.Span }))
