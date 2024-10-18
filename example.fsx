#!/usr/bin/env -S dotnet fsi --langversion:preview

#r "nuget: dotenv.net, 3.2.0"
#load "src/FsChat/FsChat.fsx"
// or #r "nuget: FsChat 0.1.0-beta1"

open dotenv.net
open FsChat
open FsChat.Types

DotEnv.Load(DotEnvOptions(envFilePaths=[".env"]))

let chat = Chat(OpenAI.gpt4o_mini)

let resp = chat.send [
    User """
        Who were `Eurovision` winners in recent 10 years of `Eurovision`?
        Quote any named entity that is appearing on `Wikipedia` into backticks (as in this example).
        Complete the following 4-column `Markdown` table:
    """
    Assistant """
        | Year | Country | Artist | Song title |
        | :--- | :--- | :--- | :--- |
    """
    // we're trying here if it will complete the started table
    // (render just the data rows as instructed)
    // or if it will start a new table with the same headers
]

printfn "\nResult: %A" resp.result
