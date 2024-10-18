#if INTERACTIVE
// nothing
#else
[<AutoOpen>]
module FsChat.Types
#endif

open System

type ApiProvider =
    | OpenAI
    | TogetherAI
    | Groq
    | Lepton

/// unique identifier for a model
type ModelId = string

type GptModel = {
    /// get the API key
    authToken: unit -> string
    /// render the URL for the modelId
    baseUrl: string
    /// model name in URLs
    id: string
    /// descriptive name
    name: string
    provider: ApiProvider
}

module OpenAI =
    let mkApi modelId modelName =
        {
            authToken = fun () -> Environment.GetEnvironmentVariable "OPENAI_API_KEY"
            baseUrl = $"https://api.openai.com/v1/{modelId}"
            id = modelId
            name = modelName
            provider = OpenAI
        }
    let gpt4 = mkApi "gpt-4" "GPT-4"
    let gpt4o = mkApi "gpt-4o" "GPT-4o"
    let gpt4o_mini = mkApi "gpt-4o-mini" "GPT-4o Mini"
    let gpt4T = mkApi "gpt-4-turbo" "GPT-4 Turbo"
    let gpt35T = mkApi "gpt-3.5-turbo" "GPT-3.5 Turbo"
    let o1_preview = mkApi "o1-preview" "O1 Preview"
    let o1_mini = mkApi "o1-mini" "O1 Mini"



module TogetherAI =
    let mkApi modelId modelName =
        {
            authToken = fun () -> Environment.GetEnvironmentVariable "TOGETHERAI_API_KEY"
            baseUrl = $"https://api.together.xyz/v1/{modelId}"
            id = modelId
            name = modelName
            provider = TogetherAI
        }
(*
    let llama31_405b = api "meta-llama/Meta-Llama-3.1-405B-Instruct-Turbo"
    let llama31_70b = api "meta-llama/Meta-Llama-3.1-70B-Instruct-Turbo"
    let llama31_8b = api "meta-llama/Meta-Llama-3.1-8B-Instruct-Turbo"
    let llama3_70b = api "meta-llama/Llama-3-70b-chat-hf"
    let qwen2_72b_instr = api "Qwen/Qwen2-72B-Instruct"
*)
    let llama31_405b = mkApi "meta-llama/Meta-Llama-3.1-405B-Instruct-Turbo" "LLama 3.1 405B"
    let llama31_70b = mkApi "meta-llama/Meta-Llama-3.1-70B-Instruct-Turbo" "LLama 3.1 70B"
    let llama31_8b = mkApi "meta-llama/Meta-Llama-3.1-8B-Instruct-Turbo" "LLama 3.1 8B"
    let llama3_70b = mkApi "meta-llama/Llama-3-70b-chat-hf" "LLama 3 70B"
    let qwen2_72b_instr = mkApi "Qwen/Qwen2-72B-Instruct" "Qwen 2 72B Instruct"



module Groq =
    let api modelId modelName =
        {
            authToken = fun () -> Environment.GetEnvironmentVariable "GROQ_API_KEY"
            baseUrl = $"https://api.groq.com/openai/v1/{modelId}"
            id = modelId
            name = modelName
            provider = Groq
        }
    let llama31_70b = api "llama-3.1-70b-versatile" "LLama 3.1 70B"
    let llama31_405b = api "llama-3.1-405b-reasoning" "LLama 3.1 405B"

module LeptonAI =
    let api modelId modelName =
        {
            authToken = fun () -> Environment.GetEnvironmentVariable "LEPTON_API_KEY"
            baseUrl = $"https://{modelId}.lepton.run/api/v1"
            id = modelId
            name = modelName
            provider = Lepton
        }
    let llama31_70b = api "llama3-1-70b" "LLama 3.1 70B"
    let llama31_405b = api "llama3-1-405b" "LLama 3.1 405B"

type Prompt =
    | System of string
    | User of string
    | Assistant of string
    /// content is a template containing "text {{title}} blah {{text}}" with values being spliced in from the context
    | Template of Prompt


type FinishReason =
    | Stop
    | Length
    | ContentFilter
    | ToolCalls
    | Cached
    | Other of string

type GptStats = {
    created: DateTime
    requestedModel: string
    actualModel: string
    fingerprint: string option
    nTokens: int
    durationMs: int
}

type GptChunk =
    | Role of string
    /// first response chunk (internally generated) containing table header
    | Preamble of string
    | Chunk of string
    | Finished of reason:FinishReason * stats: GptStats
    | Err of string

