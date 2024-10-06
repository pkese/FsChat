#!/usr/bin/env -S dotnet fsi --langversion:preview

#r "nuget: dotenv.net, 3.2.0"
#load "FsChat.AiApi.fsx"

open System
open FSharp.Control
open FsChat.AiApi
open FsChat.Types
open dotenv.net

do // load .env file
    let envFilePaths = [|".env"; IO.Path.Combine(__SOURCE_DIRECTORY__,"../../.env")|]
    //printfn "envFilePaths: %A" envFilePaths
    let options = DotEnvOptions(envFilePaths=envFilePaths)
    DotEnv.Load(options)

let selectedModel =
    //None
    Some LLama31_8b

let testStream() =
    taskSeq {
        let prompt = [
            User """
                Who were `Eurovision` winners in recent 10 years of `Eurovision`?
                Quote any named entity that is appearing on `Wikipedia` into backticks (as in this example).
                Complete the following 4-column `Markdown` table:
            """
            Assistant """
                | Year | Country | Artist | Song title |
                | :--- | :--- | :--- | :--- |
            """
        ]
        (*
        let prompt = [
            User """
                Spell out a list of numbers from zero to fifty in English.
            """
        ]
        *)

        let chunks = fetchStreaming (prompt |> Seq.map Prompt.toMsg, selectedModel)
        for chunk in chunks do
            match chunk with
            | Role role -> yield sprintf "\nRole: %s\n" role
            | Preamble text
            | Chunk text -> yield text
            | Finished (reason, stats) -> yield sprintf "\n\nFinished in %dms: %A, %d tokens\n" stats.durationMs reason stats.nTokens
            | Err err -> yield sprintf "\nError: %s\n" err
            do! Console.Out.FlushAsync()
    }
    |> TaskSeq.mapAsync (fun s -> task {
        do! Console.Out.WriteAsync s
        return s
    })
    |> TaskSeq.tryLast


try
    testStream().GetAwaiter().GetResult() |> printfn "last: %A"
with
| ex ->
    printfn "Exception: %s" ex.Message
    reraise()
