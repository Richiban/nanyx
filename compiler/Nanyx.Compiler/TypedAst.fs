namespace NyxCompiler

open Parser.Program

/// Internal types used during type inference.
type TyVar =
    { Id: int
      Name: string option }

type Ty =
    | TyVar of TyVar
    | TyPrimitive of string
    | TyNominal of string * Ty * bool
    | TyFunc of Ty * Ty
    | TyRecord of Map<string, Ty>
    | TyTag of string * Ty option
    | TyApply of string * Ty list
    | TyUnion of Ty list

/// A simple typed expression placeholder for future expansion. `Arguments` are a call's arguments (a pipe's
/// include the piped value first), so a lambda passed to a call keeps its inferred type.
type TypedExpr =
    { Expr: Expression; Type: Ty; Body: TypedExpr option; Statements: TypedStatement list option; MatchArms: TypedMatchArm list option
      Arguments: TypedExpr list option }

and TypedPattern =
    { Pattern: Pattern; Type: Ty }

and TypedMatchArm = TypedPattern list * TypedExpr

and TypedStatement =
    | TypedDefStatement of bool * Identifier * TypeExpr option * TypedExpr
    | TypedExprStatement of TypedExpr
    | TypedImportStatement of ImportItem list
    | TypedTypeDefStatement of Identifier * TypeDefModifier list * (Identifier * TypeExpr option) list * TypeExpr
    | TypedUseStatement of UseBinding * TypedExpr option

type TypedDefinition =
    | TypedValueDef of bool * Identifier * TypeExpr option * TypedExpr
    | TypedTypeDef of Identifier * TypeDefModifier list * (Identifier * TypeExpr option) list * TypeExpr

type TypedTopLevelItem =
    | TypedModuleDecl of ModuleName
    | TypedImport of ImportItem list
    | TypedDef of TypedDefinition
    | TypedExprItem of TypedExpr

type TypeDefInfo = { Parameters: TyVar list; Underlying: Ty; IsPrivate: bool }

type TypedModule = { Module: Module; Types: Map<string, Ty>; TypeDefs: Map<string, TypeDefInfo>; Items: TypedTopLevelItem list }
