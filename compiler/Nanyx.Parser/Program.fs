module Parser.Program

open System
open FParsec

// AST Types - Simplified for basic parsing
type Identifier = string
type ModuleName = string
type SourceRange = int * int

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
      Alias: Identifier option }

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

and Expression =
    | LiteralExpr of Literal
    | UnitExpr
    | IdentifierExpr of Identifier * SourceRange option
    | FunctionCall of Identifier * SourceRange option * Expression list
    | Lambda of (Identifier * TypeExpr option) list * Expression
    | BinaryOp of string * Expression * Expression
    | Pipe of Expression * Identifier * SourceRange option * Expression list  // expr \func or expr \func(args)
    | Block of Statement list
    | Match of Expression list * MatchArm list  // Can match on multiple values
    | TupleExpr of Expression list  // Positional-only tuple (for backward compat with existing tests)
    | RecordExpr of RecordField list  // Mixed tuple/record with named and/or positional fields
    | ListExpr of Expression list
    | TagExpr of Identifier * Expression option  // #tagName or #tagName(expr)
    | IfExpr of Expression * Expression * Expression  // if condition then trueExpr else falseExpr
    | MemberAccess of Expression * Identifier * SourceRange option  // expr.field
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

type Module = TopLevelItem list

// Helper: whitespace and comment handling
let comment () = pstring "--" >>. skipRestOfLine true
let inlineCommentNoNl () = pstring "--" >>. skipRestOfLine false
let ws: Parser<unit, unit> = skipMany (skipMany1 spaces1 <|> comment ())
let wsAll: Parser<unit, unit> = ws
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

let newlineIndentedContinuation (refCol: int64) : Parser<unit, unit> =
    attempt (
        skipMany (skipAnyOf " \t") >>. newline >>.
        many (pchar ' ' <|> pchar '\t') >>.
        getPosition >>= fun pos ->
            if pos.Column > refCol then preturn () else pzero
    )

let defEqualsContinuation (refCol: int64) : Parser<unit, unit> =
    attempt (wsNoNl() >>. followedBy (pstring "=")) <|> newlineIndentedContinuation refCol

let parseDefWith (lineStartCol: int64) (identParser: Parser<Identifier * TypeExpr option, unit>) (exprParser: Parser<Expression, unit>) =
    (pstring "def" >>. ws >>. identParser .>> wsNoNl())
    >>= fun (name, typeOpt) ->
        defEqualsContinuation lineStartCol >>. pstring "=" >>. wsNoNl() >>. exprParser
        |>> fun expr -> (name, typeOpt, expr)

// Parser for identifiers
let identifierNoWs: Parser<Identifier, unit> =
    let isIdentStart c = isLetter c || c = '_'
    let isIdentContinue c = isLetter c || isDigit c || c = '_'
    many1Satisfy2 isIdentStart isIdentContinue
    <?> "identifier"

let identifier: Parser<Identifier, unit> = identifierNoWs .>> wsInline

let typeNameIdentifier: Parser<Identifier, unit> =
    (pchar '$' >>. identifierNoWs |>> fun name -> "$" + name)
    <|> identifierNoWs

let qualifiedIdentifier: Parser<Identifier, unit> =
    pipe2
        typeNameIdentifier
        (many (pchar '.' >>. identifierNoWs))
        (fun head tail -> String.concat "." (head :: tail))

let posToRange (pos: Position) : SourceRange =
    (int pos.Line - 1, int pos.Column - 1)

let identifierWithRange: Parser<Identifier * SourceRange option, unit> =
    getPosition .>>. identifierNoWs
    |>> (fun (pos, name) -> name, Some (posToRange pos))

let qualifiedIdentifierWithRange: Parser<Identifier * SourceRange option, unit> =
    getPosition .>>. qualifiedIdentifier
    |>> (fun (pos, name) -> name, Some (posToRange pos))


// Parser for literals
let stringLiteralNoWs: Parser<Literal, unit> =
    let doubleQuoted =
        between (pstring "\"") (pstring "\"") (manyChars (noneOf "\""))
        |>> StringLit
    let singleQuoted =
        between (pstring "'") (pstring "'") (manyChars (noneOf "'"))
        |>> StringLit
    attempt doubleQuoted <|> singleQuoted
    <?> "string literal"

let stringLiteral: Parser<Literal, unit> = stringLiteralNoWs  // Don't consume ws

let intLiteralNoWs: Parser<Literal, unit> =
    // Allow parsing int even if followed by dots (for range operator "..")
    // Just don't allow a SINGLE dot (which would make it a float)
    let notFloat = followedBy (pstring "..") <|> notFollowedBy (pchar '.')
    pint32 .>> notFloat |>> IntLit
    <?> "integer literal"

let intLiteral: Parser<Literal, unit> = intLiteralNoWs  // Don't consume ws - let caller decide

let floatLiteralNoWs: Parser<Literal, unit> =
    pfloat |>> FloatLit
    <?> "float literal"

let floatLiteral: Parser<Literal, unit> = floatLiteralNoWs  // Don't consume ws - let caller decide

let boolLiteralNoWs: Parser<Literal, unit> =
    (stringReturn "true" (BoolLit true) <|> stringReturn "false" (BoolLit false))
    <?> "boolean literal"

let boolLiteral: Parser<Literal, unit> = boolLiteralNoWs  // Don't consume ws - let caller decide

let literalNoWs: Parser<Literal, unit> =
    choice [
        attempt intLiteralNoWs
        attempt floatLiteralNoWs
        boolLiteralNoWs
        stringLiteralNoWs
    ]
    <?> "literal"

let literal: Parser<Literal, unit> =
    choice [
        attempt intLiteral
        attempt floatLiteral
        boolLiteral
        stringLiteral
    ]
    <?> "literal"

let modulePath: Parser<string, unit> =
    pipe2
        identifierNoWs
        (many (pipe2 (pchar '.' <|> pchar '/') identifierNoWs (fun sep seg -> string sep + seg)))
        (fun head tail -> head + String.concat "" tail)

let importSource: Parser<ImportSource, unit> =
    attempt (
        stringLiteralNoWs |>> function
            | StringLit s -> PathImport s
            | _ -> failwith "Expected string literal for import path"
    ) <|> (modulePath |>> ModuleImport)

let importItem: Parser<ImportItem, unit> =
    pipe2
        importSource
        (opt (attempt (ws >>. pstring "as" >>. ws >>. identifierNoWs)))
        (fun source alias -> { Source = source; Alias = alias })

let importItems: Parser<ImportItem list, unit> =
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
let expression, expressionRef = createParserForwardedToRef<Expression, unit>()
let exprWithoutCrossingNewlines, exprWithoutCrossingNewlinesRef = createParserForwardedToRef<Expression, unit>()
let inlineExpression, inlineExpressionRef = createParserForwardedToRef<Expression, unit>()
let simpleExpression, simpleExpressionRef = createParserForwardedToRef<Expression, unit>()
let lambda, lambdaRef = createParserForwardedToRef<Expression, unit>()
let functionCall, functionCallRef = createParserForwardedToRef<Expression, unit>()
let matchExpr, matchExprRef = createParserForwardedToRef<Expression, unit>()
let statement, statementRef = createParserForwardedToRef<Statement, unit>()
let typeExpr, typeExprRef = createParserForwardedToRef<TypeExpr, unit>()

// Pattern parsers
let pattern, patternRef = createParserForwardedToRef<Pattern, unit>()

// Interpolated double-quoted strings with {expr} holes
let interpolatedStringExprNoWs: Parser<Expression, unit> =
    let textPart = many1Chars (noneOf "\"{") |>> StringText
    let holePart =
        between (pchar '{' .>> ws) (ws >>. pchar '}') expression
        |>> StringExpr
    let partsParser = many (choice [attempt holePart; textPart])
    between (pstring "\"") (pstring "\"") partsParser
    |>> fun parts ->
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

let wildcardPattern = pstring "_" >>% WildcardPattern
let elsePattern = pstring "else" >>% ElsePattern
let identifierPattern = identifierNoWs |>> IdentifierPattern

// Parse either a range pattern (1..10), an integer literal pattern (1), 
// or other literal types (float, bool, string)
// NOTE: For ranges, we only allow INTEGER literals to avoid ambiguity with floats like "1."
let literalOrRangePattern =
    choice [
        // Try integer with optional range
        attempt (
            intLiteralNoWs >>= fun startLit ->
            opt (pstring ".." >>. ws >>. intLiteralNoWs) >>= fun endLitOpt ->
            match endLitOpt with
            | Some endLit -> preturn (RangePattern(LiteralExpr startLit, LiteralExpr endLit))
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
        pstring "..." >>. ws >>. opt identifierNoWs
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
// Guard pattern: > 10, < 5, >= 10, <= 5, == 5, != 5
let guardPattern =
    attempt (
        let guardOp = 
            choice [
                pstring ">="
                pstring "<="
                pstring "=="
                pstring "!="
                pstring ">"
                pstring "<"
            ]
        guardOp .>>. (ws >>. (literalNoWs |>> LiteralExpr))
        |>> fun (op, value) -> GuardPattern(op, value)
    )

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

    let typeTupleOrParen =
        between
            (pstring "(")
            (ws >>. pstring ")")
            (sepBy (ws >>. typeExpr) (pstring "," .>> ws))
        |>> fun items ->
            match items with
            | [] -> TypeUnit
            | [single] -> single
            | multiple -> TypeTuple multiple

    let typeTagPayloadItem =
        let namedField =
            pipe3
                identifierNoWs
                (opt (pstring "?" >>% true))
                ((wsNoNl() >>. (pstring ":" <|> pstring "::") >>. ws >>. typeExpr))
                (fun name optFlag typeValue ->
                    let isOptional = optFlag.IsSome
                    TypeField(name, false, isOptional, Some typeValue, None))

        attempt (namedField |>> Choice1Of2) <|> (typeExpr |>> Choice2Of2)

    let typeTag =
        pstring "#" >>. identifierNoWs >>= fun tagName ->
        opt (between (pstring "(") (pstring ")") (sepBy1 (ws >>. typeTagPayloadItem) (pstring "," .>> ws))) >>= fun payloadOpt ->
        match payloadOpt with
        | None -> preturn (TypeTag(tagName, None))
        | Some items ->
            let hasNamed = items |> List.exists (function Choice1Of2 _ -> true | _ -> false)
            if hasNamed then
                let fields =
                    items
                    |> List.choose (function Choice1Of2 field -> Some field | _ -> None)
                preturn (TypeTag(tagName, Some (TypeRecord fields)))
            else
                let payloadTypes = items |> List.choose (function Choice2Of2 t -> Some t | _ -> None)
                match payloadTypes with
                | [single] -> preturn (TypeTag(tagName, Some single))
                | multiple -> preturn (TypeTag(tagName, Some (TypeTuple multiple)))

    let typeApply =
        pipe2
            identifierNoWs
            (between (pstring "(") (pstring ")") (sepBy1 (ws >>. typeExpr) (pstring "," .>> ws)))
            (fun name args -> TypeApply(name, args))

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
    ()

let typedIdentifier: Parser<Identifier * TypeExpr option, unit> =
    pipe2
        identifierNoWs
        (opt (attempt (wsInline >>. pstring ":" >>. ws >>. typeExpr)))
        (fun name typeOpt -> (name, typeOpt))

let typedQualifiedIdentifier: Parser<Identifier * TypeExpr option, unit> =
    pipe2
        qualifiedIdentifier
        (opt (attempt (wsInline >>. pstring ":" >>. ws >>. typeExpr)))
        (fun name typeOpt -> (name, typeOpt))

let typeParam: Parser<Identifier * TypeExpr option, unit> =
    pipe2
        identifierNoWs
        (opt (attempt (ws >>. pstring "::" >>. ws >>. typeExpr)))
        (fun name typeOpt -> (name, typeOpt))

// Block parser - parses indented statements after a newline
let blockExpr() : Parser<Expression, unit> =
    skipMany (skipAnyOf " \t") >>. newline >>.
    many (pchar ' ' <|> pchar '\t') >>= fun indentChars ->
        let indentLevel = indentChars.Length
        if indentLevel > 0 then
            let statementAtIndent =
                attempt (manyMinMaxSatisfy indentLevel indentLevel (fun c -> c = ' ' || c = '\t') >>. statement)

            pipe2
                statement
                (many (attempt (newline >>. statementAtIndent)))
                (fun first rest -> Block (first :: rest))
        else
            pzero

let matchArmBody: Parser<Expression, unit> =
    attempt (blockExpr()) <|> (wsNoNl() >>. exprWithoutCrossingNewlines)

let typeDefCore: Parser<Definition, unit> =
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

let valueDefCore: Parser<Definition, unit> =
    getPosition >>= fun lineStart ->
        pipe2
            (opt (attempt (pstring "export" >>. ws)))
            (parseDefWith lineStart.Column typedIdentifier (wsInline >>. (sepBy1 expression (pstring "," .>> ws) |>> fun exprs ->
                match exprs with
                | [single] -> single
                | multiple -> TupleExpr multiple)))
            (fun exportOpt (name, typeOpt, expr) -> ValueDef(exportOpt.IsSome, name, typeOpt, expr))
    <?> "value definition"

// Match expression parser
do
    let matchArm =
        pstring "|" >>. ws >>. sepBy1 pattern (pstring "," .>> ws) .>>. (pstring "->" >>. matchArmBody)
        <?> "match arm"
    
    let matchArmWithLeadingWs =
        attempt (ws >>. matchArm)

    matchExprRef :=
        pipe2
            (pstring "match" >>. ws >>. sepBy1 simpleExpression (pstring "," .>> ws))  // Multiple expressions
            (many1 matchArmWithLeadingWs)
            (fun scrutinees arms -> Match(scrutinees, arms))
        .>> skipMany (skipAnyOf " \t\r\n")
        <?> "match expression"

// Operator precedence parser
let opp = new OperatorPrecedenceParser<Expression, unit, unit>()

// Record field parser: either "name = expr" or just "expr"
let recordField =
    (attempt (
        pipe2
            (identifierNoWs .>> ws .>> pstring "=" .>> ws)
            expression
            (fun name expr -> NamedField(name, expr))
    ) <|> (expression |>> PositionalField))
    <?> "record field"

// Unit literal: ()
let unitExpr =
    between (pstring "(") (ws >>. pstring ")") (preturn ()) |>> fun _ -> UnitExpr

// Tuple/Record or parenthesized expression parser
// Handles: (x), (x, y), (x = 1), (x = 1, y = 2), (x = 1, 2), etc.
let tupleOrParenExpr =
    (between
        (pstring "(")
        (ws >>. pstring ")")
        (sepBy1 (ws >>. recordField) (pstring "," .>> ws))
    |>> fun fields ->
        match fields with
        | [PositionalField single] -> single  // Single expression in parens: (x) is just x
        | _ ->
            // Check if all fields are positional (for backward compat)
            let allPositional = fields |> List.forall (function PositionalField _ -> true | _ -> false)
            if allPositional then
                // Pure tuple: extract expressions
                let exprs = fields |> List.map (function PositionalField e -> e | _ -> failwith "unexpected")
                TupleExpr exprs
            else
                // Has named fields: use RecordExpr
                RecordExpr fields)
    <?> "tuple or record"

// List expression parser
let listExpr =
    between
        (pstring "[")
        (pstring "]")
        (sepBy (ws >>. expression) (pstring "," .>> ws))
    |>> ListExpr

// Tag expression parser: #tagName or #tagName(expr)
// The payload can be a tuple-like expression with comma-separated values
let tagExpr =
    pstring "#" >>. identifierNoWs >>= fun tagName ->
    opt (between 
            (pstring "(") 
            (pstring ")") 
            (sepBy1 (ws >>. expression) (pstring "," .>> ws))) >>= fun payloadOpt ->
    match payloadOpt with
    | None -> preturn (TagExpr(tagName, None))
    | Some [single] -> preturn (TagExpr(tagName, Some single))  // Single value
    | Some multiple -> preturn (TagExpr(tagName, Some (TupleExpr multiple)))  // Multiple values as tuple

// If-expression parser: if condition then trueExpr else falseExpr
let ifExpr =
    pipe3
        (pstring "if" >>. ws >>. expression)
        (ws >>. pstring "then" >>. ws >>. expression)
        (ws >>. pstring "else" >>. ws >>. expression)
        (fun cond thenExpr elseExpr -> IfExpr(cond, thenExpr, elseExpr))
    <?> "if expression"

let workflowBindExpr =
    attempt (
        pipe2
            (identifierNoWs .>> pchar '!' .>> ws)
            expression
            (fun name expr -> WorkflowBindExpr(name, expr)))
    <?> "workflow bind"

let workflowReturnExpr =
    let isIdentChar c = isLetter c || isDigit c || c = '_'
    attempt (
        pstring "return"
        .>> notFollowedBy (satisfy isIdentChar)
        >>. ws
        >>. expression
        |>> WorkflowReturnExpr)
    <?> "workflow return"

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
    pipe2
        (pstring "use" >>. ws >>. useBinding .>> ws .>> pstring "in" .>> ws)
        expression
        (fun binding body -> UseIn(binding, body))
    <?> "use-in expression"

// Primary expression (literals, identifiers, function calls, lambdas, parenthesized expressions)
// Version without trailing whitespace consumption (for use in block contexts)

// Context member access: C(T).field or C(T).field(args) - qualified access to context functions
// ONLY matches when type parameters are present (parens required)
let contextMemberAccess =
    // Parse a context type application with required type args: C(T)
    let contextTypeApply = 
        pipe2
            identifierNoWs
            (between (pstring "(") (pstring ")") (sepBy1 (ws >>. typeExpr) (pstring "," .>> ws)))
            (fun name args -> TypeApply(name, args))
    // Parse args as tuple-like: (expr, expr, ...)
    let callArgs =
        between (pstring "(") (pstring ")") 
            (opt (ws >>. (sepBy1 (ws >>. expression) (pstring "," .>> ws)) .>> ws))
    pipe3
        (contextTypeApply .>> pstring ".")
        identifierNoWs
        (opt callArgs)
        (fun ctxType field argsOpt ->
            match argsOpt with
            | Some (Some args) -> ContextMemberCall(ctxType, field, args)
            | Some None -> ContextMemberCall(ctxType, field, [])  // empty parens like f()
            | None -> ContextMemberAccess(ctxType, field))

let primaryExprNoWs =
    choice [
        attempt interpolatedStringExprNoWs
        attempt workflowReturnExpr
        attempt workflowBindExpr
        attempt useInExpr
        attempt unitExpr
        attempt ifExpr  // if-then-else must be early to avoid partial matches
        attempt matchExpr
        attempt lambda
        attempt contextMemberAccess  // C(T).field - must come before functionCall
        attempt functionCall
        attempt tagExpr  // #tag or #tag(expr)
        literalNoWs |>> LiteralExpr
        listExpr
        tupleOrParenExpr
        identifierWithRange |>> (fun (name, range) -> IdentifierExpr(name, range))
    ]

do simpleExpressionRef := primaryExprNoWs

// Member access: parse base expression, then chain .field.field.field... or .0.1.2...
let memberAccessChain =
    let tupleIndex = many1Satisfy isDigit
    let fieldOrIndex = identifierNoWs <|> tupleIndex
    let dotField = pstring "." >>. (getPosition .>>. fieldOrIndex |>> fun (pos, field) -> field, Some (posToRange pos))
    primaryExprNoWs >>= fun baseExpr ->
        many dotField |>> fun fields ->
            List.fold (fun expr (field, range) -> MemberAccess(expr, field, range)) baseExpr fields

let primaryExpr = memberAccessChain .>> wsInline  // Consume trailing inline ws for operator parsing

opp.TermParser <- primaryExpr

// Define operators with precedence
// Multiplicative operators (higher precedence)
opp.AddOperator(InfixOperator("*", ws, 7, Associativity.Left, fun x y -> BinaryOp("*", x, y)))
opp.AddOperator(InfixOperator("/", ws, 7, Associativity.Left, fun x y -> BinaryOp("/", x, y)))
opp.AddOperator(InfixOperator("%", ws, 7, Associativity.Left, fun x y -> BinaryOp("%", x, y)))

// Additive operators
opp.AddOperator(InfixOperator("+", ws, 6, Associativity.Left, fun x y -> BinaryOp("+", x, y)))
opp.AddOperator(InfixOperator("-", ws, 6, Associativity.Left, fun x y -> BinaryOp("-", x, y)))

// Comparison operators (lower precedence)
opp.AddOperator(InfixOperator("<", ws, 4, Associativity.Left, fun x y -> BinaryOp("<", x, y)))
opp.AddOperator(InfixOperator(">", ws, 4, Associativity.Left, fun x y -> BinaryOp(">", x, y)))
opp.AddOperator(InfixOperator("<=", ws, 4, Associativity.Left, fun x y -> BinaryOp("<=", x, y)))
opp.AddOperator(InfixOperator(">=", ws, 4, Associativity.Left, fun x y -> BinaryOp(">=", x, y)))

// Equality operators 
opp.AddOperator(InfixOperator("==", ws, 3, Associativity.Left, fun x y -> BinaryOp("==", x, y)))
opp.AddOperator(InfixOperator("!=", ws, 3, Associativity.Left, fun x y -> BinaryOp("!=", x, y)))

// Logical operators (lowest precedence)
opp.AddOperator(InfixOperator("&&", ws, 2, Associativity.Left, fun x y -> BinaryOp("&&", x, y)))
opp.AddOperator(InfixOperator("||", ws, 1, Associativity.Left, fun x y -> BinaryOp("||", x, y)))

// Note: Comma is NOT an operator in the OPP
// Commas are handled explicitly in tuple/record literals and function calls
// For bare tuple expressions (def x = 1, 2), we handle it in defStatement

// Pipe parser - handles expr \func or expr \func(args)
let pipeTarget =
    pipe3
        qualifiedIdentifierWithRange
        (opt (between (pstring "(") (pstring ")") (ws >>. (sepBy1 expression (pstring "," .>> ws) |>> fun exprs ->
            match exprs with
            | [single] -> single
            | multiple -> TupleExpr multiple))))
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
let oppInline = new OperatorPrecedenceParser<Expression, unit, unit>()
oppInline.TermParser <- primaryExpr

oppInline.AddOperator(InfixOperator("*", wsInline, 7, Associativity.Left, fun x y -> BinaryOp("*", x, y)))
oppInline.AddOperator(InfixOperator("/", wsInline, 7, Associativity.Left, fun x y -> BinaryOp("/", x, y)))

oppInline.AddOperator(InfixOperator("+", wsInline, 6, Associativity.Left, fun x y -> BinaryOp("+", x, y)))
oppInline.AddOperator(InfixOperator("-", wsInline, 6, Associativity.Left, fun x y -> BinaryOp("-", x, y)))

oppInline.AddOperator(InfixOperator("<", wsInline, 4, Associativity.Left, fun x y -> BinaryOp("<", x, y)))
oppInline.AddOperator(InfixOperator(">", wsInline, 4, Associativity.Left, fun x y -> BinaryOp(">", x, y)))
oppInline.AddOperator(InfixOperator("<=", wsInline, 4, Associativity.Left, fun x y -> BinaryOp("<=", x, y)))
oppInline.AddOperator(InfixOperator(">=", wsInline, 4, Associativity.Left, fun x y -> BinaryOp(">=", x, y)))

oppInline.AddOperator(InfixOperator("==", wsInline, 3, Associativity.Left, fun x y -> BinaryOp("==", x, y)))
oppInline.AddOperator(InfixOperator("!=", wsInline, 3, Associativity.Left, fun x y -> BinaryOp("!=", x, y)))

let pipeExpr =
    let pipeLeadingWs =
        skipMany (skipAnyOf " \t") >>.
        skipMany (attempt (newline >>. skipMany (skipAnyOf " \t")))
    let pipeOp = pipeLeadingWs >>. pstring "\\" >>. wsInline >>. pipeTarget
    pipe2
        opp.ExpressionParser
        (many (attempt (pipeOp .>> wsInline)))
        (fun expr pipes ->
            pipes |> List.fold (fun acc (funcName, range, args) -> Pipe(acc, funcName, range, args)) expr
        )

let inlinePipeExpr =
    let pipeOp = pstring "\\" >>. wsInline >>. pipeTarget
    pipe2
        oppInline.ExpressionParser
        (many (attempt (pipeOp .>> wsInline)))
        (fun expr pipes ->
            pipes |> List.fold (fun acc (funcName, range, args) -> Pipe(acc, funcName, range, args)) expr
        )

do inlineExpressionRef := inlinePipeExpr

// Expression parser that stops at newlines (for use in blocks)
do exprWithoutCrossingNewlinesRef :=
    attempt matchExpr <|> inlinePipeExpr

// Wire up the expression parser with piping support
// Prefer match expressions first to avoid parsing "match" as an identifier.
do expressionRef := (attempt matchExpr <|> pipeExpr)

// Wire up the lambda parser
do 
    let paramList = sepBy typedIdentifier (pstring "," .>> ws)
    let arrow = wsInline >>. pstring "->" .>> wsInline
    
    // Lambda body: allow multi-line statement blocks or a single expression
    let blockBody =
        let matchStatement = wsInline >>. matchExpr |>> ExprStatement
        let statementLine =
            attempt (
                ws >>.
                (attempt matchStatement <|> statement)
            )
        let multiLine =
            attempt (
                skipMany (skipAnyOf " \t\r") >>. newline >>.
                ws >>.
                (many1 statementLine .>> ws)
                |>> Block
            )

        let singleLine =
            skipMany (skipAnyOf " \t\r\n") >>.
            expression .>>
            skipMany (skipAnyOf " \t\r\n")

        attempt multiLine <|> singleLine
    
    let lambdaWithParams = pipe2 (paramList .>> arrow .>> skipMany (skipAnyOf " \t")) blockBody (fun parameters body -> Lambda(parameters, body))
    let lambdaNoParams = blockBody |>> (fun body -> Lambda([], body))
    
    // Shorthand lambda: { * } means { x, y -> x * y }
    let shorthandBinaryOp =
        let operators = ["*"; "/"; "+"; "-"; "<"; ">"; "<="; ">="; "=="; "!="]
        choice (operators |> List.map (fun op ->
            pstring op >>. ws >>. notFollowedBy (skipAnyOf "0123456789.\"'([{") >>% Lambda([("x", None); ("y", None)], BinaryOp(op, IdentifierExpr("x", None), IdentifierExpr("y", None)))
        ))
    
    // Shorthand lambda: { * 2 } means { x -> x * 2 }
    // Also supports comparison operators: { > 10 } means { x -> x > 10 }
    let shorthandUnaryOp =
        let operators = [
            ("*", "*"); ("/", "/"); ("+", "+"); ("-", "-");
            ("<=", "<="); (">=", ">="); ("<", "<"); (">", ">");
            ("==", "=="); ("!=", "!=")
        ]
        choice (operators |> List.map (fun (op, opStr) ->
            pstring op >>. ws >>. primaryExpr |>> (fun rightExpr ->
                Lambda([("x", None)], BinaryOp(opStr, IdentifierExpr("x", None), rightExpr))
            )
        ))
    
    // Shorthand lambda: { .name } means { x -> x.name }
    let shorthandPropertyAccess =
        pstring "." >>. identifier |>> (fun propName ->
            Lambda([("x", None)], MemberAccess(IdentifierExpr("x", None), propName, None))
        )

    // Shorthand match lambda: { | pat -> expr ... }
    let shorthandMatchLambda =
        let matchArm =
            pstring "|" >>. ws >>. sepBy1 pattern (pstring "," .>> ws) .>>. (pstring "->" >>. matchArmBody)
        let matchArms = many1 (attempt (ws >>. matchArm))
        matchArms |>> (fun arms ->
            let maxArity =
                arms
                |> List.map (fun (patterns, _) -> patterns.Length)
                |> List.max
            let argNames =
                [1 .. maxArity]
                |> List.map (fun index -> ($"arg{index}", None))
            let scrutinees =
                argNames
                |> List.map (fun (name, _) -> IdentifierExpr(name, None))
            Lambda(argNames, Match(scrutinees, arms)))
    
    lambdaRef :=
        between (pstring "{") (pstring "}") (
            skipMany (skipAnyOf " \t") >>. choice [
                attempt shorthandUnaryOp
                attempt shorthandBinaryOp
                attempt shorthandPropertyAccess
                attempt shorthandMatchLambda
                attempt lambdaWithParams
                lambdaNoParams
            ] .>> skipMany (skipAnyOf " \t\r\n")
        )
        <?> "lambda expression"

// Wire up the function call parser
do 
    // Trailing lambda: function can be called with a lambda after parentheses
    // Examples: f(a, b) { x -> x + 1 } or f { x -> x + 1 } (no parens needed if only lambda arg)
    // Note: f(a, b) creates single tuple argument: FunctionCall("f", None, [TupleExpr[a; b]])

    let fieldsToExpr fields =
        match fields with
        | [PositionalField single] -> single
        | _ ->
            let allPositional = fields |> List.forall (function PositionalField _ -> true | _ -> false)
            if allPositional then
                let exprs = fields |> List.map (function PositionalField e -> e | _ -> failwith "unexpected")
                TupleExpr exprs
            else
                RecordExpr fields
    
    let callWithParensAndTrailing =
        pipe3
            qualifiedIdentifierWithRange
            (between (pstring "(") (pstring ")") 
                (opt (ws >>. (sepBy1 (ws >>. recordField) (pstring "," .>> ws) |>> fieldsToExpr) .>> ws)))
            (opt (attempt (skipMany (skipAnyOf " \t") >>. lambda)))
            (fun (name, range) exprOpt trailingLambdaOpt ->
                let baseArgs = match exprOpt with Some e -> [e] | None -> []
                match trailingLambdaOpt with
                | Some lambdaExpr -> FunctionCall(name, range, baseArgs @ [lambdaExpr])
                | None -> FunctionCall(name, range, baseArgs))
    
    let callWithOnlyTrailing =
        attempt (
            qualifiedIdentifierWithRange >>= fun (name, range) ->
                skipMany (skipAnyOf " \t") >>.
                lookAhead (pchar '{') >>.
                lambda |>> fun lambdaExpr ->
                    FunctionCall(name, range, [lambdaExpr])
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

    let defStatement = 
        let exprParser =
            attempt (blockExpr()) <|> (sepBy1 expression (pstring "," .>> ws) |>> fun exprs ->
                match exprs with
                | [single] -> single
                | multiple -> TupleExpr multiple)

        getPosition >>= fun lineStart ->
            pipe2
                (opt (attempt (pstring "export" >>. ws)))
                (parseDefWith lineStart.Column typedQualifiedIdentifier exprParser)
                (fun exportOpt (name, typeOpt, expr) -> DefStatement(exportOpt.IsSome, name, typeOpt, expr))
    
    let exprStatement =
        wsNoNl()
        >>. exprWithoutCrossingNewlines
        .>> statementTail
        |>> ExprStatement  // Consume trailing inline comments without consuming newlines
    
    statementRef := (attempt importStatement <|> attempt typeDefStatement <|> attempt defStatement <|> attempt useStatement <|> exprStatement) <?> "statement"

// Parser for module declaration
let moduleDecl: Parser<TopLevelItem, unit> =
    pstring "module" >>. ws >>. identifier |>> ModuleDecl
    <?> "module declaration"

// Parser for value definition (top-level)
let valueDef: Parser<TopLevelItem, unit> =
    let exprParser =
        attempt (blockExpr()) <|> (wsInline >>. (sepBy1 expression (pstring "," .>> ws) |>> fun exprs ->
            match exprs with
            | [single] -> single
            | multiple -> TupleExpr multiple))

    getPosition >>= fun lineStart ->
        pipe2
            (opt (attempt (pstring "export" >>. ws)))
            (parseDefWith lineStart.Column typedQualifiedIdentifier exprParser)
            (fun exportOpt (name, typeOpt, expr) -> Def(ValueDef(exportOpt.IsSome, name, typeOpt, expr)))
    <?> "value definition"

// Parser for type definition (top-level)
let typeDef: Parser<TopLevelItem, unit> =
    typeDefCore |>> Def

// Parser for top-level expressions (script-style)
let topLevelExpr: Parser<TopLevelItem, unit> =
    exprWithoutCrossingNewlines |>> Expr
    <?> "top-level expression"

let private moduleBlockItem: Parser<TopLevelItem, unit> =
    choice [
        (attempt (pstring "import" >>. wsNoNl() >>. importItems) |>> Import)
        attempt typeDef
        valueDef
        topLevelExpr
    ]

let moduleBlockDecl: Parser<TopLevelItem, unit> =
    let linePayload: Parser<TopLevelItem option, unit> =
        choice [
            attempt (skipMany (skipAnyOf " \t") >>. inlineCommentNoNl() >>% None)
            attempt (skipMany (skipAnyOf " \t") >>. (followedBy newline <|> followedBy eof) >>% None)
            moduleBlockItem |>> Some
        ]

    let lineAtIndent indent =
        attempt (manyMinMaxSatisfy indent indent (fun c -> c = ' ' || c = '\t') >>. linePayload)

    attempt (
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
    )
    <?> "module block declaration"

// Parser for top-level items
let topLevelItem: Parser<TopLevelItem, unit> =
    ws >>. choice [
        attempt moduleBlockDecl
        moduleDecl
        (attempt (pstring "import" >>. wsNoNl() >>. importItems) |>> Import)
        attempt typeDef
        valueDef
        topLevelExpr
    ] .>> ws
    <?> "top-level item"

// Parser for a complete module
let moduleParser: Parser<Module, unit> =
    ws >>. many topLevelItem .>> ws .>> eof
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

    let rec rewrite e =
        match e with
        | IdentifierExpr(name, range) -> IdentifierExpr(rewriteName name, range)
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
        | LiteralExpr _ -> e
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
let parseModule (input: string) : Result<Module, string> =
    let normalized =
        input.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd()
    match run moduleParser normalized with
    | ParserResult.Success(result, _, _) -> Result.Ok (expandMidFileModules result)
    | ParserResult.Failure(errorMsg, _, _) -> Result.Error errorMsg

// Main entry point for standalone testing
[<EntryPoint>]
let main argv =
    if argv.Length > 0 then
        let file = argv.[0]
        let text = System.IO.File.ReadAllText(file)
        
        // Debug mode: if file ends with .txt, parse as pattern
        if file.EndsWith(".txt") then
            match run (literalOrRangePattern .>> eof) text with
            | Success(pat, _, _) ->
                printfn "Pattern parsed successfully:"
                printfn "  %A" pat
                0
            | Failure(errorMsg, _, _) ->
                printfn "Pattern parse error: %s" errorMsg
                1
        else
            match parseModule text with
            | Result.Ok ast ->
                printfn "Parsed successfully:"
                ast |> List.iter (printfn "  %A")
                0
            | Result.Error err ->
                printfn "Parse error: %s" err
                1
    else
        printfn "Usage: NyxParser <file.nyx>"
        1
