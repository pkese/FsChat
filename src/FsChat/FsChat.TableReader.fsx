#if INTERACTIVE
#r "nuget: TypeShape, 10.0.0"
#else
module FsChat.TableReader
#endif

open System
open FSharp.Reflection
open TypeShape.Core

type FldName = string
type ColName = string

(*

    given:

        type MyRecord = { name: string; age: int; email: string; phone: string }

    provides a function:

        parseTableAs<MyRecord list> : TableData -> MyRecord list

    that will parse a Markdown table.

    To match the table columns to the record fields, we use 'edit distance'.

*)
    
type TableData = {
    headers: ColName list
    rows: string list list
}

module private Parsing =

    let rec mkParser<'T> (fldNm: string) : (string -> obj) =
        match shapeof<'T> with
        | Shape.FSharpOption o ->
            o.Element.Accept {
                new ITypeVisitor<string -> obj> with
                    member _.Visit<'a> () =
                        let mapper = mkParser<'a> fldNm
                        fun (s:string) ->
                            match s.Trim() with
                            | "" | "N/A" | "N / A" | "/" | "-" | "--" -> box None
                            | s ->
                                let x = mapper s
                                x |> unbox<'a> |> Some |> box
            }
        | Shape.Bool -> Convert.ToBoolean >> box
        | Shape.Byte -> Convert.ToByte >> box
        | Shape.Char -> Convert.ToChar >> box
        | Shape.DateTime -> Convert.ToDateTime >> box
        | Shape.Decimal -> Convert.ToDecimal >> box
        | Shape.Double -> Convert.ToDouble >> box
        | Shape.Single -> Convert.ToSingle >> box
        | Shape.Int16 -> Convert.ToInt16 >> box
        | Shape.Int32 -> Convert.ToInt32 >> box
        | Shape.Int64 -> Convert.ToInt64 >> box
        | Shape.SByte -> Convert.ToSByte >> box
        | Shape.String -> box
        | Shape.UInt16 -> Convert.ToUInt16 >> box
        | Shape.UInt32 -> Convert.ToUInt32 >> box
        | Shape.UInt64 -> Convert.ToUInt64 >> box
        | Shape.Array s ->
            s.Element.Accept {
                new ITypeVisitor<string -> obj> with
                    member _.Visit<'a> () =
                        let mapper = mkParser<'a> fldNm >> unbox<'a>
                        fun (s:string) ->
                            s.Split([|',';';'|]) |> Array.map mapper |> box
            }
        | Shape.FSharpList s ->
            s.Element.Accept {
                new ITypeVisitor<string -> obj> with
                    member _.Visit<'a> () =
                        let mapper = mkParser<'a> fldNm >> unbox<'a>
                        fun (s:string) ->
                            [ for txt in s.Split([|',';';'|]) -> mapper txt ] |> box
            }
        | _ -> failwithf "Unsupported type %A for field %s" (shapeof<'T>) fldNm

    let mkMemberReader (fldName:string) (shape : IShapeMember<'DeclaringType>) =
        shape.Accept { new IMemberVisitor<'DeclaringType, string -> obj> with
            member _.Visit (shape : ShapeMember<'DeclaringType, 'Field>) =
                let fldParser = mkParser<'Field>(fldName)
                fldParser }


/// Try to map field names to column names, returning a list of (column name, field name) pairs
/// Use edit-distance algorithm to find the best match for each field name
let rec private renderFieldMapping (colNames:ColName list) (_fldNames:FldName[]) =
    let fldMap =
        Map.ofArray [| for i,fld in Array.indexed _fldNames -> fld, i |]
    let spaces = Set.ofSeq " -_.,"
    let editDisance (a:string) (b:string) =
        let m = a.Length
        let n = b.Length
        let d = Array2D.zeroCreate (m + 1) (n + 1)
        for i in 0..m do d[i, 0] <- i
        for j in 0..n do d[0, j] <- j
        for j in 1..n do
            for i in 1..m do
                let cost =
                    let x, y = a[i-1], b[j-1]
                    if x = y then 0
                    elif Char.ToUpper x = Char.ToUpper y then 1
                    elif spaces.Contains x && spaces.Contains y then 1
                    elif spaces.Contains x || spaces.Contains y then 2
                    else 10
                d[i, j] <-
                    min
                        (min (d[i - 1, j] + 1) (d[i, j - 1] + 1))
                        (d[i - 1, j - 1] + cost)
        d[m, n]

    let rec mapToIndex (fldMap: Map<FldName,int>) (colNames:ColName list) =
        match colNames with
        | [] when not fldMap.IsEmpty ->
            let remaining = String.Join(", ", Map.keys fldMap)
            failwithf "Fields could not be mapped to table columns: %s" remaining
        | [] -> []
        | colName :: rest when fldMap.IsEmpty ->
            // nothing else to map, return zeros
            (colName,None) :: mapToIndex fldMap rest
        | colName :: rest when fldMap.ContainsKey colName ->
            let idx = fldMap.[colName]
            (colName,Some (colName,idx)) :: mapToIndex (fldMap.Remove colName) rest
        | colName :: rest ->
            let bestMatch =
                fldMap
                |> Seq.minBy (fun kv -> editDisance kv.Key colName)
            let fldName, idx = bestMatch.Key, bestMatch.Value
            (colName,Some (fldName,idx)) :: mapToIndex (fldMap.Remove fldName) rest
    mapToIndex fldMap colNames


let readTableRecords<'T> (table: TableData) =
    match shapeof<'T> with
    | Shape.FSharpRecord (:? ShapeFSharpRecord<'T> as r) ->
        let flds = r.Fields
        let col2fld = renderFieldMapping table.headers [| for f in flds -> f.Label |]
        let scratchpad : obj[] = Array.zeroCreate flds.Length
        let colParsers =
            col2fld
            |> List.map (fun (colName, fld) ->
                match fld with
                | Some (fldName, idx) ->
                    let fldShape = r.Fields[idx]
                    let fldParser = Parsing.mkMemberReader fldName fldShape
                    fun (cell:string) ->
                        scratchpad[idx] <- fldParser cell
                | None -> fun _ -> ())

        seq {
            for row in table.rows do
                // populate scratchpad with values
                (colParsers, row) ||> List.iter2 (fun parser cell -> parser cell)
                FSharpValue.MakeRecord(typeof<'T>, scratchpad) :?> 'T
        }
    | _ -> failwithf "Unsupported type %A" (shapeof<'T>)

let parseTableAs<'T>(table: TableData) : 'T =
    match shapeof<'T> with
    | Shape.FSharpList s ->
        s.Element.Accept {
            new ITypeVisitor<'T> with
                member _.Visit<'R> () =
                    let records = readTableRecords<'R> table
                    [ for r in records -> r ] |> box :?> 'T
        }
    | Shape.Array s ->
        s.Element.Accept {
            new ITypeVisitor<'T> with
                member _.Visit<'R> () =
                    let records = readTableRecords<'R> table
                    [| for r in records -> r |] |> box :?> 'T
        }
    | _ ->
        let typ = typeof<'T>.Name
        failwithf $"Can't parse table into <{typ}>: did you want <{typ}[]> or <{typ} list>?"
