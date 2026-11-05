namespace NyxCompiler

open System.Collections.Generic

/// Substitution map for type variables.
type Subst = Map<int, Ty>

module Unifier =
    let private tryTupleLikeRecord (fields: Map<string, Ty>) : Ty list option * (string * Ty) list =
        let tupleFields, namedFields =
            fields
            |> Map.toList
            |> List.fold (fun (tupleAcc, namedAcc) (name, ty) ->
                match System.Int32.TryParse(name) with
                | true, index when index >= 0 ->
                    ((index, ty) :: tupleAcc, namedAcc)
                | _ -> (tupleAcc, (name, ty) :: namedAcc)) ([], [])
        let orderedTuple = tupleFields |> List.sortBy fst
        let expectedIndices = [0 .. orderedTuple.Length - 1]
        let hasAllIndices =
            orderedTuple
            |> List.map fst
            |> fun indices -> indices = expectedIndices
        if hasAllIndices && orderedTuple.Length > 0 then
            let tupleItems = orderedTuple |> List.map snd
            Some tupleItems, namedFields
        else
            None, namedFields

    /// `ty` as it would be written in an annotation. A function or union is bracketed wherever it would otherwise
    /// run into what surrounds it: as a function's input (`(a -> b) -> c`, since `->` groups to the right), as a
    /// union that is a function's result (`a -> (#some(a) | #none)`), as a member of a union, and wherever items are
    /// separated by commas (`(int, (a -> b))`).
    let rec tyToString (ty: Ty) : string =
        // A function or union, bracketed.
        let grouped ty =
            match ty with
            | TyFunc _
            | TyUnion _ -> $"({tyToString ty})"
            | _ -> tyToString ty
        match ty with
        // The unit type is written `()`; `unit` is only its internal name.
        | TyPrimitive "unit" -> "()"
        | TyPrimitive name -> name
        | TyVar v ->
            match v.Name with
            | Some name -> $"'{name}"
            | None -> $"'t{v.Id}"
        | TyNominal(name, _, _) -> name
        | TyFunc(arg, ret) ->
            // `->` groups to the right, so a function result needs no brackets; a union result does.
            let retText =
                match ret with
                | TyUnion _ -> grouped ret
                | _ -> tyToString ret
            $"{grouped arg} -> {retText}"
        | TyRecord fields ->
            match tryTupleLikeRecord fields with
            | Some tupleItems, namedFields ->
                let tupleText =
                    tupleItems
                    |> List.map grouped
                    |> String.concat ", "
                let namedText =
                    namedFields
                    |> List.sortBy fst
                    |> List.map (fun (name, fieldTy) -> $"{name}: {grouped fieldTy}")
                    |> String.concat ", "
                if namedText = "" then
                    $"({tupleText})"
                elif tupleText = "" then
                    $"({namedText})"
                else
                    $"({tupleText}, {namedText})"
            | None, _ ->
                fields
                |> Map.toList
                |> List.map (fun (name, fieldTy) -> $"{name}: {grouped fieldTy}")
                |> String.concat ", "
                |> fun inner -> $"{{ {inner} }}"
        // Tags are written with their `#`, as in source: `#some(int) | #notFound`.
        | TyTag(name, payloadOpt) ->
            match payloadOpt with
            | Some payload -> $"#{name}({tyToString payload})"
            | None -> $"#{name}"
        | TyApply(name, [ arg ]) -> $"{name}({tyToString arg})"
        | TyApply(name, args) ->
            let argsText = args |> List.map grouped |> String.concat ", "
            $"{name}({argsText})"
        | TyUnion items ->
            items
            |> List.map grouped
            |> String.concat " | "

    /// The type variables of `ty` in the order `tyToString` prints them, each once. A nominal's underlying type
    /// is not printed, so its variables are not included.
    let private varsInPrintOrder (ty: Ty) : TyVar list =
        let rec collect ty =
            match ty with
            | TyVar v -> [ v ]
            | TyPrimitive _
            | TyNominal _ -> []
            | TyFunc(arg, ret) -> collect arg @ collect ret
            | TyRecord fields ->
                match tryTupleLikeRecord fields with
                | Some tupleItems, namedFields ->
                    (tupleItems |> List.collect collect) @ (namedFields |> List.sortBy fst |> List.collect (snd >> collect))
                | None, _ -> fields |> Map.toList |> List.collect (snd >> collect)
            | TyTag(_, payload) -> payload |> Option.map collect |> Option.defaultValue []
            | TyApply(_, args) -> args |> List.collect collect
            | TyUnion items -> items |> List.collect collect
        collect ty |> List.distinctBy (fun v -> v.Id)

    let private mismatchMessage left right =
        $"Type mismatch: {tyToString left} vs {tyToString right}."

    let private tagUnionLengthMessage leftCount rightCount left right =
        $"Tag union length mismatch: {leftCount} vs {rightCount}. {mismatchMessage left right}"

    let private recordKeyMessage leftKeys rightKeys left right =
        let leftKeyList = leftKeys |> Set.toList |> List.sort |> String.concat ", "
        let rightKeyList = rightKeys |> Set.toList |> List.sort |> String.concat ", "
        $"Record field mismatch: {{{leftKeyList}}} vs {{{rightKeyList}}}. {mismatchMessage left right}"

    let private keySet (fields: Map<string, Ty>) =
        fields |> Map.keys |> Set.ofSeq

    let private ensureTagUnion items =
        items
        |> List.forall (function
            | TyTag _ -> true
            | TyVar _ -> true
            | _ -> false)


    /// A union's rest variable stands for more tags: `#some(a) | r` is never a `string`. (Unions and
    /// nominals are unwrapped before a lone type is matched against a union, so a tag is all that is left.)
    let private isTagLike ty =
        match ty with
        | TyTag _ -> true
        | _ -> false

    let private tagUnionKey ty =
        match ty with
        | TyTag(name, _) -> name
        | _ -> tyToString ty

    let private normalizeUnionItems (items: Ty list) =
        items
        |> List.fold (fun acc ty ->
            let key = tagUnionKey ty
            if acc |> Map.containsKey key then acc else acc |> Map.add key ty) Map.empty

    let private parseTagUnion items =
        if ensureTagUnion items then
            let mutable restVar: TyVar option = None
            let tagMap =
                items
                |> List.choose (function
                    | TyTag _ as tag -> Some tag
                    | TyVar v ->
                        match restVar with
                        | None ->
                            restVar <- Some v
                            None
                        | Some _ ->
                            restVar <- restVar
                            None
                    | _ -> None)
                |> normalizeUnionItems
            Ok (tagMap, restVar)
        else
            Error "Only tag unions are supported"

    let empty : Subst = Map.empty

    let rec apply (subst: Subst) (ty: Ty) : Ty =
        match ty with
        | TyVar v ->
            match subst |> Map.tryFind v.Id with
            | Some replacement when replacement = ty -> ty
            | Some replacement -> apply subst replacement
            | None -> ty
        | TyNominal(name, underlying, isPrivate) -> TyNominal(name, apply subst underlying, isPrivate)
        | TyFunc(arg, ret) -> TyFunc(apply subst arg, apply subst ret)
        | TyRecord fields ->
            fields |> Map.map (fun _ v -> apply subst v) |> TyRecord
        | TyTag(name, payload) -> TyTag(name, payload |> Option.map (apply subst))
        | TyApply(name, args) -> TyApply(name, args |> List.map (apply subst))
        // A rest variable solved to a union (`r` := `#notFound`) is spliced into the union it is the rest of.
        | TyUnion items ->
            items
            |> List.collect (fun item ->
                match apply subst item with
                | TyUnion inner -> inner
                | applied -> [ applied ])
            |> TyUnion
        | TyPrimitive _ -> ty

    /// `ty` for a reader: type variables the compiler made up are renamed `'a`, `'b`, ... in the order they appear,
    /// skipping names already taken; variables named in an annotation keep their names.
    let tyToDisplayString (ty: Ty) : string =
        let vars = varsInPrintOrder ty
        let taken = vars |> List.choose (fun v -> v.Name) |> Set.ofList
        let candidates =
            Seq.initInfinite (fun index -> if index < 26 then string (char (int 'a' + index)) else $"t{index}")
            |> Seq.filter (fun name -> not (taken.Contains name))
        let unnamed = vars |> List.filter (fun v -> v.Name.IsNone)
        // Each made-up variable is replaced by a primitive that prints as its new name.
        let names =
            Seq.zip unnamed candidates
            |> Seq.map (fun (v, name) -> v.Id, TyPrimitive $"'{name}")
            |> Map.ofSeq
        tyToString (apply names ty)

    let rec occurs (varId: int) (ty: Ty) : bool =
        match ty with
        | TyVar v -> v.Id = varId
        | TyNominal(_, underlying, _) -> occurs varId underlying
        | TyFunc(arg, ret) -> occurs varId arg || occurs varId ret
        | TyRecord fields -> fields |> Map.exists (fun _ v -> occurs varId v)
        | TyTag(_, payload) -> payload |> Option.exists (occurs varId)
        | TyApply(_, args) -> args |> List.exists (occurs varId)
        | TyUnion items -> items |> List.exists (occurs varId)
        | TyPrimitive _ -> false

    let unify (constraints: ConstraintSet) : Result<Subst, string> =
        let rec loop (subst: Subst) (remaining: ConstraintSet) =
            let unifyRecordFields (lFields: Map<string, Ty>) (rFields: Map<string, Ty>) rest left right =
                let lKeys = keySet lFields
                let rKeys = keySet rFields
                if Set.isSubset lKeys rKeys || Set.isSubset rKeys lKeys then
                    let commonKeys = Set.intersect lKeys rKeys
                    let pairs =
                        commonKeys
                        |> Seq.map (fun key -> (lFields.[key], rFields.[key]))
                        |> Seq.toList
                    let pairConstraints = pairs |> List.map (fun (lTy, rTy) -> (lTy, rTy, Equal))
                    loop subst (pairConstraints @ rest)
                else
                    Error (recordKeyMessage lKeys rKeys left right)
            let unifyEqual l r rest =
                match l, r with
                | TyVar v, ty
                | ty, TyVar v ->
                    if occurs v.Id ty then
                        Error $"Occurs check failed: {tyToString ty} contains 't{v.Id}."
                    else
                        let newSubst = subst |> Map.add v.Id ty
                        loop newSubst rest
                | TyPrimitive lName, TyPrimitive rName when lName = rName ->
                    loop subst rest
                | TyApply(lName, lArgs), TyApply(rName, rArgs) when lName = rName && lArgs.Length = rArgs.Length ->
                    let pairs = List.zip lArgs rArgs
                    let pairConstraints = pairs |> List.map (fun (lTy, rTy) -> (lTy, rTy, Equal))
                    loop subst (pairConstraints @ rest)
                | TyNominal(lName, lUnderlying, lPrivate), TyNominal(rName, rUnderlying, rPrivate) ->
                    if lName = rName then
                        if lPrivate || rPrivate then
                            loop subst rest
                        else
                            loop subst ((lUnderlying, rUnderlying, Equal) :: rest)
                    else
                        Error (mismatchMessage l r)
                | TyNominal(_, _, true), _
                | _, TyNominal(_, _, true) ->
                    Error (mismatchMessage l r)
                | TyNominal(_, underlying, false), _ ->
                    loop subst ((underlying, r, Equal) :: rest)
                | _, TyNominal(_, underlying, false) ->
                    loop subst ((l, underlying, Equal) :: rest)
                | TyFunc(lArg, lRet), TyFunc(rArg, rRet) ->
                    loop subst ((lArg, rArg, Equal) :: (lRet, rRet, Equal) :: rest)
                | TyRecord lFields, TyRecord rFields ->
                    unifyRecordFields lFields rFields rest l r
                | TyTag(lName, lPayload), TyTag(rName, rPayload) when lName = rName ->
                    match lPayload, rPayload with
                    | None, None -> loop subst rest
                    | Some lTy, Some rTy -> loop subst ((lTy, rTy, Equal) :: rest)
                    | _ -> Error $"Tag payload mismatch: #{lName}. {mismatchMessage l r}"
                | TyUnion lItems, TyUnion rItems ->
                    match parseTagUnion lItems, parseTagUnion rItems with
                    | Ok (lMap, lRest), Ok (rMap, rRest) ->
                        let lKeys = lMap |> Map.keys |> Set.ofSeq
                        let rKeys = rMap |> Map.keys |> Set.ofSeq
                        let extraLeft = Set.difference lKeys rKeys
                        let extraRight = Set.difference rKeys lKeys
                        let commonKeys = Set.intersect lKeys rKeys
                        let pairs =
                            commonKeys
                            |> Seq.map (fun key -> (lMap.[key], rMap.[key]))
                            |> Seq.toList
                            |> List.map (fun (lTy, rTy) -> (lTy, rTy, Equal))
                        let restConstraints =
                            [ match lRest with
                              | Some restVar when not extraRight.IsEmpty ->
                                  let extraTags = extraRight |> Seq.map (fun key -> rMap.[key]) |> Seq.toList
                                  yield (TyVar restVar, TyUnion extraTags, Equal)
                              | _ -> ()
                              match rRest with
                              | Some restVar when not extraLeft.IsEmpty ->
                                  let extraTags = extraLeft |> Seq.map (fun key -> lMap.[key]) |> Seq.toList
                                  yield (TyVar restVar, TyUnion extraTags, Equal)
                              | _ -> ()
                              match lRest, rRest with
                              | Some lVar, Some rVar -> yield (TyVar lVar, TyVar rVar, Equal)
                              | _ -> () ]
                        if lRest.IsNone && rRest.IsNone then
                            if extraLeft.IsEmpty || extraRight.IsEmpty then
                                loop subst (pairs @ rest)
                            else
                                Error (tagUnionLengthMessage lMap.Count rMap.Count l r)
                        else if (lRest.IsSome || extraRight.IsEmpty) && (rRest.IsSome || extraLeft.IsEmpty) then
                            loop subst (pairs @ restConstraints @ rest)
                        else
                            Error (tagUnionLengthMessage lMap.Count rMap.Count l r)
                    | Error message, _ -> Error message
                    | _, Error message -> Error message
                | TyUnion lItems, ty ->
                    match parseTagUnion lItems with
                    | Ok (lMap, lRest) ->
                        let tags = lMap |> Map.toList |> List.map snd
                        // A member of the union that unifies with `ty` is tried against the rest of the
                        // constraints too, so when none works the failure may come from later on; report
                        // the innermost failure rather than a bare mismatch here.
                        let rec tryItems items (lastError: string option) =
                            match items with
                            | [] ->
                                match lRest with
                                | Some restVar when isTagLike ty -> loop subst ((TyVar restVar, ty, Equal) :: rest)
                                | _ -> Error (lastError |> Option.defaultValue (mismatchMessage l r))
                            | item :: restItems ->
                                match loop subst ((item, ty, Equal) :: rest) with
                                | Ok result -> Ok result
                                | Error inner -> tryItems restItems (Some inner)
                        tryItems tags None
                    | Error message -> Error message
                | ty, TyUnion rItems ->
                    match parseTagUnion rItems with
                    | Ok (rMap, rRest) ->
                        let tags = rMap |> Map.toList |> List.map snd
                        let rec tryItems items (lastError: string option) =
                            match items with
                            | [] ->
                                match rRest with
                                | Some restVar when isTagLike ty -> loop subst ((ty, TyVar restVar, Equal) :: rest)
                                | _ -> Error (lastError |> Option.defaultValue (mismatchMessage l r))
                            | item :: restItems ->
                                match loop subst ((ty, item, Equal) :: rest) with
                                | Ok result -> Ok result
                                | Error inner -> tryItems restItems (Some inner)
                        tryItems tags None
                    | Error message -> Error message
                | _ -> Error (mismatchMessage l r)
            match remaining with
            | [] -> Ok subst
            | (left, right, kind) :: rest ->
                let l = apply subst left
                let r = apply subst right
                if l = r then
                    loop subst rest
                else
                    match kind with
                    | Assignable ->
                        match l, r with
                        | TyVar _, _
                        | _, TyVar _ ->
                            unifyEqual l r rest
                        | _ ->
                        match l, r with
                        | TyNominal(lName, _, _), TyNominal(rName, _, _) when lName = rName ->
                            loop subst rest
                        | TyNominal(_, _, true), _
                        | _, TyNominal(_, _, true) ->
                            Error (mismatchMessage l r)
                        | TyNominal(_, underlying, false), other when not (match other with TyNominal _ -> true | _ -> false) ->
                            loop subst ((underlying, other, Equal) :: rest)
                        | other, TyNominal(_, _, false) when not (match other with TyNominal _ -> true | _ -> false) ->
                            Error (mismatchMessage l r)
                        | TyNominal _, TyNominal _ ->
                            Error (mismatchMessage l r)
                        | _ ->
                            unifyEqual l r rest
                    | Equal ->
                        unifyEqual l r rest
        loop empty constraints
