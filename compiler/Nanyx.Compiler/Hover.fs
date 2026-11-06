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

/// The expressions whose spans contain `offset`, innermost first. Only expressions that contain the offset are
/// searched, and those without a span of their own (made-up parents), so a lookup is proportional to depth.
let expressionsAt (offset: int) (module': Module) : Expression list =
    // Each expression found is pushed before its children are searched, so they end up in front of it.
    let rec search (found: Expression list) (expr: Expression) : Expression list =
        if contains offset expr.Span then children expr |> List.fold search (expr :: found)
        elif SourceSpan.isNone expr.Span then children expr |> List.fold search found
        else found
    module' |> List.collect topLevelExpressions |> List.fold search []

/// The span of a call's or pipe's function name, when it has one.
let private calleeSpan (expr: Expression) =
    match expr.Kind with
    | FunctionCall(_, nameSpan, _)
    | Pipe(_, _, nameSpan, _) -> nameSpan
    | _ -> None

/// The hover for the expression at `offset` in the compiled root module: the type of the narrowest expression
/// there that has one, or of the function itself on a call's function name, where it is instantiated at that
/// call, with made-up type variables named by `names`. `None` when the module did not type-check or no typed
/// expression is at `offset`.
///
/// The narrowest span wins rather than the deepest node, because a made-up node can sit inside the syntax it
/// stands for: a `for` loop's element is an identifier spanning all of the loop's arms.
let typeAt (names: TypeVarNames) (result: CompileResult) (offset: int) : HoverInfo option =
    match result.Syntax, result.Typed with
    | Some syntax, Some typed ->
        let candidates =
            expressionsAt offset syntax
            |> List.collect (fun expr ->
                let callee =
                    match calleeSpan expr with
                    | Some nameSpan when contains offset nameSpan ->
                        typed.CalleeTypes |> Map.tryFind expr.Id |> Option.map (fun ty -> ty, nameSpan)
                    | _ -> None
                let own = typed.ExprTypes |> Map.tryFind expr.Id |> Option.map (fun ty -> ty, expr.Span)
                // A callee's name is inside its call, so it comes first among equals.
                Option.toList callee @ Option.toList own)
        match candidates with
        | [] -> None
        | _ ->
            // `minBy` keeps the first of equal spans: the innermost.
            let ty, span = candidates |> List.minBy (fun (_, span) -> span.End - span.Start)
            Some { Type = Unifier.tyToDisplayString names ty; Span = span }
    | _ -> None
