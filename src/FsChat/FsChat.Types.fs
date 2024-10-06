[<AutoOpen>]
module FsChat.Types

open System

type GptApi =
    | OpenAI
    | TogetherAI
    | Groq
    | Lepton

type GptModel =
    | Gpt35T
    | Gpt4
    | Gpt4T
    | Gpt4o
    | Gpt4o_mini
    | LLama31_405b
    | LLama31_70b
    | LLama31_8b
    | LLama3_70b
    | Qwen2_72b_instr
with
    static member all = [
        LLama31_405b; LLama31_70b; LLama31_8b; LLama3_70b; Gpt4o; Gpt4o_mini; Gpt4T; Gpt4; Gpt35T
        Qwen2_72b_instr
    ]
    /// given gpt string name, return the matching model
    static member fromApi = function
        | "gpt-3.5-turbo" -> Some Gpt35T
        | "gpt-4" -> Some Gpt4
        | "gpt-4-turbo" -> Some Gpt4T
        | "gpt-4o" -> Some Gpt4o
        | "gpt-4o-mini" -> Some Gpt4o_mini
        // TogetherAI
        | "meta-llama/Llama-3-70b-chat-hf" -> Some LLama3_70b
        | "meta-llama/Meta-Llama-3.1-70B-Instruct-Turbo" -> Some LLama31_70b
        | "meta-llama/Meta-Llama-3.1-8B-Instruct-Turbo" -> Some LLama31_8b
        | "meta-llama/Meta-Llama-3.1-405B-Instruct-Turbo" -> Some LLama31_405b
        | "Qwen/Qwen2-72B-Instruct" -> Some Qwen2_72b_instr
        // Groq
        | "llama-3.1-405b-reasoning" -> Some LLama31_405b
        | "llama-3.1-70b-versatile" -> Some LLama31_70b
        // Lepton
        | "llama3-1-405b" -> Some LLama31_405b
        | "llama3-1-70b" -> Some LLama31_70b
        | _ -> None



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

