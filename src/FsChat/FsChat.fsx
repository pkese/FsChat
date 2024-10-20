#if INTERACTIVE
#r "nuget: TypeShape, 10.0.0"
//#r "nuget: System.Text.Json, 9.0.0-rc.2.24473.5"
#r "nuget: FSharp.SystemTextJson, 1.3.13"
#load "FsChat.Types.fsx" "FsChat.AiApi.fsx" "FsChat.Markdown.fsx"
#else
namespace FsChat
#endif

open System
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
//open System.Text.Json.Schema
open System.Runtime.InteropServices
open FSharp.SystemTextJson
open System.Threading.Tasks
open FSharp.Control
open FsChat.Types
open FsChat.AiApi

[<AutoOpen>]
module Renderers =

    type IChatRenderer =
        abstract member Create: unit -> (GptChunk -> unit)

    type StdoutRenderer() =
        let formatChunk = function
            | Role role -> sprintf "\nRole: %s\n" role
            | Preamble text
            | Chunk text -> text
            | Finished (reason, stats) -> sprintf "\n\nFinished in %.2fs: `%A` @ %d tokens\n" (float stats.durationMs/1000.0) reason stats.nTokens
            | Err err -> sprintf "\nError: %s\n" err

        interface IChatRenderer with
            member this.Create() =
                fun chunk ->
                    formatChunk chunk
                    |> Console.Out.Write

    type NoRenderer() =
        interface IChatRenderer with
            member this.Create() = fun _ -> ()

[<AutoOpen>]
module ResponseExtensions =
    // add extensions to parse tables
    type ChatResponse with
        member this.IsSuccess = this.result |> Result.isOk
        member this.Tables with get() =
            this.text
            |> FsChat.Markdown.parse
            |> FsChat.Markdown.getTables

        member this.ParseTableAs<'T>() : 'T =
            this.Tables
            |> List.last
            |> FsChat.TableReader.parseTableAs<'T>

type Prompt =
    | System of string
    | User of string
    | Assistant of string
    | Temperature of float
    | MaxTokens of int
    | Seed of int
    | ResponseFormat of string
    | Clear
    | Undo of int
    //| ResponseType of <'T>

module Prompt =
    let cleanContent (s:string) =
        let mutable lines = s.Replace("\r", "").Split("\n")
        while lines.Length>0 && lines[0].Trim() = "" do
            lines <- lines[1..]
        while lines.Length>0 && lines[lines.Length-1].Trim() = "" do
            lines <- lines[..lines.Length-2]
        let countSpaces (line:string) =
            let rec cnt i =
                if i < line.Length && line.[i] = ' ' then
                    cnt (i+1)
                else
                    i
            cnt 0
        let unindentSize = lines |> Seq.map countSpaces |> Seq.min
        if unindentSize > 0 then
            lines <- [| for l in lines -> l[unindentSize..] |]
        if lines[lines.Length-1].EndsWith('|') then
            lines[lines.Length-1] <- lines[lines.Length-1] + "\n"
        lines |> String.concat "\n"

    let toMsg = function
        | System s -> { role = Role.system; content = cleanContent s }
        | User s -> { role = Role.user; content = cleanContent s }
        | Assistant s -> { role = Role.assistant; content = cleanContent s }
        | x -> failwithf "%A is not a message" x
    let ofMsg = function
        | { role=Role.system; content=s } -> System s
        | { role=Role.user; content=s } -> User s
        | { role=Role.assistant; content=s } -> Assistant s


[<AutoOpen>]
// static defaults
module Chat =
    /// <summary>Default renderer used by <see cref="Chat"/> instances</summary>
    /// <remarks>Can be replaced by setting <c>Chat.defaultRenderer <- NoRenderer()</c></remarks>
    /// <remarks>`FsChat.Interactive` replaces this with <see cref="NotebookRenderer"/>NotebookRenderer</see></remarks>
    let mutable defaultRenderer : IChatRenderer = StdoutRenderer()
    let mutable defaultCache : ICompletionCache option = None
    let mutable defaultApiUserName : string option = None

/// <summary>Chat model</summary>
/// <param name="model">GPT model to use</param>
/// <param name="renderer">IChatRenderer to use (see <see cref="NotebookRenderer"/>NotebookRenderer</see>)</param>
/// <param name="context">Initial chat prompt context, e.g. <c>[ System "You're a helpful assistant" ]</c></param>
type Chat(?model:GptModel, ?renderer:IChatRenderer, ?prompt: Prompt seq, ?apiUserName:string) as this =

    let mutable gptModel = model |> Option.defaultValue OpenAI.gpt4o_mini
    let mutable chunkRenderer : IChatRenderer = defaultArg renderer Chat.defaultRenderer
    let mutable cache = Chat.defaultCache
    let mutable _seed = Option<int>.None
    let mutable _max_tokens = Option<int>.None
    let mutable _temperature = Some 0.0
    let mutable _responseFormat = None
    let mutable ctx = ResizeArray<Msg>() // filled in later

    let fetchGpt(msgs: Msg[]) = task {
        let render = chunkRenderer.Create()
        //let messages = prompts |> Seq.map Prompt.toMsg
        let completionRq = {
            model = gptModel
            messages = msgs
            user = apiUserName
            seed = _seed
            stream = true
            n = 1
            temperature = _temperature
            max_tokens = _max_tokens
            response_format = _responseFormat
        }
        let cacheKey = { url=gptModel.baseUrl; tag=None; completion=completionRq }
        let! resp = task {
            let! cached =
                match cache with
                | Some c -> c.TryGetCompletion cacheKey
                | None -> Task.FromResult None
            match cached with
            | Some resp ->
                render (Chunk resp.text)
                return [ resp ]
            | None ->
                return!
                    taskSeq {
                        let chunks = fetchStreamingCompletion completionRq
                        let text = StringBuilder()
                        let mutable role = None

                        let buildResponse result = {
                            role = role
                            text = text.ToString()
                            result = result
                        }
                        for chunk in chunks do
                            render chunk |> ignore
                            match chunk with
                            | Role r -> role <- Some r
                            | Preamble s
                            | Chunk s -> text.Append(s) |> ignore
                            | Finished (reason, stats) -> yield buildResponse (Ok(reason, stats))
                            | Err err -> yield buildResponse (Error err)
                    }
                    |> TaskSeq.toListAsync
        }
        // clear ephemeral state
        _responseFormat <- None

        match resp with
        | [] -> return { role = None; text = ""; result = Error "No results" }
        | [r] ->
            //printfn "Result: %A" r.result
            match r.result with
            | Ok (reason,stats) ->
                ctx.Add { role=Role.assistant; content=r.text }
                match cache, reason with
                | Some cache, FinishReason.Stop -> do! cache.PutCompletion cacheKey r
                | _ -> ()
            | Error err -> ()
            return r
        | results -> return { role = None; text = ""; result = Error $"Expected a single result, got %d{results.Length}: %A{results}" }
    }

    let apply op =
        match op with
        | System _
        | User _
        | Assistant _ -> ctx.Add (op |> Prompt.toMsg)
        | Temperature t -> _temperature <- Some t
        | MaxTokens t -> _max_tokens <- Some t
        | Seed s -> _seed <- Some s
        | ResponseFormat s -> _responseFormat <- Some s
        | Clear -> ctx.Clear()
        | Undo n -> this.undo(n)

    let applyOps ops = for op in ops do apply op

    do
        prompt
        |> Option.defaultValue Seq.empty
        |> applyOps

    member internal this.Fetch (prompts:ResizeArray<Msg>) =
        try
            let result = (fetchGpt (prompts.ToArray())).GetAwaiter().GetResult()
            result
        with
        | ex -> { role = None; text = ""; result = Error (ex.ToString()) }

    member this.send(text: string) =
        apply (User text)
        this.Fetch ctx
    member this.send(op: Prompt) =
        apply op
        this.Fetch ctx
    member this.send(msgs: Msg seq) =
        ctx.AddRange msgs
        this.Fetch ctx
    member this.send(ops: Prompt seq) =
        applyOps ops
        this.Fetch ctx

    member this.temperature with set(t:float) = _temperature <- Some t
    member this.max_tokens with set(t:int) = _max_tokens <- Some t
    member this.seed with set(s:int) = _seed <- Some s
    member this.response_format with set (json:string) = _responseFormat <- Some json

(*
    member this.response_format with set (typ:Type) =
        let jsonSerializerOptions = JsonSerializerOptions.Default;
        JsonFSharpOptions.Default()
            .WithUnionTagCaseInsensitive()
            .WithUnionExternalTag()
            .WithUnionUnwrapSingleFieldCases()
            .WithSkippableOptionFields()
            .AddToJsonSerializerOptions(jsonSerializerOptions)

        let schema = jsonSerializerOptions.GetJsonSchemaAsNode(typ)
        responseFormat <- Some (schema.ToString(Prompt.Assistant
*)
    /// clear all previous messages
    member this.clear([<Optional; DefaultParameterValue(false)>] keepInitialSystemPrompt:bool) =
        if keepInitialSystemPrompt then
            let nSystemMsgs = ctx |> Seq.takeWhile (fun m -> m.role = Role.system) |> Seq.length
            ctx.RemoveRange(nSystemMsgs, ctx.Count-nSystemMsgs)
        else
            ctx.Clear()

    member this.messages with get() = ctx
    member this.setRenderer(r) = chunkRenderer <- r
    /// deletes last interaction (your last prompt plus assistant's last response)
    member this.undo([<Optional; DefaultParameterValue(1)>] nInteractions:int) =
        while ctx.Count>0 && ctx[ctx.Count-1].role = Role.assistant do
            ctx.RemoveAt(ctx.Count-1)
        while ctx.Count>0 && ctx[ctx.Count-1].role = Role.user do
            ctx.RemoveAt(ctx.Count-1)
        if nInteractions>1 then this.undo(nInteractions-1)

    member this.model with get() = gptModel and set(m) = gptModel <- m

    member this.parseTableAs<'T>() : 'T =
        ctx
        |> Seq.choose (function { role=Role.assistant; content=text } -> Some text  | _ -> None)
        |> Seq.last
        |> FSharp.Formatting.Markdown.Markdown.Parse
        |> FsChat.Markdown.getTables
        |> List.last
        |> FsChat.TableReader.parseTableAs<'T>


(*
//let chat = Chat(Gpt4o)
#load "chat.fsx"
open Chat
open Shared.Gpt
let chat = Chat(Gpt4o_mini)
//let chat = Chat(LLama31_70b)
chat.send [
    System """
        You're a helpful assistant.
        You respond in Markdown (or Markdown code blocks) unless instructed otherwise.
        Depending on content, render responses as Markdown tables where applicable.
        Skip politeness phrases or excuses at the beginning of responses. Start directly with the main content.
    """
    User """
        Who were the winners in recent 12 years of `Eurovision` contest?
        Render any named entity that is appearing on `Wikipedia` in italic.
        Mark any year when the contest wasn't held with "N/A" + reason.

        Start response with a short title,
        Add a single line of explanation e.g. tell who was the most recent winner.
        then render a Markdown table consisting of following columns:
        | Year | Country | Artist | Song title |
    """
];;
*)
