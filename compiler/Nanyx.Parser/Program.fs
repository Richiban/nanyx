module Parser.Program

open System
open FParsec

// AST Types - Simplified for basic parsing
type Identifier = string
type ModuleName = string

/// Where a syntax node came from: character offsets into its file's source (`End` is one past the last
/// character, and trailing whitespace is not included), plus the zero-based line and column of `Start`.
type SourceSpan =
    { File: string
      Start: int
      End: int
      StartLine: int
      StartColumn: int }

module SourceSpan =
    /// The span of a node the compiler made up rather than parsed.
    let none = { File = ""; Start = -1; End = -1; StartLine = -1; StartColumn = -1 }

    let isNone (span: SourceSpan) = span.Start < 0

    /// From the start of `first` to the end of `last`; a made-up span on either side gives the other one.
    let between (first: SourceSpan) (last: SourceSpan) =
        if isNone first then last
        elif isNone last then first
        else { first with End = last.End }

    /// Whether `inner` is written within `outer`. A made-up span is within nothing.
    let contains (outer: SourceSpan) (inner: SourceSpan) =
        not (isNone outer) && not (isNone inner) && outer.File = inner.File && outer.Start <= inner.Start && inner.End <= outer.End

/// Identifies a syntax node, so that analysis results can be kept in tables keyed by node rather than in a copy
/// of the tree. Ids are unique within a process, so nodes from different modules of a compilation never clash.
type NodeId = int64

module NodeId =
    let private last = ref 0L
    let next () : NodeId = System.Threading.Interlocked.Increment(&last.contents)

type Literal =
    | StringLit of string
    | IntLit of int
    | FloatLit of float
    | BoolLit of bool

type TypeDefModifier =
    | Export
    | Context
    | Private

type ImportSource =
    | ModuleImport of string
    | PathImport of string

type ImportItem =
    { Source: ImportSource
      Alias: Identifier option
      /// The import as written: its source and alias.
      Span: SourceSpan }

type TypeExpr =
    | TypeName of Identifier
    | TypeVar of Identifier
    | TypeUnit
    | TypeTuple of TypeExpr list
    | TypeRecord of TypeRecordField list
    | TypeApply of Identifier * TypeExpr list
    | TypeFunc of TypeExpr * TypeExpr
    | TypeTag of Identifier * TypeExpr option
    | TypeUnion of TypeExpr list
    | TypeIntersection of TypeExpr list
    | TypeOptional of TypeExpr
    | TypeContext of TypeExpr list
    | TypeWithContext of TypeExpr list * TypeExpr
    | TypeConstraint of Identifier * TypeExpr * Expression
    | TypeWhere of TypeExpr * Expression

and TypeRecordField =
    | TypeField of Identifier * bool * bool * TypeExpr option * Expression option
    | TypeMember of Identifier * TypeExpr option

/// An expression and where it came from. Equality and comparison look only at `Kind`, leaving out the name spans
/// some kinds carry: two expressions are the same when they are the same code, wherever it was written. It is
/// printed (`%A`) as its kind alone.
and [<CustomEquality; CustomComparison; StructuredFormatDisplay("{Kind}")>] Expression =
    { Kind: ExpressionKind
      Span: SourceSpan
      Id: NodeId }

    /// The kind without its name span. Only the top level needs it: child expressions compare by this same rule.
    member private this.Code =
        match this.Kind with
        | FunctionCall(name, Some _, args) -> FunctionCall(name, None, args)
        | Pipe(value, name, Some _, args) -> Pipe(value, name, None, args)
        | MemberAccess(target, field, Some _) -> MemberAccess(target, field, None)
        | kind -> kind

    override this.Equals(other) =
        match other with
        | :? Expression as expr -> this.Code = expr.Code
        | _ -> false

    override this.GetHashCode() = hash this.Code

    interface IComparable with
        member this.CompareTo(other) =
            match other with
            | :? Expression as expr -> compare this.Code expr.Code
            | _ -> invalidArg "other" "an Expression can only be compared with another Expression"

/// The span on `FunctionCall`, `Pipe` and `MemberAccess` is that of the name (the function or the field), which
/// is not where the expression as a whole starts.
and ExpressionKind =
    | LiteralExpr of Literal
    | UnitExpr
    | IdentifierExpr of Identifier
    | FunctionCall of Identifier * SourceSpan option * Expression list
    | Lambda of (Identifier * TypeExpr option) list * Expression
    | BinaryOp of string * Expression * Expression
    | Pipe of Expression * Identifier * SourceSpan option * Expression list  // expr \func or expr \func(args)
    | Block of Statement list
    | Match of Expression list * MatchArm list  // Can match on multiple values
    | TupleExpr of Expression list  // Positional-only tuple (for backward compat with existing tests)
    | RecordExpr of RecordField list  // Mixed tuple/record with named and/or positional fields
    | ListExpr of Expression list
    | TagExpr of Identifier * Expression option  // #tagName or #tagName(expr)
    | IfExpr of Expression * Expression * Expression  // if condition then trueExpr else falseExpr
    | MemberAccess of Expression * Identifier * SourceSpan option  // expr.field
    | ContextMemberAccess of TypeExpr * Identifier  // C(T).field - qualified context access (as value)
    | ContextMemberCall of TypeExpr * Identifier * Expression list  // C(T).field(args) - qualified context call
    | UseIn of UseBinding * Expression
    | WorkflowBindExpr of Identifier * Expression
    | WorkflowReturnExpr of Expression
    | InterpolatedString of StringPart list

and StringPart =
    | StringText of string
    | StringExpr of Expression

// Record field: either named (x = expr) or positional (expr)
and RecordField =
    | NamedField of Identifier * Expression
    | PositionalField of Expression

and Pattern =
    | LiteralPattern of Literal
    | IdentifierPattern of Identifier
    | WildcardPattern
    | ElsePattern
    | TuplePattern of Pattern list
    | RecordPattern of Identifier * Pattern list  // Type name and patterns by position
    | RecordMemberPattern of (Identifier * Pattern) list  // Patterns by member name
    | ListPattern of Pattern list * Pattern option  // patterns, optional splat at end
    | ListSplatMiddle of Pattern list * Pattern list  // patterns before ..., patterns after
    | RangePattern of Expression * Expression  // start..end (inclusive)
    | GuardPattern of string * Expression  // operator (>, <, >=, <=, ==, !=) and value
    | TagPattern of Identifier * Pattern option  // #tagName or #tagName(pattern)

and MatchArm = Pattern list * Expression  // Multiple patterns for multi-value match

and Statement =
    | DefStatement of bool * Identifier * TypeExpr option * Expression
    | TypeDefStatement of Identifier * TypeDefModifier list * (Identifier * TypeExpr option) list * TypeExpr
    | ImportStatement of ImportItem list
    | UseStatement of UseBinding
    | ExprStatement of Expression

and UseBinding =
    | UseValue of Expression
    | UseContextInstance of TypeExpr * Expression  // use C(T) = expr

// Helper type for parsing list patterns
type ListPatternElement =
    | RegularPattern of Pattern
    | Splat of Identifier option

type Definition =
    | ValueDef of bool * Identifier * TypeExpr option * Expression
    | TypeDef of Identifier * TypeDefModifier list * (Identifier * TypeExpr option) list * TypeExpr

type TopLevelItem =
    | ModuleDecl of ModuleName
    | ModuleBlock of ModuleName * TopLevelItem list
    | Def of Definition
    | Import of ImportItem list
    | Expr of Expression
    /// An `@name` attribute line; it applies to the top-level item that follows it (e.g. `@entry`).
    | Attribute of Identifier

type Module = TopLevelItem list

/// Matches an expression by its kind inside a larger pattern: `FunctionCall(_, _, [ Kind (IdentifierExpr name) ])`.
let (|Kind|) (expr: Expression) = expr.Kind

module Expression =
    /// An expression written at `span`.
    let at (span: SourceSpan) (kind: ExpressionKind) : Expression =
        { Kind = kind; Span = span; Id = NodeId.next () }

    /// An expression the compiler made up in place of `origin`; it is reported where `origin` was written.
    let from (origin: Expression) (kind: ExpressionKind) = at origin.Span kind

    /// An expression the compiler made up that has no source of its own.
    let synthetic (kind: ExpressionKind) = at SourceSpan.none kind

/// How a node declares names. One node can declare in more than one way: `def f = { x -> x }` declares `f` by
/// the lambda as a def's value, and `x` by the lambda's parameters.
type Declarer =
    /// The value of a def declares the def's name.
    | DefValue
    /// A lambda declares its parameters.
    | LambdaParameters
    /// The body of a match arm declares the variables its patterns bind.
    | ArmBody

/// The names nodes declare, each with the span it is written at. Declarations have no nodes of their own yet
/// (#96), so their spans are kept here, keyed by the node they belong to.
type DeclaredNames = Map<NodeId * Declarer, (Identifier * SourceSpan) list>

/// What the parser keeps beside the input. FParsec restores it when it backtracks, so a declaration parsed on
/// a path that failed is forgotten with it.
type ParseState =
    { Declared: DeclaredNames
      /// Names parsed for a construct that has not been built yet (a lambda's parameters, a pattern's
      /// variables), most recent first. The construct takes them with `collectingNames`.
      PendingNames: (Identifier * SourceSpan) list
      /// How many lambdas the parser is inside.
      LambdaDepth: int
      /// The text of each `module name =` block parsed so far, with the block's name.
      ModuleBlocks: (ModuleName * SourceSpan) list
      /// The furthest workflow keyword applied to a value, where it is and what to say about it. It is a cell
      /// shared by every copy of the state, so backtracking does not forget it (see `workflowKeywordApplication`).
      WorkflowKeyword: (Position * string) option ref }

module ParseState =
    let empty () = { Declared = Map.empty; PendingNames = []; LambdaDepth = 0; ModuleBlocks = []; WorkflowKeyword = ref None }

// Helper: whitespace and comment handling
let comment () = pstring "--" >>. skipRestOfLine true
let inlineCommentNoNl () = pstring "--" >>. skipRestOfLine false
let ws: Parser<unit, ParseState> = skipMany (skipMany1 spaces1 <|> comment ())
let wsAll: Parser<unit, ParseState> = ws
let wsInline = skipMany (skipAnyOf " \t")
let wsNoNl() = skipMany (skipAnyOf " \t") // whitespace without newlines
let wsWithComments () = ws

// Indentation helpers
let getIndentation () = 
    getPosition |>> (fun pos -> int pos.Column - 1)

let skipToColumn (col: int64) =
    getPosition >>= fun pos ->
        let currentCol = pos.Column
        if currentCol < col then
            skipManyTill (pchar ' ') (getPosition >>= fun p -> if p.Column >= col then preturn () else pzero)
        else if currentCol = col then
            preturn ()
        else
            pzero

let atIndentation (refCol: int64) =
    getPosition >>= fun pos ->
        if pos.Column = refCol then preturn ()
        else pzero

let greaterIndentation (refCol: int64) =
    getPosition >>= fun pos ->
        if pos.Column > refCol then preturn ()
        else pzero

let newlineIndentedContinuation (refCol: int64) : Parser<unit, ParseState> =
    attempt (
        skipMany (skipAnyOf " \t") >>. newline >>.
        many (pchar ' ' <|> pchar '\t') >>.
        getPosition >>= fun pos ->
            if pos.Column > refCol then preturn () else pzero
    )

let defEqualsContinuation (refCol: int64) : Parser<unit, ParseState> =
    attempt (wsNoNl() >>. followedBy (pstring "=")) <|> newlineIndentedContinuation refCol

// Parser for identifiers
let identifierNoWs: Parser<Identifier, ParseState> =
    let isIdentStart c = isLetter c || c = '_'
    let isIdentContinue c = isLetter c || isDigit c || c = '_'
    many1Satisfy2 isIdentStart isIdentContinue
    <?> "identifier"

let identifier: Parser<Identifier, ParseState> = identifierNoWs .>> wsInline

let typeNameIdentifier: Parser<Identifier, ParseState> =
    (pchar '$' >>. identifierNoWs |>> fun name -> "$" + name)
    <|> identifierNoWs

let qualifiedIdentifier: Parser<Identifier, ParseState> =
    pipe2
        typeNameIdentifier
        (many (pchar '.' >>. identifierNoWs))
        (fun head tail -> String.concat "." (head :: tail))

let private spanOf (start: Position) (endIndex: int) : SourceSpan =
    { File = start.StreamName
      Start = int start.Index
      End = max endIndex (int start.Index)
      StartLine = int start.Line - 1
      StartColumn = int start.Column - 1 }

/// The index just past the last character consumed that is not whitespace. Many parsers consume the whitespace
/// after what they parse; it is not part of a node's span.
let private trimmedEnd: Parser<int, ParseState> =
    fun stream ->
        let mutable back = 0
        let isTrailingSpace (offset: int) =
            let c = stream.Peek(offset)
            c <> CharStream.EndOfStreamChar && Char.IsWhiteSpace c
        while isTrailingSpace (-(back + 1)) do
            back <- back + 1
        Reply(int stream.Index - back)

/// Runs `p` and pairs its result with the span of the text it consumed.
let withSpan (p: Parser<'a, ParseState>) : Parser<'a * SourceSpan, ParseState> =
    pipe3 getPosition p trimmedEnd (fun start value endIndex -> value, spanOf start endIndex)

/// The span of `name`, written from `start` on one line.
let private nameSpan (start: Position) (name: Identifier) = spanOf start (int start.Index + name.Length)

/// Notes `name`, written at `start`, as declared by the construct being parsed: see `collectingNames`.
let private noteName (start: Position) (name: Identifier) : Parser<unit, ParseState> =
    updateUserState (fun state -> { state with PendingNames = (name, nameSpan start name) :: state.PendingNames })

/// Runs `p`, and returns with its result the names noted while it ran, in the order they are written. They are
/// no longer pending afterwards.
let private collectingNames (p: Parser<'a, ParseState>) : Parser<'a * (Identifier * SourceSpan) list, ParseState> =
    getUserState >>= fun before ->
        p >>= fun value ->
            getUserState >>= fun after ->
                let noted =
                    after.PendingNames
                    |> List.take (after.PendingNames.Length - before.PendingNames.Length)
                    |> List.rev
                setUserState { after with PendingNames = before.PendingNames } >>% (value, noted)

/// Records that `holder` declares `names` as `declarer`. A name declared twice, by the alternatives of an
/// or-pattern, keeps the span of the first.
let private declare (declarer: Declarer) (holder: NodeId) (names: (Identifier * SourceSpan) list) : Parser<unit, ParseState> =
    if List.isEmpty names then
        preturn ()
    else
        updateUserState (fun state ->
            let key = holder, declarer
            let existing = state.Declared |> Map.tryFind key |> Option.defaultValue []
            { state with Declared = state.Declared |> Map.add key (existing @ names |> List.distinctBy fst) })

/// Runs `p` and makes what it built an expression spanning the text it consumed.
let spanned (p: Parser<ExpressionKind, ParseState>) : Parser<Expression, ParseState> =
    withSpan p |>> fun (kind, span) -> Expression.at span kind

let identifierWithSpan: Parser<Identifier * SourceSpan option, ParseState> =
    withSpan identifierNoWs |>> fun (name, span) -> name, Some span

let qualifiedIdentifierWithSpan: Parser<Identifier * SourceSpan option, ParseState> =
    withSpan qualifiedIdentifier |>> fun (name, span) -> name, Some span

/// One expression, or several separated by commas as a tuple of them.
let private singleOrTuple (items: Parser<Expression list, ParseState>) : Parser<Expression, ParseState> =
    withSpan items |>> fun (exprs, span) ->
        match exprs with
        | [single] -> single
        | multiple -> Expression.at span (TupleExpr multiple)

/// A block of statements. It ends where its last statement's expression does: a statement may have consumed a
/// trailing `-- comment`, which is not part of the block.
let private blockSpanned (statements: Parser<Statement list, ParseState>) : Parser<Expression, ParseState> =
    withSpan statements |>> fun (stmts, span) ->
        let lastExpr =
            match List.tryLast stmts with
            | Some (ExprStatement expr)
            | Some (DefStatement(_, _, _, expr))
            | Some (UseStatement (UseValue expr))
            | Some (UseStatement (UseContextInstance(_, expr))) -> Some expr
            | _ -> None
        let span =
            match lastExpr with
            | Some expr when not (SourceSpan.isNone expr.Span) && expr.Span.End < span.End -> { span with End = expr.Span.End }
            | _ -> span
        Expression.at span (Block stmts)


// Parser for literals
let stringLiteralNoWs: Parser<Literal, ParseState> =
    let doubleQuoted =
        between (pstring "\"") (pstring "\"") (manyChars (noneOf "\""))
        |>> StringLit
    let singleQuoted =
        between (pstring "'") (pstring "'") (manyChars (noneOf "'"))
        |>> StringLit
    attempt doubleQuoted <|> singleQuoted
    <?> "string literal"

let stringLiteral: Parser<Literal, ParseState> = stringLiteralNoWs  // Don't consume ws

let intLiteralNoWs: Parser<Literal, ParseState> =
    // Allow parsing int even if followed by dots (for range operator "..")
    // Just don't allow a SINGLE dot (which would make it a float)
    let notFloat = followedBy (pstring "..") <|> notFollowedBy (pchar '.')
    pint32 .>> notFloat |>> IntLit
    <?> "integer literal"

let intLiteral: Parser<Literal, ParseState> = intLiteralNoWs  // Don't consume ws - let caller decide

let floatLiteralNoWs: Parser<Literal, ParseState> =
    pfloat |>> FloatLit
    <?> "float literal"

let floatLiteral: Parser<Literal, ParseState> = floatLiteralNoWs  // Don't consume ws - let caller decide

let boolLiteralNoWs: Parser<Literal, ParseState> =
    (stringReturn "true" (BoolLit true) <|> stringReturn "false" (BoolLit false))
    <?> "boolean literal"

let boolLiteral: Parser<Literal, ParseState> = boolLiteralNoWs  // Don't consume ws - let caller decide

let literalNoWs: Parser<Literal, ParseState> =
    choice [
        attempt intLiteralNoWs
        attempt floatLiteralNoWs
        boolLiteralNoWs
        stringLiteralNoWs
    ]
    <?> "literal"

let literal: Parser<Literal, ParseState> =
    choice [
        attempt intLiteral
        attempt floatLiteral
        boolLiteral
        stringLiteral
    ]
    <?> "literal"

let modulePath: Parser<string, ParseState> =
    pipe2
        identifierNoWs
        (many (pipe2 (pchar '.' <|> pchar '/') identifierNoWs (fun sep seg -> string sep + seg)))
        (fun head tail -> head + String.concat "" tail)

let importSource: Parser<ImportSource, ParseState> =
    attempt (
        stringLiteralNoWs |>> function
            | StringLit s -> PathImport s
            | _ -> failwith "Expected string literal for import path"
    ) <|> (modulePath |>> ModuleImport)

let importItem: Parser<ImportItem, ParseState> =
    withSpan (importSource .>>. opt (attempt (ws >>. pstring "as" >>. ws >>. identifierNoWs)))
    |>> fun ((source, alias), span) -> { Source = source; Alias = alias; Span = span }

let importItems: Parser<ImportItem list, ParseState> =
    let importParenItems =
        between
            (pstring "(")
            (ws >>. pstring ")")
            (pipe2
                (ws >>. importItem)
                (many (attempt (ws >>. importItem)))
                (fun first rest -> first :: rest))

    let importSingleItems =
        importItem |>> fun item -> [item]

    choice [
        attempt importParenItems
        importSingleItems
    ]

// Forward references for recursive expression parsing
let expression, expressionRef = createParserForwardedToRef<Expression, ParseState>()
let exprWithoutCrossingNewlines, exprWithoutCrossingNewlinesRef = createParserForwardedToRef<Expression, ParseState>()
let inlineExpression, inlineExpressionRef = createParserForwardedToRef<Expression, ParseState>()
let simpleExpression, simpleExpressionRef = createParserForwardedToRef<Expression, ParseState>()
let lambda, lambdaRef = createParserForwardedToRef<Expression, ParseState>()
let functionCall, functionCallRef = createParserForwardedToRef<Expression, ParseState>()
let matchExpr, matchExprRef = createParserForwardedToRef<Expression, ParseState>()
let statement, statementRef = createParserForwardedToRef<Statement, ParseState>()
/// One line of a block as the statements it stands for: a single statement, or the several a
/// destructuring `def (x, y) = ...` desugars into.
let statementItems, statementItemsRef = createParserForwardedToRef<Statement list, ParseState>()
let typeExpr, typeExprRef = createParserForwardedToRef<TypeExpr, ParseState>()
/// The type in a def's annotation: a type expression, or bare parameters `int, int -> int` (see `typeExpr`).
let typeSignature, typeSignatureRef = createParserForwardedToRef<TypeExpr, ParseState>()
/// Types separated by commas, and the result they go to when an arrow follows them.
let typeList, typeListRef = createParserForwardedToRef<TypeExpr list * TypeExpr option, ParseState>()

/// What a `typeList` means: `a, b -> c` is a function of two parameters, since `,` binds tighter than `->`.
let private typeListToType (items: TypeExpr list, resultOpt: TypeExpr option) : TypeExpr =
    match items, resultOpt with
    | [ single ], None -> single
    | _, None -> TypeTuple items
    | [ single ], Some result -> TypeFunc(single, result)
    | _, Some result -> TypeFunc(TypeTuple items, result)

// Pattern parsers
let pattern, patternRef = createParserForwardedToRef<Pattern, ParseState>()

// Interpolated double-quoted strings with {expr} holes
let interpolatedStringExprNoWs: Parser<Expression, ParseState> =
    let textPart = many1Chars (noneOf "\"{") |>> StringText
    let holePart =
        between (pchar '{' .>> ws) (ws >>. pchar '}') expression
        |>> StringExpr
    let partsParser = many (choice [attempt holePart; textPart])
    let toKind parts =
        match parts with
        | [] -> LiteralExpr (StringLit "")
        | _ when parts |> List.exists (function StringExpr _ -> true | _ -> false) ->
            InterpolatedString parts
        | _ ->
            let text =
                parts
                |> List.choose (function StringText value -> Some value | _ -> None)
                |> String.concat ""
            LiteralExpr (StringLit text)
    spanned (between (pstring "\"") (pstring "\"") partsParser |>> toKind)

let wildcardPattern = pstring "_" >>% WildcardPattern
let elsePattern = pstring "else" >>% ElsePattern
let identifierPattern =
    getPosition .>>. identifierNoWs >>= fun (start, name) -> noteName start name >>% IdentifierPattern name

// Parse either a range pattern (1..10), an integer literal pattern (1), 
// or other literal types (float, bool, string)
// NOTE: For ranges, we only allow INTEGER literals to avoid ambiguity with floats like "1."
let literalOrRangePattern =
    choice [
        // Try integer with optional range
        attempt (
            withSpan intLiteralNoWs >>= fun (startLit, startSpan) ->
            opt (pstring ".." >>. ws >>. withSpan intLiteralNoWs) >>= fun endLitOpt ->
            match endLitOpt with
            | Some (endLit, endSpan) ->
                preturn (RangePattern(Expression.at startSpan (LiteralExpr startLit), Expression.at endSpan (LiteralExpr endLit)))
            | None -> preturn (LiteralPattern startLit)
        )
        // Or any other literal type
        literalNoWs |>> LiteralPattern
    ]

// Tuple pattern: (pat1, pat2, ...)
let tuplePattern =
    between
        (pstring "(")
        (pstring ")")
        (sepBy1 (ws >>. pattern) (pstring "," .>> ws))
    |>> fun patterns ->
        match patterns with
        | [single] -> single  // Single pattern in parens is just that pattern, not a tuple
        | multiple -> TuplePattern multiple

// `def (x, y) = value` binds each element of a tuple. It is desugared in the parser into a binding of the
// whole value plus one `def` per element (`def x = __tuple.0`), so the rest of the compiler only ever sees
// plain defs. Only names, `_` and nested tuples may appear in the pattern.
let private destructuringBindings (position: Position) (pattern: Pattern) (value: Expression) : (Identifier * Expression) list option =
    let temp = $"__tuple_{position.Line}_{position.Column}"
    let rec bind (pattern: Pattern) (source: Expression) =
        match pattern with
        | IdentifierPattern name -> Some [ name, source ]
        | WildcardPattern -> Some []
        | TuplePattern elements ->
            elements
            |> List.mapi (fun index element -> bind element (Expression.from value (MemberAccess(source, string index, None))))
            |> List.fold (fun acc next ->
                match acc, next with
                | Some bound, Some more -> Some (bound @ more)
                | _ -> None) (Some [])
        | _ -> None
    bind pattern (Expression.from value (IdentifierExpr temp))
    |> Option.map (fun bindings -> (temp, value) :: bindings)

/// `[export] def (pattern) = value`, as the export flag and the bindings it stands for.
let destructuringDefWith (lineStartCol: int64) (exprParser: Parser<Expression, ParseState>) : Parser<bool * (Identifier * Expression) list, ParseState> =
    pipe3
        (opt (attempt (pstring "export" >>. ws)))
        (getPosition .>>. (pstring "def" >>. ws >>. collectingNames tuplePattern .>> wsNoNl()))
        (defEqualsContinuation lineStartCol >>. pstring "=" >>. wsNoNl() >>. exprParser)
        (fun exportOpt (position, pattern) value -> exportOpt.IsSome, position, pattern, value)
    >>= fun (isExport, position, (pattern, names), value) ->
        match pattern with
        | TuplePattern _ ->
            match destructuringBindings position pattern value with
            | Some bindings ->
                // Each name is declared by the value of the def it becomes.
                let declarations =
                    bindings
                    |> List.map (fun (name, bound) -> declare DefValue bound.Id (names |> List.filter (fun (declared, _) -> declared = name)))
                List.foldBack (>>.) declarations (preturn (isExport, bindings))
            | None -> fail "only names, `_` and nested tuples can be destructured by a def"
        | _ -> fail "a destructuring def needs a tuple pattern"

// Record pattern by position: TypeName(pat1, pat2, ...)
let recordPatternByPosition =
    pipe2
        identifierNoWs
        (between
            (pstring "(")
            (pstring ")")
            (sepBy1 (ws >>. pattern) (pstring "," .>> ws)))
        (fun typeName patterns -> RecordPattern(typeName, patterns))

// Record pattern by member: (member1 = pat1, member2 = pat2, ...)
let recordMemberPattern =
    between
        (pstring "(")
        (pstring ")")
        (sepBy1
            (pipe2
                (ws >>. identifierNoWs .>> ws)
                (pstring "=" >>. ws >>. pattern)
                (fun name pat -> (name, pat)))
            (pstring "," .>> ws))
    |>> RecordMemberPattern

// List pattern: [pat1, pat2, ...] or [pat1, ...rest] or [first, ..., last]
let listPattern =
    let splatPattern = 
        pstring "..." >>. ws >>. opt (getPosition .>>. identifierNoWs >>= fun (start, name) -> noteName start name >>% name)
        |>> Splat
    
    let regularPatternOrSplat =
        (attempt splatPattern) <|> (pattern |>> RegularPattern)
    
    between
        (pstring "[")
        (pstring "]")
        (sepBy (ws >>. regularPatternOrSplat) (pstring "," .>> ws))
    |>> fun elements ->
        // Find where the splat is (if any)
        let splatIndex = elements |> List.tryFindIndex (fun e -> 
            match e with Splat _ -> true | _ -> false)
        
        match splatIndex with
        | None ->
            // No splat - regular list pattern
            let patterns = elements |> List.map (fun e -> 
                match e with 
                | RegularPattern p -> p 
                | _ -> failwith "unexpected")
            ListPattern(patterns, None)
        | Some idx when idx = elements.Length - 1 ->
            // Splat at end: [a, b, ...rest] or [a, b, ...]
            let beforeSplat = elements |> List.take idx |> List.map (fun e ->
                match e with 
                | RegularPattern p -> p 
                | _ -> failwith "unexpected")
            let splatName = 
                match elements.[idx] with
                | Splat (Some id) -> Some (IdentifierPattern id)
                | Splat None -> None
                | _ -> failwith "unexpected"
            ListPattern(beforeSplat, splatName)
        | Some idx ->
            // Splat in middle: [first, ..., last]
            let beforeSplat = elements |> List.take idx |> List.map (fun e ->
                match e with 
                | RegularPattern p -> p 
                | _ -> failwith "unexpected")
            let afterSplat = elements |> List.skip (idx + 1) |> List.map (fun e ->
                match e with 
                | RegularPattern p -> p 
                | _ -> failwith "unexpected")
            ListSplatMiddle(beforeSplat, afterSplat)

// Range pattern: 1..10
// Must use attempt to ensure proper backtracking if ".." is not found
// Guard pattern: `{ < 0 }`, `{ >= limit }` (the pattern-matching page writes a guard as a predicate in the
// shorthand-lambda style). The value may be any expression. The older bare form `> 5` is still accepted.
let guardPattern =
    let guardOp =
        choice [
            pstring ">="
            pstring "<="
            pstring "=="
            pstring "!="
            pstring ">"
            pstring "<"
        ]
    let braced =
        attempt (
            between (pstring "{" .>> ws) (pstring "}") (guardOp .>>. (ws >>. expression .>> ws))
            |>> fun (op, value) -> GuardPattern(op, value))
    let bare =
        attempt (
            guardOp .>>. (ws >>. spanned (literalNoWs |>> LiteralExpr))
            |>> fun (op, value) -> GuardPattern(op, value))
    braced <|> bare

// Tag pattern: #nil or #some(x) or #error data
// Tag pattern: #tagName or #tagName(pattern) or #tagName(p1, p2, ...)
let tagPattern =
    attempt (
        (pstring "#" >>. identifierNoWs) >>= fun tagName ->
        let parenPayload =
            between
                (pstring "(")
                (pstring ")")
                (sepBy1 (ws >>. pattern) (pstring "," .>> ws))
        let inlinePayload =
            skipMany1 (skipAnyOf " \t") >>. pattern |>> fun p -> [p]
        opt (attempt parenPayload <|> attempt inlinePayload) >>= fun patternsOpt ->
        match patternsOpt with
        | None -> preturn (TagPattern(tagName, None))
        | Some [single] -> preturn (TagPattern(tagName, Some single))
        | Some multiple -> preturn (TagPattern(tagName, Some (TuplePattern multiple)))
    )

do patternRef := 
    choice [
        attempt elsePattern         // "else" must come before identifiers
        attempt tagPattern          // "#tag" must come before literals/identifiers
        attempt guardPattern        // "> 5" must come before operators in other contexts
        attempt literalOrRangePattern  // Handles both literals and ranges (1 or 1..10)
        attempt recordPatternByPosition  // Must come before identifierPattern
        attempt recordMemberPattern
        attempt listPattern
        attempt tuplePattern
        attempt wildcardPattern
        identifierPattern           // Last resort - any identifier
    ] .>> ws
    <?> "pattern"

// Type expression parser
do
    let typeIdentifier =
        typeNameIdentifier |>> fun name ->
            if name.Length = 1 && Char.IsLower name.[0] then TypeVar name
            else TypeName name

    let typeParam =
        pipe2
            identifierNoWs
            (opt (attempt (ws >>. pstring "::" >>. ws >>. typeExpr)))
            (fun name typeOpt -> (name, typeOpt))

    let typeMemberField =
        pipe2
            (pstring "type" >>. ws >>. identifierNoWs .>> wsNoNl())
            (opt (attempt (pstring "=" >>. wsNoNl() >>. typeExpr)))
            (fun name typeOpt -> TypeMember(name, typeOpt))

    let typeField =
        pipe5
            (opt (pstring "mut" >>. ws))
            identifierNoWs
            (opt (pstring "?" >>% true))
            (wsNoNl() >>. (pstring ":" <|> pstring "::") >>. ws >>. typeExpr)
            (opt (attempt (ws >>. pstring "=" >>. ws >>. expression)))
            (fun mutOpt name optFlag typeValue defaultOpt ->
                let isMutable = mutOpt.IsSome
                let isOptional = optFlag.IsSome
                TypeField(name, isMutable, isOptional, Some typeValue, defaultOpt))

    let recordFieldSeparator =
        attempt (pstring "," >>. ws >>% ()) <|> (skipMany1 (skipAnyOf " \t\r\n") >>. ws)

    let typeRecord =
        between
            (pstring "(")
            (ws >>. pstring ")")
            (pipe2
                (ws >>. (attempt typeMemberField <|> typeField))
                (many (attempt (recordFieldSeparator >>. (attempt typeMemberField <|> typeField))))
                (fun first rest -> TypeRecord(first :: rest)))

    // Brackets group: `(int, a -> b)` is a function of two parameters, as it is without them, and `()` is unit.
    let typeTupleOrParen =
        between
            (pstring "(")
            (ws >>. pstring ")")
            (ws >>. opt (attempt typeSignature))
        |>> Option.defaultValue TypeUnit

    let typeTag =
        let namedField =
            pipe3
                identifierNoWs
                (opt (pstring "?" >>% true))
                ((wsNoNl() >>. (pstring ":" <|> pstring "::") >>. ws >>. typeExpr))
                (fun name optFlag typeValue ->
                    let isOptional = optFlag.IsSome
                    TypeField(name, false, isOptional, Some typeValue, None))
        // A payload is a record of named fields, or the types in the brackets: `#pair(int, string)` carries a
        // tuple and `#apply(a, b -> c)` a function.
        let payload =
            between
                (pstring "(")
                (ws >>. pstring ")")
                (attempt (sepBy1 (ws >>. namedField) (pstring "," .>> ws) |>> TypeRecord) <|> (ws >>. typeList |>> typeListToType))
        pipe2 (pstring "#" >>. identifierNoWs) (opt payload) (fun tagName payloadOpt -> TypeTag(tagName, payloadOpt))

    // `map(string, int)` has two arguments; `map(string, a -> b)` has one, a function, as `,` binds tighter.
    let typeApply =
        pipe2
            identifierNoWs
            (between (pstring "(") (ws >>. pstring ")") (ws >>. typeList))
            (fun name (args, resultOpt) ->
                match resultOpt with
                | None -> TypeApply(name, args)
                | Some _ -> TypeApply(name, [ typeListToType (args, resultOpt) ]))

    let contextSet =
        between
            (pstring "[")
            (wsNoNl() >>. pstring "]")
            (sepBy1
                (wsNoNl() >>. (attempt typeApply <|> typeIdentifier))
                (wsNoNl() >>. pstring "+" >>. wsNoNl()))
        |>> TypeContext

    let typeConstraint =
        pipe3
            identifierNoWs
            (pstring "::" >>. ws >>. typeExpr)
            (ws >>. pstring "where" >>. ws >>. expression)
            (fun name baseType predicate -> TypeConstraint(name, baseType, predicate))

    let typeAtom =
        choice [
            attempt typeConstraint
            attempt typeTag
            attempt typeApply
            attempt contextSet
            attempt typeRecord
            attempt typeTupleOrParen
            typeIdentifier
        ]

    let typePostfix =
        pipe2 typeAtom (opt (pchar '?')) (fun baseType optFlag ->
            match optFlag with
            | Some _ -> TypeOptional baseType
            | None -> baseType)

    let typeIntersection =
        sepBy1 typePostfix (attempt (ws >>. pstring "&" >>. ws)) |>> fun items ->
            match items with
            | [single] -> single
            | multiple -> TypeIntersection multiple

    let typeUnion =
        opt (pstring "|" >>. ws) >>. sepBy1 typeIntersection (attempt (ws >>. pstring "|" >>. ws)) |>> fun items ->
            match items with
            | [single] -> single
            | multiple -> TypeUnion multiple

    let typeArrow =
        pipe2 typeUnion (opt (attempt (ws >>. pstring "->" >>. ws >>. typeExpr))) (fun left rightOpt ->
            match rightOpt with
            | None -> left
            | Some right ->
                match left with
                | TypeTuple _ -> TypeFunc(left, right)
                | _ -> TypeFunc(left, right))

    let typeWithContext =
        (attempt (
            pipe2
                (contextSet .>> ws)
                (opt (attempt typeArrow))
                (fun ctx typeOpt ->
                    match typeOpt with
                    | Some t -> TypeWithContext(ctx |> (function TypeContext items -> items | _ -> []), t)
                    | None -> ctx)))
        <|> typeArrow

    typeExprRef := typeWithContext

    // `,` binds tighter than `->` wherever types are listed (see the types page), so `int, int -> int` means
    // `(int, int) -> int` in a def's annotation, in brackets, in type arguments and in a tag's payload alike. A
    // function that is one of the items has brackets of its own. A record's fields and a lambda's parameters
    // are separated by commas themselves, so a type there is a `typeExpr`, which takes no bare list.
    typeListRef :=
        sepBy1 typeUnion (attempt (wsInline >>. pstring "," >>. ws))
        .>>. opt (attempt (ws >>. pstring "->" >>. ws >>. typeExpr))

    // A def's annotation: a type list, possibly after the contexts it needs.
    typeSignatureRef :=
        attempt (
            pipe2 (contextSet .>> ws) typeList (fun ctx list ->
                TypeWithContext((match ctx with TypeContext items -> items | _ -> []), typeListToType list)))
        <|> (typeList |>> typeListToType)
    ()

let typedIdentifier: Parser<Identifier * TypeExpr option, ParseState> =
    pipe2
        identifierNoWs
        (opt (attempt (wsInline >>. pstring ":" >>. ws >>. typeExpr)))
        (fun name typeOpt -> (name, typeOpt))

/// A def's name with an optional same-line annotation. Unlike `typedIdentifier` (lambda parameters), the
/// annotation is a `typeSignature`.
let typedDefIdentifier: Parser<Identifier * TypeExpr option, ParseState> =
    pipe2
        identifierNoWs
        (opt (attempt (wsInline >>. pstring ":" >>. ws >>. typeSignature)))
        (fun name typeOpt -> (name, typeOpt))

let typedQualifiedIdentifier: Parser<Identifier * TypeExpr option, ParseState> =
    pipe2
        qualifiedIdentifier
        (opt (attempt (wsInline >>. pstring ":" >>. ws >>. typeSignature)))
        (fun name typeOpt -> (name, typeOpt))

// A def's name and type annotation. The annotation may follow the name on the same line or start an
// indented continuation line (`def f` / `  : T` / `  = ...`), like the `=` does.
let defNameAndType (lineStartCol: int64) (identParser: Parser<Identifier * TypeExpr option, ParseState>) =
    identParser .>> wsNoNl()
    >>= fun (name, typeOpt) ->
        match typeOpt with
        | Some _ -> preturn (name, typeOpt)
        | None ->
            opt (attempt (newlineIndentedContinuation lineStartCol >>. pstring ":" >>. ws >>. typeSignature .>> wsNoNl()))
            |>> fun continuationType -> (name, continuationType)

/// `def name = value`. The def's name is declared by its value.
let parseDefWith (lineStartCol: int64) (identParser: Parser<Identifier * TypeExpr option, ParseState>) (exprParser: Parser<Expression, ParseState>) =
    (pstring "def" >>. ws >>. getPosition .>>. defNameAndType lineStartCol identParser)
    >>= fun (start, (name, typeOpt)) ->
        defEqualsContinuation lineStartCol >>. pstring "=" >>. wsNoNl() >>. exprParser
        >>= fun expr -> declare DefValue expr.Id [ name, nameSpan start name ] >>% (name, typeOpt, expr)

let typeParam: Parser<Identifier * TypeExpr option, ParseState> =
    pipe2
        identifierNoWs
        (opt (attempt (ws >>. pstring "::" >>. ws >>. typeExpr)))
        (fun name typeOpt -> (name, typeOpt))

// Block parser - parses indented statements after a newline
let blockExpr() : Parser<Expression, ParseState> =
    skipMany (skipAnyOf " \t") >>. newline >>.
    many (pchar ' ' <|> pchar '\t') >>= fun indentChars ->
        let indentLevel = indentChars.Length
        if indentLevel > 0 then
            let statementAtIndent =
                attempt (manyMinMaxSatisfy indentLevel indentLevel (fun c -> c = ' ' || c = '\t') >>. statementItems)

            blockSpanned (
                pipe2
                    statementItems
                    (many (attempt (newline >>. statementAtIndent)))
                    (fun first rest -> List.concat (first :: rest)))
        else
            pzero

let matchArmBody: Parser<Expression, ParseState> =
    attempt (blockExpr()) <|> (wsNoNl() >>. exprWithoutCrossingNewlines)

let typeDefCore: Parser<Definition, ParseState> =
    let typeModifiers =
        many (attempt (
            choice [
                pstring "export" >>. ws >>% Export
                pstring "context" >>. ws >>% Context
            ]))

    let typeParams =
        between (pstring "(") (pstring ")") (sepBy1 (wsNoNl() >>. typeParam) (pstring "," .>> wsNoNl()))

    let typeExprWithWhere =
        pipe2 typeExpr (opt (attempt (ws >>. pstring "where" >>. ws >>. expression))) (fun baseType whereOpt ->
            match whereOpt with
            | Some predicate -> TypeWhere(baseType, predicate)
            | None -> baseType)

    let applyPrivate modifiers privateOpt =
        match privateOpt with
        | Some _ -> Private :: modifiers
        | None -> modifiers

    let contextName =
        typeNameIdentifier >>= fun name ->
            if name = "type" then
                fail "expected context name"
            else
                preturn name

    let contextDef =
        pipe5
            (opt (attempt (pstring "export" >>. ws)))
            (pstring "context" >>. ws >>. contextName)
            (opt (attempt (wsNoNl() >>. typeParams)))
            (wsNoNl() >>. pstring "=" >>. ws >>. opt (attempt (pstring "private" >>. ws)))
            typeExprWithWhere
            (fun exportOpt name paramsOpt privateOpt typeValue ->
                let typeParams = defaultArg paramsOpt []
                let baseMods = if exportOpt.IsSome then [Export] else []
                let modifiers = applyPrivate (Context :: baseMods) privateOpt
                TypeDef(name, modifiers, typeParams, typeValue))

    let typeDefWithModifiers =
        pipe5
            typeModifiers
            (pstring "type" >>. ws >>. typeNameIdentifier)
            (opt (attempt (wsNoNl() >>. typeParams)))
            (wsNoNl() >>. pstring "=" >>. ws >>. opt (attempt (pstring "private" >>. ws)))
            typeExprWithWhere
            (fun modifiers name paramsOpt privateOpt typeValue ->
                let typeParams = defaultArg paramsOpt []
                let modifiers = applyPrivate modifiers privateOpt
                TypeDef(name, modifiers, typeParams, typeValue))

    attempt contextDef <|> typeDefWithModifiers
    <?> "type definition"

let valueDefCore: Parser<Definition, ParseState> =
    getPosition >>= fun lineStart ->
        pipe2
            (opt (attempt (pstring "export" >>. ws)))
            (parseDefWith lineStart.Column typedDefIdentifier (wsInline >>. singleOrTuple (sepBy1 expression (pstring "," .>> ws))))
            (fun exportOpt (name, typeOpt, expr) -> ValueDef(exportOpt.IsSome, name, typeOpt, expr))
    <?> "value definition"

// A match arm: `| patterns -> body`. An arm may list several alternatives (an or-pattern), written
// `| p1 | p2 -> body` on one line or with each `|` on its own line; every arm ends in `->`, so a `|` that is
// not preceded by `->` continues the current arm rather than starting a new one. The arm is desugared into one
// arm per alternative, all sharing the body, so the rest of the compiler only ever sees plain arms.
let matchArmAlternatives : Parser<(Pattern list * Expression) list, ParseState> =
    let alternative = sepBy1 pattern (pstring "," .>> ws)
    pipe2
        (pstring "|" >>. ws >>. collectingNames (sepBy1 alternative (attempt (ws >>. pstring "|" >>. ws))))
        (pstring "->" >>. matchArmBody)
        (fun (alternatives, names) body -> alternatives, names, body)
    >>= fun (alternatives, names, body) ->
        declare ArmBody body.Id names >>% (alternatives |> List.map (fun patterns -> patterns, body))
    <?> "match arm"

/// One or more arms, each possibly an or-pattern, flattened into plain arms.
let matchArms : Parser<(Pattern list * Expression) list, ParseState> =
    many1 (attempt (ws >>. matchArmAlternatives)) |>> List.concat

// `for iterable | arms` runs the arms' bodies for each element (the element is matched against the arms, as
// the loops page shows with `| { < 5 } -> ... | _ -> ...`), and `for iterable -> body` ignores the element.
// Both are represented as a call to the built-in "__for" with the iterable and a one-parameter lambda,
// which the type checker and backend handle as a loop.
let forExpr : Parser<Expression, ParseState> =
    let keywordEnd = notFollowedBy (satisfy (fun c -> Char.IsLetterOrDigit c || c = '_'))
    let elementName = "__for_element"
    // The whitespace before the first arm is skipped outside the span, which starts at its `|`.
    let withArms =
        ws >>. withSpan matchArms |>> fun (arms, span) ->
            let element = Expression.at span (IdentifierExpr elementName)
            Expression.at span (Lambda([ (elementName, None) ], Expression.at span (Match([ element ], arms))))
    let withoutElement =
        ws >>. withSpan (pstring "->" >>. matchArmBody) |>> fun (body, span) ->
            Expression.at span (Lambda([ (elementName, None) ], body))
    spanned (
        pipe2
            (pstring "for" >>. keywordEnd >>. ws >>. expression)
            (attempt withArms <|> withoutElement)
            (fun iterable body -> FunctionCall("__for", None, [ iterable; body ])))
    <?> "for loop"

/// The column (counted from 1, as FParsec does) of the first character on the current line that is not a space
/// or tab: how far the line the parser is on is indented.
let private currentLineIndent: Parser<int64, ParseState> =
    fun stream ->
        let isOnLine (offset: int) =
            let c = stream.Peek(offset)
            c <> '\n' && c <> CharStream.EndOfStreamChar
        let isIndent (offset: int) =
            let c = stream.Peek(offset)
            c = ' ' || c = '\t'
        let mutable lineStart = 0
        while isOnLine (lineStart - 1) do
            lineStart <- lineStart - 1
        let mutable indent = 0
        while isIndent (lineStart + indent) do
            indent <- indent + 1
        Reply(int64 indent + 1L)

// Match expression parser. The scrutinees are any expressions written on the `match` line (`match a, b`), or
// a block on the lines after it. The block must be indented more than both the line `match` is on and the
// arms, so that it cannot be mistaken for either:
//
//   match
//       def trimmed = s \trim
//       trimmed \split(",")
//     | [a, b] -> ...
do
    matchExprRef :=
        let keywordEnd = notFollowedBy (satisfy (fun c -> Char.IsLetterOrDigit c || c = '_'))
        let sameLine = wsNoNl() >>. sepBy1 expression (pstring "," .>> ws) |>> fun scrutinees -> scrutinees, None
        let indentedBlock matchIndent =
            lookAhead (wsNoNl() >>. newline >>. wsNoNl() >>. getPosition) >>= fun blockStart ->
                if blockStart.Column <= matchIndent then
                    fail "the scrutinee of a match must be on the same line or indented more than the match"
                else
                    blockExpr () |>> fun block ->
                        // A block of one expression is just that expression.
                        let scrutinee =
                            match block.Kind with
                            | Block [ ExprStatement expr ] -> expr
                            | _ -> block
                        [ scrutinee ], Some blockStart.Column
        let armsAfter blockColumn =
            match blockColumn with
            | None -> matchArms
            | Some column ->
                lookAhead (ws >>. getPosition) >>= fun firstArm ->
                    if firstArm.Column >= column then
                        fail "the arms of a match must be indented less than the block before them"
                    else
                        matchArms
        currentLineIndent >>= fun matchIndent ->
            spanned (
                pstring "match" >>. keywordEnd
                >>. (attempt (indentedBlock matchIndent) <|> sameLine)
                >>= fun (scrutinees, blockColumn) ->
                    armsAfter blockColumn |>> fun arms -> Match(scrutinees, arms))
            .>> skipMany (skipAnyOf " \t\r\n")
        <?> "match expression"

/// An operand of an operator, with the span of all the text it was written as. That is wider than the expression's
/// own span when the operand is in parentheses (`(1 + 2)`) or a lone spread (`[...xs]`), neither of which is a
/// node of its own. An operator spans from its first operand's text to its last one's, so it takes in the
/// delimiters, while an identifier in parentheses is still reported at the identifier.
type Operand = Expression * SourceSpan

// Operator precedence parser
let opp = new OperatorPrecedenceParser<Operand, unit, ParseState>()

let private binary op ((left, leftText): Operand) ((right, rightText): Operand) : Operand =
    let span = SourceSpan.between leftText rightText
    Expression.at span (BinaryOp(op, left, right)), span

// Record field parser: either "name = expr" or just "expr"
let recordField =
    (attempt (
        pipe2
            (identifierNoWs .>> ws .>> pstring "=" .>> ws)
            expression
            (fun name expr -> NamedField(name, expr))
    ) <|> (expression |>> PositionalField))
    <?> "record field"

// The fields of a record or tuple, or a call's arguments, separated by commas or written one per line:
//
//   R(
//     a = 1
//     b = 2
//   )
//
// When the first field starts its line, a field that starts a later line at the same indentation follows the
// field before it without a comma. A field may itself run over several lines (a lambda, a `match`), as long as
// its continuation lines are indented more than the fields.
let recordFields =
    ws >>. getPosition .>>. currentLineIndent >>= fun (first, firstLineIndent) ->
        let comma = pstring "," .>> ws
        let nextLine =
            if first.Column <> firstLineIndent then pzero
            else
                ws >>. getPosition .>>. currentLineIndent >>= fun (pos, lineIndent) ->
                    if pos.Column = first.Column && lineIndent = pos.Column then preturn () else pzero
        pipe2
            recordField
            (many ((comma >>. recordField) <|> attempt (nextLine >>. recordField)))
            (fun head tail -> head :: tail)

// Unit literal: ()
let unitExpr =
    spanned (between (pstring "(") (ws >>. pstring ")") (preturn ()) |>> fun _ -> UnitExpr)

// Tuple/Record or parenthesized expression parser
// Handles: (x), (x, y), (x = 1), (x = 1, y = 2), (x = 1, 2), etc.
let tupleOrParenExpr =
    (withSpan (
        between
            (pstring "(")
            (ws >>. pstring ")")
            recordFields)
    |>> fun (fields, span) ->
        match fields with
        | [PositionalField single] -> single  // Single expression in parens: (x) is just x
        | _ ->
            // Check if all fields are positional (for backward compat)
            let allPositional = fields |> List.forall (function PositionalField _ -> true | _ -> false)
            if allPositional then
                // Pure tuple: extract expressions
                let exprs = fields |> List.map (function PositionalField e -> e | _ -> failwith "unexpected")
                Expression.at span (TupleExpr exprs)
            else
                // Has named fields: use RecordExpr
                Expression.at span (RecordExpr fields))
    <?> "tuple or record"

// List expression parser. `...expr` splices a list in place: `[a, ...xs, b]` is desugared to
// `[a] ++ xs ++ [b]`, so the rest of the compiler only ever sees list literals and concatenation.
let listExpr =
    let item =
        (pstring "..." >>. ws >>. expression |>> Choice2Of2)
        <|> (expression |>> Choice1Of2)
    withSpan (
        between
            (pstring "[")
            (pstring "]")
            (sepBy (ws >>. item) (pstring "," .>> ws)))
    |>> fun (items, span) ->
        // Each segment is a run of elements or a spread list.
        let segments =
            items
            |> List.fold (fun segments item ->
                match item, segments with
                | Choice1Of2 element, Choice1Of2 elements :: rest -> Choice1Of2 (elements @ [ element ]) :: rest
                | Choice1Of2 element, _ -> Choice1Of2 [ element ] :: segments
                | Choice2Of2 spread, _ -> Choice2Of2 spread :: segments) []
            |> List.rev
            |> List.map (function
                | Choice1Of2 elements -> Expression.at span (ListExpr elements)
                | Choice2Of2 spread -> spread)
        match segments with
        | [] -> Expression.at span (ListExpr [])
        | _ -> segments |> List.reduceBack (fun left right -> Expression.at span (BinaryOp("++", left, right)))

// Tag expression parser: #tagName or #tagName(expr)
// The payload can be a tuple-like expression with comma-separated values
let tagExpr =
    let payload =
        between
            (pstring "(")
            (pstring ")")
            (ws >>. singleOrTuple (sepBy1 expression (pstring "," .>> ws)))  // Multiple values as a tuple
    spanned (
        pstring "#" >>. identifierNoWs >>= fun tagName ->
        opt payload |>> fun payloadOpt -> TagExpr(tagName, payloadOpt))

// If-expression parser: if condition then trueExpr else falseExpr
// `if cond then a else b`, or if-matching: `if value is pattern then body [else other]` (see the
// pattern-matching page), which is desugared to `match value | pattern -> body | _ -> other`, with `()` as
// the other arm when there is no `else`. `is` makes it plain that the pattern binds its names rather than
// comparing against existing ones.
// Either branch may be an indented block on the following lines, and the `else` may be left out when the
// `then` branch is unit (`if i > 0 then` followed by `println(...)` on the functions page); a missing
// `else` is `()`.
// The multi-branch form `if | cond1 -> a | value is pattern -> b | else -> c` tests each arm in order and is
// desugared to nested ifs and if-matches, so `else` (or `()` when it is left out) ends up in the innermost
// one. Nothing may follow the `else` arm, so a nested multi-branch if with an `else` cannot swallow the arms
// of an enclosing match.
let ifExpr =
    let keywordEnd = notFollowedBy (satisfy (fun c -> Char.IsLetterOrDigit c || c = '_'))
    let branch = attempt (blockExpr()) <|> (ws >>. expression)
    let patternBinding =
        attempt (expression .>> ws .>> pstring "is" .>> keywordEnd .>> ws) .>>. pattern
        |>> fun (value, pat) -> Choice2Of2 (pat, value)
    let condition = expression |>> Choice1Of2
    let head = patternBinding <|> condition
    // A head and the branch it guards. The names the head's pattern binds are declared by the branch, which is
    // the body of the match arm the head becomes.
    let headThen (branchParser: Parser<Expression, ParseState>) =
        collectingNames head .>>. branchParser
        >>= fun ((head, names), branch) -> declare ArmBody branch.Id names >>% (head, branch)
    // A left-out `else` is `()` made up at the `then` branch.
    let desugar span head (thenExpr: Expression) (elseOpt: Expression option) =
        let elseExpr = elseOpt |> Option.defaultWith (fun () -> Expression.from thenExpr UnitExpr)
        match head with
        | Choice1Of2 cond -> Expression.at span (IfExpr(cond, thenExpr, elseExpr))
        | Choice2Of2 (pat, value) -> Expression.at span (Match([ value ], [ [ pat ], thenExpr; [ WildcardPattern ], elseExpr ]))
    let elseKeyword = pstring "else" >>. keywordEnd
    let conditionArm =
        pstring "|" >>. ws >>. notFollowedBy elseKeyword >>. headThen (ws >>. pstring "->" >>. matchArmBody)
    let elseArm = pstring "|" >>. ws >>. elseKeyword >>. ws >>. pstring "->" >>. matchArmBody
    // Each form builds the if given the span of the whole `if ...`. An if nested in place of a later arm spans
    // from that arm's `|` to the end of the last arm.
    let multiBranch =
        pipe2
            (many1 (attempt (ws >>. withSpan conditionArm)))
            (opt (attempt (ws >>. withSpan elseArm)))
            (fun arms elseOpt span ->
                let lastText = match elseOpt with Some (_, text) -> text | None -> snd (List.last arms)
                let outer, inner = List.head arms, List.tail arms
                let rest =
                    List.foldBack (fun ((head, body), armText) rest ->
                        Some (desugar (SourceSpan.between armText lastText) head body rest)) inner (Option.map fst elseOpt)
                let (head, body), _ = outer
                desugar span head body rest)
    let singleBranch =
        pipe2
            (headThen (ws >>. pstring "then" >>. keywordEnd >>. branch))
            (opt (attempt (ws >>. elseKeyword >>. branch)))
            (fun (head, thenExpr) elseOpt span -> desugar span head thenExpr elseOpt)
    withSpan (pstring "if" >>. keywordEnd >>. ws >>. (multiBranch <|> singleBranch))
    |>> fun (build, span) -> build span
    <?> "if expression"

let workflowBindExpr =
    attempt (
        spanned (
            pipe2
                (identifierNoWs .>> pchar '!' .>> ws)
                expression
                (fun name expr -> WorkflowBindExpr(name, expr))))
    <?> "workflow bind"

let workflowReturnExpr =
    let isIdentChar c = isLetter c || isDigit c || c = '_'
    attempt (
        spanned (
            pstring "return"
            .>> notFollowedBy (satisfy isIdentChar)
            >>. ws
            >>. expression
            |>> WorkflowReturnExpr))
    <?> "workflow return"

// `try x` applies the workflow keyword `try` to `x`, the same as the older `try! x`; what it does is up to the
// workflow (see the workflows page of the docs). `try` is the only keyword written this way so far. Its value is
// on the same line, and `try` is reserved: `try` alone is not a name.
let workflowKeywordExpr =
    let keywordEnd = notFollowedBy (satisfy (fun c -> Char.IsLetterOrDigit c || c = '_'))
    spanned (
        attempt (pstring "try" .>> keywordEnd) >>. wsInline >>. expression
        |>> fun value -> WorkflowBindExpr("try", value))
    <?> "workflow keyword"

// Use binding parser: `use expr` or `use C(T) = expr`
let useBinding =
    // First try context instance: C(T) = expr or C = expr
    let contextInstance =
        pipe2
            (attempt (identifierNoWs .>>. opt (between (pstring "(") (pstring ")") (sepBy1 (ws >>. typeExpr) (pstring "," .>> ws)))))
            (ws >>. pstring "=" >>. ws >>. expression)
            (fun (name, typeArgsOpt) expr ->
                let contextType = 
                    match typeArgsOpt with
                    | Some args -> TypeApply(name, args)
                    | None -> TypeName name
                UseContextInstance(contextType, expr))
    attempt contextInstance <|> (expression |>> UseValue)

// Use-in expression: use X in expr
let useInExpr =
    spanned (
        pipe2
            (pstring "use" >>. ws >>. useBinding .>> ws .>> pstring "in" .>> ws)
            expression
            (fun binding body -> UseIn(binding, body)))
    <?> "use-in expression"

// Primary expression (literals, identifiers, function calls, lambdas, parenthesized expressions)
// Version without trailing whitespace consumption (for use in block contexts)

// Context member access: C(T).field or C(T).field(args) - qualified access to context functions
// ONLY matches when type parameters are present (parens required). Context names are capitalised, which is
// what tells `ToString(a).toString(x)` apart from a member of a call's result such as `openFile(path).contents`.
let contextMemberAccess =
    let contextName =
        identifierNoWs >>= fun name ->
            if Char.IsUpper name.[0] then preturn name else fail "context name (capitalised)"
    // Parse a context type application with required type args: C(T)
    let contextTypeApply =
        pipe2
            contextName
            (between (pstring "(") (pstring ")") (sepBy1 (ws >>. typeExpr) (pstring "," .>> ws)))
            (fun name args -> TypeApply(name, args))
    // Parse args as tuple-like: (expr, expr, ...)
    let callArgs =
        between (pstring "(") (pstring ")") 
            (opt (ws >>. (sepBy1 (ws >>. expression) (pstring "," .>> ws)) .>> ws))
    spanned (
        pipe3
            (contextTypeApply .>> pstring ".")
            identifierNoWs
            (opt callArgs)
            (fun ctxType field argsOpt ->
                match argsOpt with
                | Some (Some args) -> ContextMemberCall(ctxType, field, args)
                | Some None -> ContextMemberCall(ctxType, field, [])  // empty parens like f()
                | None -> ContextMemberAccess(ctxType, field)))

// A name applied to a value without parentheses, as in `yield 1`, is how a workflow keyword is used (see the
// workflows page of the docs). Only `try` is parsed that way so far, and without this any other name parses as
// an identifier and the failure surfaces far from here. So it fails at the name, and notes the failure in the
// state, which `parseModuleFileWithNames` reports in place of FParsec's error when the parse fails. Workflow
// blocks are lambdas, so only a name inside a lambda is taken for a keyword: outside one, a line such as
// `pattern string -> ...` is some other mistake.
let private workflowKeywordApplication =
    let reservedWords =
        set [ "and"; "as"; "context"; "def"; "else"; "export"; "for"; "if"; "import"; "in"; "is"; "match"; "module"
              "mut"; "not"; "or"; "private"; "rec"; "return"; "then"; "try"; "type"; "use"; "where"; "with" ]
    let operandStart c = isLetter c || isDigit c || c = '_' || c = '"' || c = '#' || c = '(' || c = '['
    let keyword =
        identifierNoWs >>= fun name ->
            if Char.IsLower name.[0] && not (reservedWords.Contains name) then preturn name else pzero
    // What follows `x` in `x then` or `x and y` is the rest of an enclosing construct, not a value.
    let operand = followedBy (satisfy operandStart) >>. notFollowedBy (identifierNoWs >>= fun word ->
        if reservedWords.Contains word then preturn () else pzero)
    userStateSatisfies (fun state -> state.LambdaDepth > 0)
    >>. getPosition .>>. lookAhead (keyword .>> skipMany1 (anyOf " \t") .>> operand)
    .>>. getUserState
    >>= fun ((position, name), state) ->
        let message =
            $"`{name}` is applied to a value without parentheses, which is how a workflow keyword is used. The "
            + $"only workflow keywords so far are `try` and `return`. To call a function, write `{name}(...)`."
        match state.WorkflowKeyword.Value with
        | Some (furthest, _) when furthest.Index >= position.Index -> ()
        | _ -> state.WorkflowKeyword.Value <- Some (position, message)
        failFatally message

let primaryExprNoWs =
    choice [
        attempt interpolatedStringExprNoWs
        attempt workflowReturnExpr
        attempt workflowBindExpr
        workflowKeywordExpr
        attempt useInExpr
        attempt unitExpr
        attempt ifExpr  // if-then-else must be early to avoid partial matches
        attempt forExpr
        attempt matchExpr
        attempt lambda
        attempt contextMemberAccess  // C(T).field - must come before functionCall
        attempt functionCall
        attempt tagExpr  // #tag or #tag(expr)
        spanned (literalNoWs |>> LiteralExpr)
        listExpr
        tupleOrParenExpr
        workflowKeywordApplication
        spanned (identifierNoWs |>> IdentifierExpr)
    ]

do simpleExpressionRef := primaryExprNoWs

// Member access: parse base expression, then chain .field.field.field... or .0.1.2...
let memberAccessChain =
    let tupleIndex = many1Satisfy isDigit
    let fieldOrIndex = identifierNoWs <|> tupleIndex
    // `record with field = value, ...` copies a record with some fields replaced. The parser represents it
    // as a call to "__with" with the record and a record of the updated fields, which the type checker and
    // backend handle as a built-in (the same way `not` is a call to "not"). The record of updates spans from the
    // first update to the last.
    let withClause =
        let keywordEnd = notFollowedBy (satisfy (fun c -> Char.IsLetterOrDigit c || c = '_'))
        let update =
            (identifierNoWs .>> wsInline .>> pstring "=" .>> ws) .>>. expression
            |>> fun (field, value) -> NamedField(field, value)
        attempt (wsInline >>. pstring "with" >>. keywordEnd >>. ws >>. withSpan (sepBy1 update (pstring "," .>> ws)))
    // `..` is the range operator, not a member access.
    let dotField = attempt (pstring "." >>. notFollowedBy (pchar '.') >>. withSpan fieldOrIndex)
    // Each access spans from the start of the base's text, so `(a + b).x` takes in the opening parenthesis.
    withSpan primaryExprNoWs >>= fun (baseExpr, baseText) ->
        many dotField >>= fun fields ->
            let accessed, accessedText =
                fields
                |> List.fold (fun ((expr: Expression), text) (field, fieldSpan) ->
                    let span = SourceSpan.between text fieldSpan
                    Expression.at span (MemberAccess(expr, field, Some fieldSpan)), span) (baseExpr, baseText)
            opt withClause |>> function
                | Some (updates, updatesSpan) ->
                    let updated = Expression.at updatesSpan (RecordExpr updates)
                    Expression.at (SourceSpan.between accessedText updatesSpan) (FunctionCall("__with", None, [ accessed; updated ]))
                | None -> accessed

let primaryExpr = memberAccessChain .>> wsInline  // Consume trailing inline ws for operator parsing

// Boolean operations are the keywords `not`, `and` and `or` (lowest precedence; see the operators page of
// the docs). A keyword operator must not swallow the start of an identifier such as `order` or `android`.
// The symbolic `&&` and `||` predate the keywords and are kept as aliases that build the same tree.
let private keywordEnd = notFollowedBy (satisfy (fun c -> Char.IsLetterOrDigit c || c = '_'))
let private keywordOperatorEnd = keywordEnd >>. ws

// `not` binds tighter than every infix operator, so `not a == b` is `(not a) == b`, as `!` does elsewhere. That
// makes it part of the operand it applies to, so it is parsed here rather than by the operator precedence parser,
// and spans from the keyword.
let private operand : Parser<Operand, ParseState> =
    let notKeyword = attempt (withSpan (pstring "not" .>> keywordEnd)) .>> ws
    pipe2 (many notKeyword) (withSpan primaryExpr) (fun nots term ->
        List.foldBack (fun (_, notSpan) ((inner: Expression), innerText) ->
            let span = SourceSpan.between notSpan innerText
            Expression.at span (FunctionCall("not", None, [ inner ])), span) nots term)

opp.TermParser <- operand

// Define operators with precedence
// Multiplicative operators (higher precedence)
opp.AddOperator(InfixOperator("*", ws, 7, Associativity.Left, binary "*"))
opp.AddOperator(InfixOperator("/", ws, 7, Associativity.Left, binary "/"))
opp.AddOperator(InfixOperator("%", ws, 7, Associativity.Left, binary "%"))

// Additive operators
opp.AddOperator(InfixOperator("+", ws, 6, Associativity.Left, binary "+"))
// `-` must not take the start of a `->` (as in `for 1..2 -> ...`) or of a `--` comment.
let private minusEnd = notFollowedBy (anyOf ">-")
opp.AddOperator(InfixOperator("-", minusEnd >>. ws, 6, Associativity.Left, binary "-"))

// Concatenation (strings, lists): binds looser than arithmetic, tighter than comparison
opp.AddOperator(InfixOperator("++", ws, 5, Associativity.Right, binary "++"))

// Comparison operators (lower precedence)
opp.AddOperator(InfixOperator("<", ws, 4, Associativity.Left, binary "<"))
opp.AddOperator(InfixOperator(">", ws, 4, Associativity.Left, binary ">"))
opp.AddOperator(InfixOperator("<=", ws, 4, Associativity.Left, binary "<="))
opp.AddOperator(InfixOperator(">=", ws, 4, Associativity.Left, binary ">="))

// Equality operators
// A bounded range `a..b` (see the ranges page) is represented as a call to the built-in "__range". It binds
// looser than arithmetic, so `n - 1..n + 1` is `(n - 1)..(n + 1)`. `...` is list spread, not a range.
opp.AddOperator(InfixOperator("..", notFollowedBy (pchar '.') >>. ws, 4, Associativity.Left, fun (x, xText) (y, yText) ->
    let span = SourceSpan.between xText yText
    Expression.at span (FunctionCall("__range", None, [ x; y ])), span))
opp.AddOperator(InfixOperator("==", ws, 3, Associativity.Left, binary "=="))
opp.AddOperator(InfixOperator("!=", ws, 3, Associativity.Left, binary "!="))

opp.AddOperator(InfixOperator("and", keywordOperatorEnd, 2, Associativity.Left, binary "and"))
opp.AddOperator(InfixOperator("or", keywordOperatorEnd, 1, Associativity.Left, binary "or"))
opp.AddOperator(InfixOperator("&&", ws, 2, Associativity.Left, binary "and"))
opp.AddOperator(InfixOperator("||", ws, 1, Associativity.Left, binary "or"))

// Note: Comma is NOT an operator in the OPP
// Commas are handled explicitly in tuple/record literals and function calls
// For bare tuple expressions (def x = 1, 2), we handle it in defStatement

// Pipe parser - handles expr \func or expr \func(args)
let pipeTarget =
    pipe3
        qualifiedIdentifierWithSpan
        (opt (between (pstring "(") (pstring ")") (ws >>. singleOrTuple (sepBy1 expression (pstring "," .>> ws)))))
        (opt (attempt (wsInline >>. lambda)))
        (fun (funcName, funcRange) argOpt trailingLambdaOpt ->
            let baseArgs = match argOpt with Some arg -> [arg] | None -> []
            let args =
                match trailingLambdaOpt with
                | Some lambdaExpr -> baseArgs @ [lambdaExpr]
                | None -> baseArgs
            (funcName, funcRange, args)
        )

// Inline operator precedence parser (no newlines in operator whitespace)
let oppInline = new OperatorPrecedenceParser<Operand, unit, ParseState>()
oppInline.TermParser <- withSpan primaryExpr

oppInline.AddOperator(InfixOperator("*", wsInline, 7, Associativity.Left, binary "*"))
oppInline.AddOperator(InfixOperator("/", wsInline, 7, Associativity.Left, binary "/"))

oppInline.AddOperator(InfixOperator("+", wsInline, 6, Associativity.Left, binary "+"))
oppInline.AddOperator(InfixOperator("-", minusEnd >>. wsInline, 6, Associativity.Left, binary "-"))
oppInline.AddOperator(InfixOperator("++", wsInline, 5, Associativity.Right, binary "++"))

oppInline.AddOperator(InfixOperator("<", wsInline, 4, Associativity.Left, binary "<"))
oppInline.AddOperator(InfixOperator(">", wsInline, 4, Associativity.Left, binary ">"))
oppInline.AddOperator(InfixOperator("<=", wsInline, 4, Associativity.Left, binary "<="))
oppInline.AddOperator(InfixOperator(">=", wsInline, 4, Associativity.Left, binary ">="))

oppInline.AddOperator(InfixOperator("==", wsInline, 3, Associativity.Left, binary "=="))
oppInline.AddOperator(InfixOperator("!=", wsInline, 3, Associativity.Left, binary "!="))

/// `expr \f \g(a)`: each pipe spans from the start of `expr`'s text to the end of its own target.
let private applyPipes (value: Operand) pipes =
    pipes
    |> List.fold (fun ((acc: Expression), accText) ((funcName, nameSpan, args), targetSpan) ->
        let span = SourceSpan.between accText targetSpan
        Expression.at span (Pipe(acc, funcName, nameSpan, args)), span) value
    |> fst

let pipeExpr =
    let pipeLeadingWs =
        skipMany (skipAnyOf " \t") >>.
        skipMany (attempt (newline >>. skipMany (skipAnyOf " \t")))
    let pipeOp = pipeLeadingWs >>. pstring "\\" >>. wsInline >>. withSpan pipeTarget
    pipe2
        opp.ExpressionParser
        (many (attempt (pipeOp .>> wsInline)))
        applyPipes

let inlinePipeExpr =
    let pipeOp = pstring "\\" >>. wsInline >>. withSpan pipeTarget
    pipe2
        oppInline.ExpressionParser
        (many (attempt (pipeOp .>> wsInline)))
        applyPipes

do inlineExpressionRef := inlinePipeExpr

// Expression parser that stops at newlines (for use in blocks)
do exprWithoutCrossingNewlinesRef :=
    attempt matchExpr <|> inlinePipeExpr

// Wire up the expression parser with piping support
// Prefer match expressions first to avoid parsing "match" as an identifier.
do expressionRef := (attempt matchExpr <|> pipeExpr)

// Wire up the lambda parser
do 
    let parameter =
        getPosition .>>. typedIdentifier >>= fun (start, (name, typeOpt)) -> noteName start name >>% (name, typeOpt)
    let paramList = sepBy parameter (pstring "," .>> ws)
    let arrow = wsInline >>. pstring "->" .>> wsInline
    
    // Lambda body: allow multi-line statement blocks or a single expression
    let blockBody =
        let matchStatement = wsInline >>. matchExpr |>> ExprStatement |>> List.singleton
        let statementLine =
            attempt (
                ws >>.
                (attempt matchStatement <|> statementItems)
            )
        let multiLine =
            attempt (
                skipMany (skipAnyOf " \t\r") >>. newline >>.
                ws >>.
                blockSpanned (many1 statementLine |>> List.concat) .>> ws
            )

        let singleLine =
            skipMany (skipAnyOf " \t\r\n") >>.
            expression .>>
            skipMany (skipAnyOf " \t\r\n")

        attempt multiLine <|> singleLine
    
    // Each form builds the lambda's kind given the span of the whole `{ ... }`, which is also where the
    // parameters and expressions a shorthand stands for are made up.
    let lambdaWithParams = pipe2 (paramList .>> arrow .>> skipMany (skipAnyOf " \t")) blockBody (fun parameters body _ -> Lambda(parameters, body))
    let lambdaNoParams = blockBody |>> (fun body _ -> Lambda([], body))
    // `{ }`, possibly spanning lines, is a lambda that does nothing
    let emptyLambda = skipMany (skipAnyOf " \t\r\n") >>. followedBy (pstring "}") >>% fun span -> Lambda([], Expression.at span UnitExpr)

    // Shorthand lambda: { * } means { x, y -> x * y }
    let shorthandBinaryOp =
        // "++" before "+" so the longer operator wins
        let operators = ["*"; "/"; "++"; "+"; "-"; "<"; ">"; "<="; ">="; "=="; "!="]
        choice (operators |> List.map (fun op ->
            pstring op >>. ws >>. notFollowedBy (skipAnyOf "0123456789.\"'([{") >>% fun span ->
                let made kind = Expression.at span kind
                Lambda([("x", None); ("y", None)], made (BinaryOp(op, made (IdentifierExpr "x"), made (IdentifierExpr "y"))))
        ))
    
    // Shorthand lambda: { * 2 } means { x -> x * 2 }
    // Also supports comparison operators: { > 10 } means { x -> x > 10 }
    let shorthandUnaryOp =
        let operators = [
            ("*", "*"); ("/", "/"); ("++", "++"); ("+", "+"); ("-", "-");
            ("<=", "<="); (">=", ">="); ("<", "<"); (">", ">");
            ("==", "=="); ("!=", "!=")
        ]
        choice (operators |> List.map (fun (op, opStr) ->
            pstring op >>. ws >>. primaryExpr |>> (fun rightExpr span ->
                let made kind = Expression.at span kind
                Lambda([("x", None)], made (BinaryOp(opStr, made (IdentifierExpr "x"), rightExpr)))
            )
        ))

    // Shorthand lambda: { .name } means { x -> x.name }
    let shorthandPropertyAccess =
        pstring "." >>. identifier |>> (fun propName span ->
            let made kind = Expression.at span kind
            Lambda([("x", None)], made (MemberAccess(made (IdentifierExpr "x"), propName, None)))
        )

    // Shorthand match lambda: { | pat -> expr ... }
    let shorthandMatchLambda =
        matchArms |>> (fun arms span ->
            let made kind = Expression.at span kind
            let maxArity =
                arms
                |> List.map (fun (patterns, _) -> patterns.Length)
                |> List.max
            let argNames =
                [1 .. maxArity]
                |> List.map (fun index -> ($"arg{index}", None))
            let scrutinees =
                argNames
                |> List.map (fun (name, _) -> made (IdentifierExpr name))
            Lambda(argNames, made (Match(scrutinees, arms))))

    // The names noted while a lambda is parsed are its parameters: whatever is nested in it takes its own.
    lambdaRef :=
        collectingNames (
            withSpan (
                between
                    (pstring "{" .>> updateUserState (fun state -> { state with LambdaDepth = state.LambdaDepth + 1 }))
                    (pstring "}" .>> updateUserState (fun state -> { state with LambdaDepth = state.LambdaDepth - 1 })) (
                    skipMany (skipAnyOf " \t") >>. choice [
                        attempt emptyLambda
                        attempt shorthandUnaryOp
                        attempt shorthandBinaryOp
                        attempt shorthandPropertyAccess
                        attempt shorthandMatchLambda
                        attempt lambdaWithParams
                        lambdaNoParams
                    ] .>> skipMany (skipAnyOf " \t\r\n")
                )))
        >>= (fun ((build, span), parameters) ->
            let lambda = Expression.at span (build span)
            declare LambdaParameters lambda.Id parameters >>% lambda)
        <?> "lambda expression"

// Wire up the function call parser
do 
    // Trailing lambda: function can be called with a lambda after parentheses
    // Examples: f(a, b) { x -> x + 1 } or f { x -> x + 1 } (no parens needed if only lambda arg)
    // Note: f(a, b) creates single tuple argument: FunctionCall("f", None, [TupleExpr[a; b]])

    let fieldsToExpr (fields, span) =
        match fields with
        | [PositionalField single] -> single
        | _ ->
            let allPositional = fields |> List.forall (function PositionalField _ -> true | _ -> false)
            if allPositional then
                let exprs = fields |> List.map (function PositionalField e -> e | _ -> failwith "unexpected")
                Expression.at span (TupleExpr exprs)
            else
                Expression.at span (RecordExpr fields)

    let callWithParensAndTrailing =
        spanned (
            pipe3
                qualifiedIdentifierWithSpan
                (between (pstring "(") (pstring ")")
                    (opt (ws >>. (withSpan recordFields |>> fieldsToExpr) .>> ws)))
                (opt (attempt (skipMany (skipAnyOf " \t") >>. lambda)))
                (fun (name, nameSpan) exprOpt trailingLambdaOpt ->
                    let baseArgs = match exprOpt with Some e -> [e] | None -> []
                    match trailingLambdaOpt with
                    | Some lambdaExpr -> FunctionCall(name, nameSpan, baseArgs @ [lambdaExpr])
                    | None -> FunctionCall(name, nameSpan, baseArgs)))

    let callWithOnlyTrailing =
        attempt (
            spanned (
                qualifiedIdentifierWithSpan >>= fun (name, nameSpan) ->
                    skipMany (skipAnyOf " \t") >>.
                    lookAhead (pchar '{') >>.
                    lambda |>> fun lambdaExpr ->
                        FunctionCall(name, nameSpan, [lambdaExpr]))
        )
    
    functionCallRef :=
        callWithOnlyTrailing <|> callWithParensAndTrailing
        <?> "function call"

// Wire up statement parser
do
    let statementTail =
        wsNoNl() >>. opt (attempt (inlineCommentNoNl ())) >>% ()

    let importStatement =
        pstring "import" >>. wsNoNl() >>. importItems |>> ImportStatement

    let useStatement =
        (pstring "use" >>. ws >>. useBinding .>> notFollowedBy (ws >>. pstring "in"))
        |>> UseStatement

    let typeDefStatement =
        typeDefCore |>> function
            | TypeDef(name, modifiers, typeParams, typeValue) ->
                TypeDefStatement(name, modifiers, typeParams, typeValue)
            | _ -> failwith "Expected type definition"

    let defExprParser =
        attempt (blockExpr()) <|> singleOrTuple (sepBy1 expression (pstring "," .>> ws))

    let defStatement =
        getPosition >>= fun lineStart ->
            pipe2
                (opt (attempt (pstring "export" >>. ws)))
                (parseDefWith lineStart.Column typedQualifiedIdentifier defExprParser)
                (fun exportOpt (name, typeOpt, expr) -> DefStatement(exportOpt.IsSome, name, typeOpt, expr))

    let destructuringDefStatement =
        getPosition >>= fun lineStart ->
            destructuringDefWith lineStart.Column defExprParser
            |>> fun (isExport, bindings) ->
                bindings |> List.mapi (fun index (name, value) -> DefStatement(isExport && index > 0, name, None, value))
    // `rec name = { ... }` is a local definition that can refer to itself (see the functions page). It is a
    // def whose value is wrapped in a call to the built-in "__rec", which tells the backend to compile the
    // lambda as a function that may call itself.
    let recStatement =
        let keywordEnd = notFollowedBy (satisfy (fun c -> Char.IsLetterOrDigit c || c = '_'))
        getPosition >>= fun lineStart ->
            (pstring "rec" >>. keywordEnd >>. ws >>. getPosition .>>. defNameAndType lineStart.Column typedQualifiedIdentifier)
            >>= fun (start, (name, typeOpt)) ->
                defEqualsContinuation lineStart.Column >>. pstring "=" >>. wsNoNl() >>. defExprParser
                >>= fun expr ->
                    let value = Expression.from expr (FunctionCall("__rec", None, [ expr ]))
                    declare DefValue value.Id [ name, nameSpan start name ] >>% DefStatement(false, name, typeOpt, value)

    let exprStatement =
        wsNoNl()
        >>. exprWithoutCrossingNewlines
        .>> statementTail
        |>> ExprStatement  // Consume trailing inline comments without consuming newlines
    
    statementRef := (attempt importStatement <|> attempt typeDefStatement <|> attempt defStatement <|> attempt useStatement <|> exprStatement) <?> "statement"
    statementItemsRef := (attempt destructuringDefStatement <|> (statement |>> List.singleton)) <?> "statement"
    statementRef := (attempt importStatement <|> attempt typeDefStatement <|> attempt defStatement <|> attempt recStatement <|> attempt useStatement <|> exprStatement) <?> "statement"

// Parser for module declaration
let moduleDecl: Parser<TopLevelItem, ParseState> =
    pstring "module" >>. ws >>. identifier |>> ModuleDecl
    <?> "module declaration"

let private valueDefExprParser =
    attempt (blockExpr()) <|> (wsInline >>. singleOrTuple (sepBy1 expression (pstring "," .>> ws)))

// Parser for value definition (top-level)
let valueDef: Parser<TopLevelItem, ParseState> =
    getPosition >>= fun lineStart ->
        pipe2
            (opt (attempt (pstring "export" >>. ws)))
            (parseDefWith lineStart.Column typedQualifiedIdentifier valueDefExprParser)
            (fun exportOpt (name, typeOpt, expr) -> Def(ValueDef(exportOpt.IsSome, name, typeOpt, expr)))
    <?> "value definition"

// Top-level `def (x, y) = ...`, as the defs it desugars into (see `destructuringBindings`).
let valueDestructuringDef: Parser<TopLevelItem list, ParseState> =
    getPosition >>= fun lineStart ->
        destructuringDefWith lineStart.Column valueDefExprParser
        |>> fun (isExport, bindings) ->
            bindings |> List.mapi (fun index (name, value) -> Def(ValueDef(isExport && index > 0, name, None, value)))
    <?> "value definition"

// Parser for type definition (top-level)
let typeDef: Parser<TopLevelItem, ParseState> =
    typeDefCore |>> Def

// Parser for top-level expressions (script-style)
let topLevelExpr: Parser<TopLevelItem, ParseState> =
    exprWithoutCrossingNewlines |>> Expr
    <?> "top-level expression"

// Parser for an attribute line such as `@entry`; it annotates the next top-level item
let attributeItem: Parser<TopLevelItem, ParseState> =
    pchar '@' >>. identifierNoWs |>> Attribute
    <?> "attribute"

let private moduleBlockItem: Parser<TopLevelItem, ParseState> =
    choice [
        (attempt (pstring "import" >>. wsNoNl() >>. importItems) |>> Import)
        attributeItem
        attempt typeDef
        valueDef
        topLevelExpr
    ]

let moduleBlockDecl: Parser<TopLevelItem, ParseState> =
    let linePayload: Parser<TopLevelItem option, ParseState> =
        choice [
            attempt (skipMany (skipAnyOf " \t") >>. inlineCommentNoNl() >>% None)
            attempt (skipMany (skipAnyOf " \t") >>. (followedBy newline <|> followedBy eof) >>% None)
            moduleBlockItem |>> Some
        ]

    let lineAtIndent indent =
        attempt (manyMinMaxSatisfy indent indent (fun c -> c = ' ' || c = '\t') >>. linePayload)

    // The block's text is noted, since what it does not export can only be named inside it.
    withSpan (attempt (
        (pstring "module" >>. ws >>. identifierNoWs .>> wsNoNl() .>> pstring "=") >>= fun name ->
            skipMany (skipAnyOf " \t") >>. newline >>.
            many (pchar ' ' <|> pchar '\t') >>= fun indentChars ->
                let indent = indentChars.Length
                if indent > 0 then
                    pipe2
                        linePayload
                        (many (attempt (newline >>. lineAtIndent indent)))
                        (fun first rest ->
                            let items = first :: rest |> List.choose id
                            match items with
                            | [] -> ModuleBlock(name, [])
                            | _ -> ModuleBlock(name, items))
                    >>= fun block ->
                        match block with
                        | ModuleBlock(_, items) when items.IsEmpty -> pzero
                        | _ -> preturn block
                else
                    pzero
    ))
    >>= fun (block, span) ->
        match block with
        | ModuleBlock(name, _) ->
            updateUserState (fun state -> { state with ModuleBlocks = state.ModuleBlocks @ [ name, span ] }) >>% block
        | _ -> preturn block
    <?> "module block declaration"

// Parser for top-level items
let topLevelItem: Parser<TopLevelItem, ParseState> =
    ws >>. choice [
        attempt moduleBlockDecl
        moduleDecl
        (attempt (pstring "import" >>. wsNoNl() >>. importItems) |>> Import)
        attributeItem
        attempt typeDef
        valueDef
        topLevelExpr
    ] .>> ws
    <?> "top-level item"

let private topLevelItems: Parser<TopLevelItem list, ParseState> =
    attempt (ws >>. valueDestructuringDef .>> ws) <|> (topLevelItem |>> List.singleton)

// Parser for a complete module
let moduleParser: Parser<Module, ParseState> =
    ws >>. many topLevelItems .>> ws .>> eof |>> List.concat
    <?> "module"

let private qualifyModuleName (prefix: string) (name: string) =
    if String.IsNullOrWhiteSpace prefix then name else $"{prefix}.{name}"

let private rewriteTypeExprInModule (typeMap: Map<string, string>) (typeExpr: TypeExpr) : TypeExpr =
    let rec rewrite t =
        match t with
        | TypeName name ->
            match typeMap |> Map.tryFind name with
            | Some qualified -> TypeName qualified
            | None -> TypeName name
        | TypeVar _
        | TypeUnit -> t
        | TypeTuple items -> TypeTuple (items |> List.map rewrite)
        | TypeRecord fields ->
            TypeRecord (
                fields
                |> List.map (function
                    | TypeField(name, isMutable, isOptional, typeOpt, defaultOpt) -> TypeField(name, isMutable, isOptional, typeOpt |> Option.map rewrite, defaultOpt)
                    | TypeMember(name, typeOpt) -> TypeMember(name, typeOpt |> Option.map rewrite)))
        | TypeApply(name, args) ->
            let mappedName = typeMap |> Map.tryFind name |> Option.defaultValue name
            TypeApply(mappedName, args |> List.map rewrite)
        | TypeFunc(inputTy, outputTy) -> TypeFunc(rewrite inputTy, rewrite outputTy)
        | TypeTag(name, payload) -> TypeTag(name, payload |> Option.map rewrite)
        | TypeUnion items -> TypeUnion (items |> List.map rewrite)
        | TypeIntersection items -> TypeIntersection (items |> List.map rewrite)
        | TypeOptional inner -> TypeOptional (rewrite inner)
        | TypeContext items -> TypeContext (items |> List.map rewrite)
        | TypeWithContext(items, inner) -> TypeWithContext(items |> List.map rewrite, rewrite inner)
        | TypeConstraint(name, baseTy, predicate) -> TypeConstraint(name, rewrite baseTy, predicate)
        | TypeWhere(baseTy, predicate) -> TypeWhere(rewrite baseTy, predicate)
    rewrite typeExpr

let private rewriteExpressionInModule (valueMap: Map<string, string>) (typeMap: Map<string, string>) (expr: Expression) : Expression =
    let rewriteQualifiedHead (name: string) =
        if name.Contains(".") then
            let dot = name.IndexOf('.')
            let head = name.Substring(0, dot)
            let tail = name.Substring(dot + 1)
            match typeMap |> Map.tryFind head with
            | Some qualifiedHead -> $"{qualifiedHead}.{tail}"
            | None -> name
        else
            name

    let rewriteName (name: string) =
        match valueMap |> Map.tryFind name with
        | Some qualified -> qualified
        | None ->
            match typeMap |> Map.tryFind name with
            | Some qualifiedType -> qualifiedType
            | None -> rewriteQualifiedHead name

    let rec rewrite (e: Expression) = { e with Kind = rewriteKind e }
    and rewriteKind e =
        match e.Kind with
        | IdentifierExpr name -> IdentifierExpr(rewriteName name)
        | FunctionCall(name, range, args) -> FunctionCall(rewriteName name, range, args |> List.map rewrite)
        | Lambda(args, body) -> Lambda(args, rewrite body)
        | BinaryOp(op, left, right) -> BinaryOp(op, rewrite left, rewrite right)
        | Pipe(value, funcName, range, args) -> Pipe(rewrite value, rewriteName funcName, range, args |> List.map rewrite)
        | Block statements ->
            Block (
                statements
                |> List.map (function
                    | DefStatement(isExport, name, typeOpt, valueExpr) -> DefStatement(isExport, name, typeOpt |> Option.map (rewriteTypeExprInModule typeMap), rewrite valueExpr)
                    | TypeDefStatement(name, modifiers, typeParams, typeExpr) ->
                        let mappedParams = typeParams |> List.map (fun (paramName, paramTypeOpt) -> paramName, paramTypeOpt |> Option.map (rewriteTypeExprInModule typeMap))
                        TypeDefStatement(name, modifiers, mappedParams, rewriteTypeExprInModule typeMap typeExpr)
                    | ImportStatement items -> ImportStatement items
                    | UseStatement binding -> 
                        let mapped = 
                            match binding with 
                            | UseValue boundExpr -> UseValue (rewrite boundExpr)
                            | UseContextInstance(ctxType, boundExpr) -> UseContextInstance(rewriteTypeExprInModule typeMap ctxType, rewrite boundExpr)
                        UseStatement mapped
                    | ExprStatement bodyExpr -> ExprStatement (rewrite bodyExpr)))
        | Match(values, arms) -> Match(values |> List.map rewrite, arms |> List.map (fun (patterns, body) -> patterns, rewrite body))
        | TupleExpr items -> TupleExpr (items |> List.map rewrite)
        | RecordExpr fields ->
            RecordExpr (
                fields
                |> List.map (function
                    | NamedField(name, value) -> NamedField(name, rewrite value)
                    | PositionalField value -> PositionalField (rewrite value)))
        | ListExpr items -> ListExpr (items |> List.map rewrite)
        | TagExpr(name, payload) -> TagExpr(name, payload |> Option.map rewrite)
        | IfExpr(cond, ifTrue, ifFalse) -> IfExpr(rewrite cond, rewrite ifTrue, rewrite ifFalse)
        | MemberAccess(baseExpr, field, range) -> MemberAccess(rewrite baseExpr, field, range)
        | ContextMemberAccess(ctxType, field) -> ContextMemberAccess(rewriteTypeExprInModule typeMap ctxType, field)
        | ContextMemberCall(ctxType, field, args) -> ContextMemberCall(rewriteTypeExprInModule typeMap ctxType, field, args |> List.map rewrite)
        | UseIn(binding, body) ->
            let mappedBinding =
                match binding with
                | UseValue value -> UseValue (rewrite value)
                | UseContextInstance(ctxType, value) -> UseContextInstance(rewriteTypeExprInModule typeMap ctxType, rewrite value)
            UseIn(mappedBinding, rewrite body)
        | WorkflowBindExpr(name, value) -> WorkflowBindExpr(name, rewrite value)
        | WorkflowReturnExpr value -> WorkflowReturnExpr (rewrite value)
        | InterpolatedString parts -> InterpolatedString (parts |> List.map (function | StringText text -> StringText text | StringExpr value -> StringExpr (rewrite value)))
        | UnitExpr
        | LiteralExpr _ -> e.Kind
    rewrite expr

let rec private expandModuleBlock (prefix: string) (items: TopLevelItem list) : TopLevelItem list =
    let localTypeNames =
        items
        |> List.choose (function
            | Def (TypeDef(name, _, _, _)) when not (name.Contains(".")) -> Some name
            | _ -> None)
        |> Set.ofList

    let typeMap =
        localTypeNames
        |> Seq.map (fun name -> name, qualifyModuleName prefix name)
        |> Map.ofSeq

    let localValueNames =
        items
        |> List.choose (function
            | Def (ValueDef(_, name, _, _)) when not (name.Contains(".")) -> Some name
            | _ -> None)
        |> Set.ofList

    let valueMap =
        localValueNames
        |> Seq.map (fun name -> name, qualifyModuleName prefix name)
        |> Map.ofSeq

    let qualifyDefName (name: string) =
        if name.Contains(".") then
            let dot = name.IndexOf('.')
            let head = name.Substring(0, dot)
            let tail = name.Substring(dot + 1)
            match typeMap |> Map.tryFind head with
            | Some qualifiedHead -> $"{qualifiedHead}.{tail}"
            | None -> qualifyModuleName prefix name
        else
            qualifyModuleName prefix name

    let qualifyTypeName (name: string) =
        if name.Contains(".") then qualifyModuleName prefix name else typeMap |> Map.tryFind name |> Option.defaultValue (qualifyModuleName prefix name)

    items
    |> List.collect (fun item ->
        match item with
        | ModuleDecl _ -> []
        | ModuleBlock(name, nested) ->
            let nestedPrefix = qualifyModuleName prefix name
            ModuleDecl nestedPrefix :: expandModuleBlock nestedPrefix nested
        | Import imports -> [ Import imports ]
        | Attribute name -> [ Attribute name ]
        | Expr expr -> [ Expr (rewriteExpressionInModule valueMap typeMap expr) ]
        | Def (ValueDef(isExport, name, typeOpt, expr)) ->
            [ Def (ValueDef(isExport, qualifyDefName name, typeOpt |> Option.map (rewriteTypeExprInModule typeMap), rewriteExpressionInModule valueMap typeMap expr)) ]
        | Def (TypeDef(name, modifiers, typeParams, body)) ->
            let mappedTypeParams = typeParams |> List.map (fun (paramName, paramTypeOpt) -> paramName, paramTypeOpt |> Option.map (rewriteTypeExprInModule typeMap))
            [ Def (TypeDef(qualifyTypeName name, modifiers, mappedTypeParams, rewriteTypeExprInModule typeMap body)) ])

let private expandMidFileModules (items: TopLevelItem list) : TopLevelItem list =
    let rec loop (currentModule: string option) (remaining: TopLevelItem list) (acc: TopLevelItem list) =
        match remaining with
        | [] -> List.rev acc
        | item :: rest ->
            match item with
            | ModuleDecl name -> loop (Some name) rest (item :: acc)
            | ModuleBlock(name, innerItems) ->
                let prefix =
                    match currentModule with
                    | Some root when not (String.IsNullOrWhiteSpace root) -> qualifyModuleName root name
                    | _ -> name
                let expanded = ModuleDecl prefix :: expandModuleBlock prefix innerItems
                loop currentModule rest ((List.rev expanded) @ acc)
            | _ -> loop currentModule rest (item :: acc)
    loop None items []

// Public API for parsing

/// Why a module could not be parsed, and where.
type ParseError = { Message: string; Span: SourceSpan }

/// Parses the source of the file at `filePath`, as `parseModuleFile` does, together with the names its nodes
/// declare and the text of each `module name =` block in it.
let parseModuleFileWithNames (filePath: string) (input: string) : Result<Module * DeclaredNames * (ModuleName * SourceSpan) list, ParseError> =
    let normalized =
        input.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd()
    let initialState = ParseState.empty ()
    // A parse fails at a point: its span is the character there, or nothing at the end of the input.
    let errorAt (position: Position) message =
        { Message = message
          Span = spanOf position (min normalized.Length (int position.Index + 1)) }
    match runParserOnString moduleParser initialState filePath normalized with
    | ParserResult.Success(result, state, _) -> Result.Ok (expandMidFileModules result, state.Declared, state.ModuleBlocks)
    | ParserResult.Failure(errorMsg, error, _) ->
        // The failure of a workflow keyword is backtracked over, and FParsec reports some enclosing construct
        // instead (often the whole def), so it is reported where it happened.
        match initialState.WorkflowKeyword.Value with
        | Some (position, message) ->
            use stream = new CharStream<unit>(normalized, 0, normalized.Length)
            stream.Name <- filePath
            let focused = ParserError(position, null, ErrorMessageList(ErrorMessage.Message message))
            Result.Error (errorAt position (focused.ToString stream))
        | None -> Result.Error (errorAt error.Position errorMsg)

/// Parses the source of the file at `filePath`, which is what the spans of the module's nodes refer to. Line
/// endings are normalised to `\n` first, so span offsets are into the normalised source (lines and columns are
/// unaffected).
let parseModuleFile (filePath: string) (input: string) : Result<Module, string> =
    parseModuleFileWithNames filePath input
    |> Result.map (fun (module', _, _) -> module')
    |> Result.mapError (fun error -> error.Message)


/// Parses source that has no file of its own.
let parseModule (input: string) : Result<Module, string> = parseModuleFile "" input

