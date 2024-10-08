#if INTERACTIVE
#load "FsChat.AiApi.fsx"
#else
namespace FsChat
#endif

open System
open System.Text
open System.Threading.Tasks
open FSharp.Control
open FsChat.Types
open FsChat.AiApi

type IChatRenderer =
    abstract member Create: unit -> (GptChunk -> unit)

//type ChunkRenderer = unit -> GptChunk -> unit

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

type ChatResponse = {
    role: string option
    text: string
    result: Result<FinishReason*GptStats, string>
} with
    member this.IsSuccess = this.result |> Result.isOk

module Chat =
    /// <summary>Default renderer used by <see cref="Chat"/> instances</summary>
    /// <remarks>Can be replaced by setting <c>Chat.defaultRenderer <- NoRenderer()</c></remarks>
    /// <remarks>`FsChat.Interactive` replaces this with <see cref="NotebookRenderer"/>NotebookRenderer</see></remarks>
    let mutable defaultRenderer : IChatRenderer = StdoutRenderer()

/// <summary>Chat model</summary>
/// <param name="model">GPT model to use</param>
/// <param name="renderer">IChatRenderer to use (see <see cref="NotebookRenderer"/>NotebookRenderer</see>)</param>
/// <param name="context">Initial chat prompt context, e.g. <c>[ System "You're a helpful assistant" ]</c></param>
type Chat(?model:GptModel, ?renderer:IChatRenderer, ?context: Prompt seq) =

    let mutable ctx = context |> Option.map List.ofSeq |> Option.defaultValue []
    let mutable gptModel = model |> Option.orElseWith (fun () -> Some Gpt4o_mini)
    let mutable chunkRenderer : IChatRenderer = defaultArg renderer Chat.defaultRenderer

    let fetchGpt(prompts) =
        let render = chunkRenderer.Create()
        taskSeq {
            let chunks = fetchStreaming (prompts |> Seq.map Prompt.toMsg, gptModel)
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
        |> fun (results) -> task {
            let! results = results
            match results with
            | [] -> return { role = None; text = ""; result = Error "No results" }
            | [r] ->
                //printfn "Result: %A" r.result
                match r.result with
                | Ok _ -> ctx <- (prompts @ [ Assistant r.text ])
                | Error err -> ()
                return r
            | results -> return { role = None; text = ""; result = Error $"Expected a single result, got %d{results.Length}: %A{results}" }
        }

    let fetch prompts =
        try
            let result = (fetchGpt prompts).GetAwaiter().GetResult()
            result
        with
        | ex -> { role = None; text = ""; result = Error (ex.ToString()) }

    member this.send(text:string) =
        fetch (ctx @ [User text])
    member this.send(prompts: Prompt seq) =
        fetch [ yield! ctx; yield! prompts ]

    member this.clear() = ctx <- []

    member this.context with get() = ctx
    member this.setContext(c) = ctx <- c
    member this.setRenderer(r) = chunkRenderer <- r
    /// deletes last interaction (your last prompt plus assistant's last response)
    member this.undo() =
        let rec loop = function
            | [] -> []
            | User _ :: Assistant _ :: [] -> []
            | User _ :: [] -> []
            | h::t -> h::loop t
        ctx <- loop ctx

    member this.model with get() = gptModel.Value and set(m) = gptModel <- Some m


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