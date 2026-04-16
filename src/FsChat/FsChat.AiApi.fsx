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
    reasoning: string option
    content: string option
    /// note: tool_calls are also appearing in streaming form and need to be reconstructed
    /// see `extract_reasoning_and_calls` in https://docs.vllm.ai/en/v0.18.2/examples/online_serving/openai_chat_completion_tool_calls_with_reasoning/
    tool_calls: {|index:string; id:string; ``type``:string; ``function``:{|name:string option; arguments:string option|}|} list option
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
type UsageStats = {
    prompt_tokens: int
    completion_tokens: int
    total_tokens: int
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

    usage: UsageStats option
}

let prepareRequestJson streaming (completion: CompletionRequest) =
    let requestMsg = {|
        completion with
            model = completion.model.id
            messages = completion.messages
            stream = streaming
            n = 1
            //reasoning = {|effort="low"|}
            cache_salt = "123" // vLLM caching - https://github.com/vllm-project/vllm/blob/main/docs/design/prefix_caching.md
            // Qwen's thinking is specified via `chat_template_kwargs` instead of `reasoning`
            chat_template_kwargs =
                if completion.model.id.Contains("qwen", StringComparison.OrdinalIgnoreCase) then
                    match completion.think with
                    | Some thinking -> Some {| enable_thinking = thinking |}
                    | None -> Some {| enable_thinking = false |}
                else None
            stream_options =
                if streaming
                then box {| include_usage = true |} // there's also `continuous_usage_stats`
                else null
    |}
    requestMsg


let fetchStreamingCompletion =

    let client = new HttpClient()
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"))
    client.DefaultRequestHeaders.TransferEncoding.Add(new TransferCodingHeaderValue("chunked"))
    //client.DefaultRequestHeaders.AcceptEncoding.Clear()

    fun (completion: CompletionRequest) -> taskSeq {
        let model = completion.model
        try
            let url =
                if model.baseUrl.EndsWith '/' then
                    model.baseUrl + "chat/completions"
                else
                    model.baseUrl + "/chat/completions"
            use request = new HttpRequestMessage(HttpMethod.Post, url)
            request.Headers.Authorization <- new AuthenticationHeaderValue("Bearer", model.authToken())
            let requestMsg = prepareRequestJson true completion
            use content = Json.JsonContent.Create(requestMsg, options=Json.options)
            //printfn "request content: %s" (JsonSerializer.Serialize(requestMsg, Json.options))
            request.Content <- content
            request.Options.Set(new HttpRequestOptionsKey<bool>("stream"), true)
            if false then // debug
                let requestJson = JsonSerializer.Serialize(requestMsg, Json.options)
                printfn "POST %s" (request.RequestUri.ToString())
                printfn "\n%s" requestJson
            let startedTs = DateTime.UtcNow

            use! response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
            use! stream = response.Content.ReadAsStreamAsync()
            use reader = new IO.StreamReader(stream)
            let mutable tokenCtr = 0
            let mutable stats = None
            let mutable finishReason = None
            let mutable lastReportedUsage = None // actual stats from upstream

            let getUsageStats () =
                match lastReportedUsage with
                | Some usage ->
                    { stats.Value with
                        promptTokens = usage.prompt_tokens
                        completionTokens = usage.completion_tokens
                        totalTokens = usage.total_tokens
                        durationMs = int (DateTime.UtcNow - startedTs).TotalMilliseconds
                    }
                | None ->
                    { stats.Value with
                        completionTokens = tokenCtr
                        totalTokens = stats.Value.promptTokens + tokenCtr
                        durationMs = int (DateTime.UtcNow - startedTs).TotalMilliseconds
                    }
                |> fun s -> printfn "getUsageStats: %A" s; s


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
                            promptTokens = 0
                            completionTokens = 0
                            totalTokens = 0
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
                        |> Seq.map (fun delta -> // print reasoning tokens
                            // todo: we neet to move this to proper types
                            if delta.reasoning <> None || delta.tool_calls <> None || delta.content <> None then
                                tokenCtr <- tokenCtr + 1
                            delta.reasoning |> Option.iter (printf "%s")
                            if delta.tool_calls <> None then
                                failwith "Tool calls in streaming output are not supported yet"
                            delta
                        )
                        |> Seq.choose _.content
                        //|> Seq.map (fun s -> tokenCtr <- tokenCtr + 1; s)
                        |> String.concat ""
                    if text.Length > 0 then
                        //printf "%s" text; do! Console.Out.FlushAsync()
                        yield Chunk text

                    // keep last known usage
                    match chunk.usage with
                    | Some usage ->
                        lastReportedUsage <- Some usage
                        if usage.completion_tokens > 0 then
                            tokenCtr <- usage.completion_tokens
                        printfn "\nUsage update: prompt %d, completion %d, total %d tokens"
                            usage.prompt_tokens usage.completion_tokens usage.total_tokens
                    | None -> ()

                    // check if finished
                    let decodedFinishReason =
                        chunk.choices
                        |> Seq.choose _.finish_reason
                        |> Seq.tryHead
                    match decodedFinishReason with
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
                        finishReason <- Some reason
                    // Lepton on streaming requests omits finishReason, but sets usage
                    | None when chunk.usage <> None && model.provider = ApiProvider.Lepton ->
                        finishReason <- Some FinishReason.Stop
                    | None -> ()
                elif line = "" then ()
                elif line = "data: [DONE]" then ()
                else
                    yield Err $"Unexpected {model.provider} - {model.id} streaming result: `{line}`"

            match finishReason with
            | Some reason -> yield Finished (reason, getUsageStats ())
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
        //max_completion_tokens = Some 4096 // Lepton defaults to 256, Gpt4o is limited to 4096
        max_completion_tokens = None
        response_format = None
        think = None
    }

    fetchStreamingCompletion completion

type GptModel with
    member this.fetchStreaming (messages: Msg seq, ?thinking: bool) =
        let completion = {
            model = this
            messages = messages |> Seq.toArray
            user = Some "glimpse.dev"
            seed = Some 123
            stream = true
            n = 1 // stream one token at a time
            //stream_options = {| include_usage = true |}
            temperature = Some 0.0 // 0.0-1.0
            max_completion_tokens = Some 4096 // Lepton defaults to 256, Gpt4o is limited to 4096
            response_format = None
            think = thinking
        }
        fetchStreamingCompletion completion