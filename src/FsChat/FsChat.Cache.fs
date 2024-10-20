module FsChat.Cache

open System
open System.Data
open System.Text.Json
open System.Text.Json.Serialization
open FSharp.SystemTextJson
open System.Threading.Tasks
open System.Reflection
open FSharp.Reflection
open FSharp.Reflection.FSharpReflectionExtensions
open Microsoft.Data.Sqlite
open Dapper
open Dapper.FSharp
open Dapper.FSharp.SQLite

open FsChat.Types
//open FsChat.AiApi

Dapper.FSharp.SQLite.OptionTypes.register()

let dedent (s:string) =
    let lines = s.Split('\n')
    if lines.Length = 0 then s
    else
        let mutable slice = lines.AsSpan()
        while not slice.IsEmpty && slice[0].Trim() = "" do
            slice[0] <- ""
            slice <- slice.Slice(1)
        while not slice.IsEmpty && slice[slice.Length-1].Trim() = "" do
            slice[slice.Length-1] <- ""
            slice <- slice.Slice(0, slice.Length-1)
        if not slice.IsEmpty then
            let mutable minIndent = Int32.MaxValue
            for line in slice do
                if line.Trim() <> "" then
                    let indent = line.Length - line.TrimStart().Length
                    minIndent <- min minIndent indent
            for i in 0..slice.Length-1 do
                slice[i] <-
                    if slice[i].Trim() = "" then ""
                    else slice[i][minIndent..]
        String.Join("\n", lines)

let sql = dedent

let jsonSerializerOptions =
    JsonFSharpOptions.Default()
        .WithUnionTagCaseInsensitive()
        .WithUnionExternalTag()
        .WithUnionUnwrapSingleFieldCases()
        .WithSkippableOptionFields()
        .ToJsonSerializerOptions()

let tables = [

    "completions", sql """
    CREATE TABLE IF NOT EXISTS completions (
        -- request
        msgHash INT,
        tagKey TEXT,
        tagValue TEXT,
        url TEXT NOT NULL,
        model TEXT NOT NULL,
        messages TEXT NOT NULL,
        seed INT,
        temperature REAL,
        max_tokens INT,
        response_format TEXT,
        -- response
        role TEXT,
        text TEXT NOT NULL,
        -- stats
        created TEXT NOT NULL,
        actualModel TEXT NOT NULL,
        fingerprint TEXT,
        nTokens INT NOT NULL,
        durationMs INT NOT NULL
    ) STRICT
    """
    "completions_index", sql """
    CREATE INDEX IF NOT EXISTS completion_idx_hash ON completions ( msgHash );
    """
]

[<CLIMutable>]
type CompletionTable = {
    msgHash: uint64
    tagKey: string option
    tagValue: string option
    url: string
    model: string
    messages: string
    seed: int option
    temperature: float option
    max_tokens: int option
    response_format: string option
    //
    role: string option
    text: string
    created: DateTime
    actualModel: string
    fingerprint: string option
    nTokens: int
    durationMs: int
}

let completionTable = table'<CompletionTable> "completions"

[<CLIMutable>]
type CompletionSqlResponse = {
    messages: string
    temperature: float option
    role: string option
    text: string
    created: DateTime
    actualModel: string
    fingerprint: string option
    nTokens: int
    durationMs: int
}

let promptHash (prompts:Prompt seq) =
    let hash = IO.Hashing.XxHash64()
    for p in prompts do
        let s =
            match p with
            | User s -> hash.Append [|1uy|]; s
            | Assistant s -> hash.Append [|2uy|]; s
            | System s -> hash.Append [|3uy|]; s
            | Template t -> failwith "Prompts should be pre-rendered at this point"
        hash.Append(Text.Encoding.UTF8.GetBytes(s))
    hash.GetCurrentHashAsUInt64()

type SqliteCache(?dbFile:string) =
    let conn =
        let dbFile =
            dbFile
            |> Option.defaultWith (fun () ->
                match Environment.GetEnvironmentVariable("FSCHAT_CACHE") with
                | null -> "llm-cache.sqlite"
                | x -> x
            )
        let connStr = $"Data Source={dbFile}"
        let conn = new SqliteConnection(connStr)
        conn.Open()
        for reason, sqlStatement in tables do
            //printfn "SQLITE>\n%s" sqlStatement
            conn.Execute(sqlStatement) |> ignore
        conn

    interface ICompletionCache with
        member __.TryGetCompletion (key: FsChat.Types.CompletionCacheKey) = task {
            let completion = key.completion
            let! completions =
                let msgHash = promptHash key.completion.messages
                let tagKey, tagValue =
                    match key.tag with
                    | Some (k, v) -> Some k, Some v
                    | None -> None, None
                let modelName = completion.model.id
                select {
                    for c in completionTable do
                    where (
                        c.msgHash = msgHash &&
                        c.tagKey = tagKey &&
                        c.tagValue = tagValue &&
                        c.url = key.url &&
                        c.model = modelName &&
                        // c.messages // expensive: handle later
                        c.seed = completion.seed &&
                        // c.temperature = completion.temperature && // float comparison
                        c.max_tokens = completion.max_tokens
                    )
                    orderByDescending c.created
                }
                |> conn.SelectAsync<CompletionSqlResponse>
                //|> conn.SelectAsync<CompletionTable>
            return
                completions
                |> Seq.filter (fun c ->
                    match c.temperature, completion.temperature with
                    | Some t1, Some t2 -> abs (t1 - t2) < 0.000001
                    | None, None -> true
                    | _ -> false
                )
                |> Seq.filter (fun c ->
                    let msg = JsonSerializer.Deserialize<Prompt list>(c.messages, jsonSerializerOptions)
                    msg = completion.messages
                )
                |> Seq.map (fun resp ->
                    {
                        role = resp.role
                        text = resp.text
                        result = Ok (FinishReason.Cached, {
                            created = resp.created
                            requestedModel = key.completion.model.id
                            actualModel = resp.actualModel
                            fingerprint = resp.fingerprint
                            nTokens = resp.nTokens
                            durationMs = resp.durationMs
                        })
                    })
                |> Seq.tryHead

        }

        member __.PutCompletion (key:CompletionCacheKey) (resp:ChatResponse) = task {
            let completion = key.completion
            let stats =
                match resp.result with
                | Ok (FinishReason.Stop, stats) -> stats
                | Ok (otherReason, _) -> failwithf "Cannot cache response with reason %A" otherReason
                | Error _ -> failwith "Cannot cache error response"
            return
                insert {
                    into completionTable
                    value {
                        msgHash = promptHash completion.messages
                        tagKey = key.tag |> Option.map fst
                        tagValue = key.tag |> Option.map snd
                        url = key.url
                        model = key.completion.model.id
                        messages = JsonSerializer.Serialize(completion.messages, jsonSerializerOptions)
                        seed = completion.seed
                        temperature = completion.temperature
                        max_tokens = completion.max_tokens
                        response_format = completion.response_format

                        role = resp.role
                        text = resp.text
                        created = stats.created
                        actualModel = stats.actualModel
                        fingerprint = stats.fingerprint
                        nTokens = stats.nTokens
                        durationMs = stats.durationMs
                    }
                }
                |> conn.InsertAsync
        }

