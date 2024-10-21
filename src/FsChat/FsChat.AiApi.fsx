#if INTERACTIVE
#r "nuget: FSharp.SystemTextJson, 1.3.13"
#r "nuget: FSharp.Control.TaskSeq"
#load "FsChat.Types.fsx"
#else
module FsChat.AiApi
#endif

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text.Json
open System.Text.Json.Serialization
open FSharp.SystemTextJson
open FSharp.Control
open FsChat.Types


let defaultModel = OpenAI.gpt4o

let llmConfig =
    OpenAI.gpt4o
    //gptModel Gpt4o

module Json =
    open System.Text.Encodings.Web;
    open System.Text.Json;
    open System.Text.Unicode;

    let options =
        JsonFSharpOptions.Default()
            // Add any .WithXXX() calls here to customize the format
            .WithAllowNullFields()
            .WithUnionUnwrapFieldlessTags()
            //.WithSkippableOptionFields()
            .WithSkippableOptionFields(SkippableOptionFields.Always, deserializeNullAsNone = true)
            .ToJsonSerializerOptions()
    options.AllowTrailingCommas <- true
    options.PropertyNamingPolicy <- JsonNamingPolicy.SnakeCaseLower
    // avoid escaping characters that can be expressed as valid UTF8
    options.Encoder <- JavaScriptEncoder.UnsafeRelaxedJsonEscaping



(* chat completion chunk

Headers:
Content-Type: text/event-stream
Tranfer-Encoding: chunked

Body:
data: {"id":"chatcmpl-123","object":"chat.completion.chunk","created":1694268190,"model":"gpt-3.5-turbo-0125", "system_fingerprint": "fp_44709d6fcb", "choices":[{"index":0,"delta":{"role":"assistant","content":""},"logprobs":null,"finish_reason":null}]}

data: {"id":"chatcmpl-123","object":"chat.completion.chunk","created":1694268190,"model":"gpt-3.5-turbo-0125", "system_fingerprint": "fp_44709d6fcb", "choices":[{"index":0,"delta":{"content":"Hello"},"logprobs":null,"finish_reason":null}]}

data: {"id":"chatcmpl-123","object":"chat.completion.chunk","created":1694268190,"model":"gpt-3.5-turbo-0125", "system_fingerprint": "fp_44709d6fcb", "choices":[{"index":0,"delta":{},"logprobs":null,"finish_reason":"stop"}]}
*)


[<CLIMutable>]
type ChoiceContent = {
    role: string option
    content: string option
    tool_calls: {|index:string; id:string; ``type``:string; ``function``:{|name:string; arguments:string|}|} list option
}

[<CLIMutable>]
type Choice = {
    //index: int // not available on TogetherAI
    delta: ChoiceContent option // when steaming
    message: ChoiceContent option // when not streaming
    //logprobs: obj
    finish_reason: string option
}

[<CLIMutable>]
type ChatCompletionChunk = {
    /// List of completion choices, usually only one, empty at the EOS
    choices: Choice list

    // Completion id (same for all chunks in a single request)
    id: string
    // on OpenAI, TogetherAI, Groq: always "chat.completion.chunk"
    // on Lepton: missing
    ``object``: string option
    // Unix timestamp of when the completion was created (they are all the same)
    created: int option
    // Name of the model used
    model: string
    /// System fingerprint (changes when they update the model, etc.)
    /// (Together.ai & Lepton may not have it)
    system_fingerprint: string option

    usage: {|
        completion_tokens: int
        prompt_tokens: int
        total_tokens: int
    |} option
}

let fetchStreamingCompletion =

    let client = new HttpClient()
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"))
    client.DefaultRequestHeaders.TransferEncoding.Add(new TransferCodingHeaderValue("chunked"))
    //client.DefaultRequestHeaders.AcceptEncoding.Clear()

    fun (completion: CompletionRequest) -> taskSeq {
        let model = completion.model
        try
            use request = new HttpRequestMessage(HttpMethod.Post, $"{model.baseUrl}/chat/completions")
            request.Headers.Authorization <- new AuthenticationHeaderValue("Bearer", model.authToken())
            let requestMsg = {|
                completion with
                    model = model.id
                    messages = completion.messages
                    stream = true
                    n = 1
            |}
            use content = Json.JsonContent.Create(requestMsg, options=Json.options)
            request.Content <- content
            request.Options.Set(new HttpRequestOptionsKey<bool>("stream"), true)
            if false then // debug
                let requestJson = JsonSerializer.Serialize(requestMsg, Json.options)
                printfn "Request: %s" requestJson
            let startedTs = DateTime.UtcNow

            use! response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
            use! stream = response.Content.ReadAsStreamAsync()
            use reader = new IO.StreamReader(stream)
            let mutable tokenCtr = 0
            let mutable stats = None
            let mutable finished = None
            while not reader.EndOfStream do //&& not finished do
                let! line = reader.ReadLineAsync()
                //printfn "> %s" line
                if line.StartsWith("data: {") then
                    let json = line.AsSpan().Slice(6)
                    let chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(json, Json.options)

                    if stats.IsNone then
                        stats <- Some {
                            created =
                                match chunk.created with
                                | Some ts -> DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime
                                | None -> startedTs
                            requestedModel = model.id
                            actualModel = chunk.model
                            fingerprint = chunk.system_fingerprint
                            nTokens = 0
                            durationMs = 0
                        }

                    // extract roles
                    let roles =
                        chunk.choices
                        |> Seq.choose (fun choice -> choice.message |> Option.bind _.role)
                    for role in roles do
                        yield Role role

                    // extract text
                    let text =
                        chunk.choices
                        |> Seq.choose _.delta
                        |> Seq.choose _.content
                        |> Seq.map (fun s -> tokenCtr <- tokenCtr + 1; s)
                        |> String.concat ""
                    if text.Length > 0 then
                        //printf "%s" text; do! Console.Out.FlushAsync()
                        yield Chunk text

                    // check if finished
                    let finishReason =
                        chunk.choices
                        |> Seq.choose _.finish_reason
                        |> Seq.tryHead
                    match finishReason with
                    | Some null -> ()
                    | Some reason ->
                        let reason =
                            match reason with
                            | "stop" // OpenAI, TogetherAI (Llama3_70b)
                            | "eos"  // TogetherAI (Llama31...)
                                -> FinishReason.Stop
                            | "length" -> FinishReason.Length
                            | "content_filter" -> FinishReason.ContentFilter
                            | "tool_calls" -> FinishReason.ToolCalls
                            | s -> FinishReason.Other s
                        let stats' = {
                            stats.Value with
                                nTokens = tokenCtr
                                durationMs = int (DateTime.UtcNow - startedTs).TotalMilliseconds
                        }
                        finished <- Some (Finished (reason, stats'))
                    // Lepton on streaming requests omits finishReason, but sets usage
                    | None when chunk.usage <> None && model.provider = ApiProvider.Lepton ->
                        let usage = chunk.usage.Value
                        let stats' = {
                            stats.Value with
                                nTokens = usage.total_tokens
                                durationMs = int (DateTime.UtcNow - startedTs).TotalMilliseconds
                        }
                        finished <- Some (Finished (FinishReason.Stop, stats'))
                    | None -> ()
                elif line = "" then ()
                elif line = "data: [DONE]" then ()
                else
                    yield Err $"Unexpected {model.provider} - {model.id} streaming result: `{line}`"

            match finished with
            | Some f -> yield f
            | None -> yield Err "Stream ended without completion"
        with
        | ex -> yield Err (sprintf "Exception: %s" ex.Message)
    }

let fetchStreaming (messages: Msg seq, model: GptModel option) =
    let model = model |> Option.defaultValue defaultModel
    let completion = {
        model = model
        messages = messages |> Seq.toArray
        user = Some "glimpse.dev"
        seed = Some 123
        stream = true
        n = 1 // stream one token at a time
        //stream_options = {| include_usage = true |}
        temperature = Some 0.0 // 0.0-1.0
        max_completion_tokens = Some 4096 // Lepton defaults to 256, Gpt4o is limited to 4096
        response_format = None
    }

    fetchStreamingCompletion completion
