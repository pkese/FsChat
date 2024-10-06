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

type ModelName = string

type GptApiConfig = {
    authToken: string
    baseUrl: ModelName -> string
}

let gptApi = function
    | OpenAI ->
        {
            authToken = Environment.GetEnvironmentVariable "OPENAI_API_KEY"
            baseUrl = fun _ -> "https://api.openai.com/v1"
        }
    | TogetherAI ->
        {
            authToken = Environment.GetEnvironmentVariable "TOGETHERAI_API_KEY"
            baseUrl = fun _ -> "https://api.together.xyz/v1"
        }
    | Groq ->
        {
            authToken = Environment.GetEnvironmentVariable "GROQ_API_KEY"
            baseUrl = fun _ -> "https://api.groq.com/openai/v1"
        }
    | Lepton ->
        {
            authToken = Environment.GetEnvironmentVariable "LEPTON_API_KEY"
            baseUrl = sprintf "https://%s.lepton.run/api/v1"
        }

type GtpModelInfo = {
    model: ModelName
    tokens: int
    api: GptApi
} with
    member this.AuthToken = (gptApi this.api).authToken
    member this.BaseUrl = (gptApi this.api).baseUrl this.model

let gptModel = function
    | Gpt4 ->       { tokens =   8192; api = OpenAI; model = "gpt-4" }
    | Gpt4o ->      { tokens = 131072; api = OpenAI; model = "gpt-4o" }
    | Gpt4o_mini -> { tokens = 131072; api = OpenAI; model = "gpt-4o-mini" }
    | Gpt4T ->      { tokens = 131072; api = OpenAI; model = "gpt-4-turbo" }
    | Gpt35T ->     { tokens =  16385; api = OpenAI; model = "gpt-3.5-turbo" }
    // Groq
    //| LLama31_405b -> { tokens =  131072; api = Groq; model = "llama-3.1-405b-reasoning" }
    | LLama31_70b -> { tokens =  131072; api = Groq; model = "llama-3.1-70b-versatile" }
    // TogetherAI
    //| LLama31_405b -> { tokens =  8192; api = TogetherAI; model = "meta-llama/Meta-Llama-3.1-405B-Instruct-Turbo" }
    //| LLama31_70b -> { tokens =  16384; api = TogetherAI; model = "meta-llama/Meta-Llama-3.1-70B-Instruct-Turbo" }
    | LLama31_8b -> { tokens =  16384; api = TogetherAI; model = "meta-llama/Meta-Llama-3.1-8B-Instruct-Turbo" }
    | LLama3_70b -> { tokens =   8192; api = TogetherAI; model = "meta-llama/Llama-3-70b-chat-hf" }
    | Qwen2_72b_instr -> { tokens =  131072; api = TogetherAI; model = "Qwen/Qwen2-72B-Instruct" }
    // Lepton
    //| LLama31_70b -> { tokens = 131072; api = Lepton; model="llama3-1-70b" }
    | LLama31_405b -> { tokens = 131072; api = Lepton; model="llama3-1-405b" }

let defaultModel = LLama31_70b

let llmConfig =
    gptModel defaultModel
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


type PromptMsg = {
    role: string
    content: string
}

type Prompt
with
    static member cleanContent (s:string) =
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

    static member toMsg = function
        | System s -> { role = "system"; content = Prompt.cleanContent s }
        | User s -> { role = "user"; content = Prompt.cleanContent s }
        | Assistant s -> { role = "assistant"; content = Prompt.cleanContent s }
        | Template f -> failwith "Prompts should be pre-rendered at this point"
    static member renderTemplate (ctx:Map<string,string>) = function
        | Template f ->
            let replaceString (s:string) =
                ctx |> Map.fold (fun (s:string) k v -> s.Replace($"{{{{{k}}}}}", v)) s
            match f with
            | System s -> System (replaceString s)
            | User s -> User (replaceString s)
            | Assistant s -> Assistant (replaceString s)
            | Template t -> Prompt.renderTemplate ctx t
        | s -> s



let completionRequest model (prompt: PromptMsg seq) = {|
    model = model |> gptModel |> _.model
    messages = prompt
    user = "glimpse.dev"
    seed = 123
    stream = true
    n = 1 // stream one token at a time
    //stream_options = {| include_usage = true |}
    temperature = 0.0 // 0.0-1.0
    max_tokens = 4096 // Lepton defaults to 256, Gpt4o is limited to 4096
|}

let fetchStreaming =

    let client = new HttpClient()
    client.DefaultRequestHeaders.Authorization <- new AuthenticationHeaderValue("Bearer", llmConfig.AuthToken)
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"))
    client.DefaultRequestHeaders.TransferEncoding.Add(new TransferCodingHeaderValue("chunked"))
    //client.DefaultRequestHeaders.AcceptEncoding.Clear()

    fun (messages: PromptMsg seq, model: GptModel option) -> taskSeq {
        let model = model |> Option.defaultValue defaultModel
        let modelSpec = gptModel model
        try
            use request = new HttpRequestMessage(HttpMethod.Post, $"{modelSpec.BaseUrl}/chat/completions")
            request.Headers.Authorization <- new AuthenticationHeaderValue("Bearer", modelSpec.AuthToken)
            let requestMsg = messages |> completionRequest model
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
                            requestedModel = model |> gptModel |> _.model
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
                    | None when chunk.usage <> None && modelSpec.api = Lepton ->
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
                    yield Err $"Unexpected {modelSpec.api} streaming result: `{line}`"

            match finished with
            | Some f -> yield f
            | None -> yield Err "Stream ended without completion"
        with
        | ex -> yield Err (sprintf "Exception: %s" ex.Message)
    }
