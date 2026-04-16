# FsChat <small>- interact with LLM Chat APIs using F#</small>

**FsChat** is a small F# library for streaming chat-style LLM interactions.

It currently supports:
- F# scripts (`.fsx`)
- normal F# projects (`.fsproj`)

It does **not** support Polyglot Notebooks / `DotNet.Interactive` / `.ipynb` anymore.

## Features

- stream responses to stdout as tokens arrive
- keep and edit chat context in `chat.messages`
- cache API responses in SQLite while iterating on prompts
- parse Markdown tables into typed F# records
- choose between `StdoutRenderer()`, `NoRenderer()`, or your own `IChatRenderer`

## Quick start (`.fsx`)

1. Get an API key from a supported provider:
   - [OpenAI](https://platform.openai.com/settings/profile?tab=api-keys)
   - [TogetherAI](https://api.together.xyz/settings/api-keys)
   - [Groq](https://console.groq.com/keys)
   - [LeptonAI](https://dashboard.lepton.ai/)

2. Put your keys into `.env` (or copy from [.env.example](.env.example)):

```sh
OPENAI_API_KEY="<your-openai-api-key-here>"
TOGETHERAI_API_KEY=...
GROQ_API_KEY=...
LEPTON_API_KEY=...
```

3. Create a script like this:

```fsharp
#!/usr/bin/env -S dotnet fsi --langversion:preview

#r "nuget: FsChat, 0.1.0-beta2"
#r "nuget: dotenv.net, 3.2.0"

open dotenv.net
open FsChat

DotEnv.Load(DotEnvOptions(envFilePaths=[ ".env" ]))

let chat = Chat(OpenAI.gpt4o_mini)

let response = chat.send [
    System """
        You're a helpful assistant that writes concise Markdown.
        Skip pleasantries and go directly to the answer.
    """
    User """
        Who were the winners of Eurovision since 2019?
        Render the answer as a Markdown table with columns:
        | Year | Country | Artist | Song title |
    """
    Temperature 0.0
    MaxTokens 1200
]

printfn "\n---\nFinal text:\n%s" response.text
```

`chat.send` streams output while the model is responding and also returns the final `ChatResponse`.

## Using FsChat from a normal project

Add the package reference in your `.fsproj` or with the CLI:

```sh
dotnet add package FsChat --version 0.1.0-beta2
```

Then use it the same way as in the script example.

## Parsing Markdown tables into records

If the model returns a Markdown table, you can parse it into your own type:

```fsharp
type EurovisionWinner = {
    year: int
    country: string
    artist: string option
    song: string option
}

let winners = response.ParseTableAs<EurovisionWinner[]>()
```

Notes:
- table column names do not have to match record field names exactly
- the parser uses Levenshtein distance to find the closest field match
- values like `N/A`, `N / A`, `/`, `-`, `--` map to `None` for option fields

## Context state: `messages`

Each `Chat` instance keeps a mutable conversation history in `chat.messages`.

```fsharp
chat.messages -> [
  { role = system; content = "You're a helpful assistant" }
  { role = user; content = "Say a random number" }
  { role = assistant; content = "42" }
]
```

Useful operations:

```fsharp
chat.clear()          // clear all history
chat.undo()           // remove last user+assistant interaction
chat.undo(2)          // remove last 2 interactions
chat.setRenderer(NoRenderer())
```

You can also set defaults globally:

```fsharp
Chat.defaultRenderer <- StdoutRenderer()
// or
Chat.defaultRenderer <- NoRenderer()
```

## Response caching

To reduce API calls while refining prompts, set `FSCHAT_CACHE` in `.env`:

```sh
FSCHAT_CACHE="llm-cache.sqlite"
```

FsChat will create a small SQLite database and reuse matching responses.
This also makes outputs reproducible even when no explicit random seed is set.

If you want to disable caching in code:

```fsharp
Chat.defaultCacheProvider <- fun () -> None
```

## Response object

`chat.send` returns a `ChatResponse` with:

- `text: string` — final generated text
- `result: Result<status * statistics, error_text>` — response metadata
- `IsSuccess` — convenience property
- `Tables` — parsed Markdown tables found in the response
- `ParseTableAs<'T>()` — parse the last table into a type

## Renderers

FsChat ships with two renderers:

- `StdoutRenderer()` — default, writes streamed chunks to console
- `NoRenderer()` — suppresses output completely

If you need different behavior, implement `IChatRenderer` yourself.

## Multi-agent example

```fsharp
let agent1 = Chat(model=OpenAI.gpt4o_mini, prompt=[
    System """
        You're playing the 20 questions game.
        Ask yes-or-no questions to guess the animal.
    """
])

let agent2 = Chat(model=OpenAI.gpt4o_mini, prompt=[
    System """
        You're playing the 20 questions game.
        Answer only with yes or no.
        The animal is: parrot.
    """
])

let rec play timesLeft (text:string) =
    if timesLeft = 0 then
        printfn "Game over"
    else
        let guess = agent1.send(text).text
        let assess = agent2.send(guess).text
        if assess.Contains "CORRECT." then
            ()
        else
            play (timesLeft - 1) assess
```

## Development

For local experimentation, see [example.fsx](./example.fsx).

Basic commands:

```sh
dotnet restore
dotnet build
```

## TODO

- [ ] report missing/unconfigured API keys
- [ ] document and improve `Prompt.ResponseFormat`
- [ ] add cache tags to sqlite records
- [ ] add cache usage statistics
- [ ] extract code snippets from markdown frames
- [ ] parameterize `parseTableAs` values that map to `None`
- [ ] dedent prompts
- [ ] improve Mermaid-related prompting examples
- [ ] parse JSON
- [ ] add C# support
- [ ] write tests
