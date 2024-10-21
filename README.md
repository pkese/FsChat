

# FsChat <small>- interact with LLM Chat APIs using F#</small>

**FsChat** focuses on interactivity and usability:
- it shows straming responses in real time as they arrive,
- it can render Markdown and Mermaid diagrams for you,
- it makes it easy to interact with GPT agents programmatically.

## Usage: 1,2,3,4

1) Register for an account with [OpenAI](https://platform.openai.com/settings/profile?tab=api-keys), [TogetherAI](https://api.together.xyz/settings/api-keys), [Groq](https://console.groq.com/keys) or [LeptonAI](https://dashboard.lepton.ai/)  
   to get an API key.

2) Write your API key it in `.env` file (or copy it from [.env.example](.env.example)).

```sh
# example .env file
OPENAI_API_KEY="<your-openai-api-key-here>"
TOGETHERAI_API_KEY=...
GROQ_API_KEY=...
LEPTON_API_KEY=...
```
3) Load **FsChat**:  
- if you're writing a command line **.fsx** script or a normal **.fsproj** project, then reference `FsChat` package,
- if you're using Dotnet Interactive (Polyglot) notebooks, then reference `FsChat.Interactive` package.    


```fsharp
#r "nuget: FsChat.Interactive, 0.1.0-beta2"
#r "nuget: dotenv.net, 3.2.0"
open dotenv.net
DotEnv.Load(DotEnvOptions(envFilePaths=[ ".env" ]))
open FsChat
```
4) Choose a GPT model and start a chat session:
```fsharp
let chat = Chat(OpenAi.gpt4o_mini)
let response = chat.send [
    System """
        You're a helpful assistant that renders responses in Markdown.
        Don't include politeness phrases or excuses at the beginning of responses,
        skip directly to content.
    """
    User """
        Who were the winners of Eurovision contest since 2010?
        Mark any occasion when the contest wasn't held with "N/A" + reason.

        Start response with a short title,
        add a line explaining who was the most recent winner,
        then render a table consisting of following columns:
        | Year | Country | Artist | Song title |
    """
]
```
If you have loaded **FsChat.Interactive** into a Dotnet Interactive (Polyglot) notebook session, then you should see live Markdown text previews:

![fschat-table](https://github.com/user-attachments/assets/773eb721-0d6f-4026-b2b6-15be9c743a78)

### Extracting Markdown tables into user defined types

Define a record type T and use  
`response.parseTableAs<T[]>()` or  
`response.parseTableAs<T list>()`  
to extract the Markdown table contents into an array (or list) of records:

```fsharp
type EurovisionWinner = {
    year: int
    country: string
    artist: string option
    song: string option
}

response.parseTableAs<EurovisionWinner[]>()
// ...returns
[|
  { year = 2023; country = "Sweden"; artist = Some "Loreen"; song = Some "Tattoo" }
  { year = 2022; country = "Ukraine"; artist = Some "Kalush Orchestra"; song = Some "Stefania" }
  { year = 2021; country = "Italy"; artist = Some "Måneskin"; song = Some "Zitti e buoni" }
  { year = 2020; country = "No Contest"; artist = None; song = None }
  { year = 2019; country = "Netherlands"; artist = Some "Duncan Laurence"; song = Some "Arcade" }
  ...
|]
```
Alternatively, if you didn't store the response into a variable, you can use `chat.parseTableAs<T>()`, which will parse the last response in the chat context.

Notice:  
1) Column names in the table and record field names don't need to match exactly:  
    in the above example, table column `Song Title` is automatically mapped into `song` field of record.  
    The parser uses Levenshtein edit distance to find the closest match.
2) Cell values `N/A`, `N / A`, `/`, `-`, `--` are automatically mapped to `None` in the record.  
    It is suggested to explicitely instruct LLM to write one of these values for missing data in the table.

### Mermaid charts

You can ask LLM to analyze the above table and render it as Mermaid chart:
````fsharp
chat.send [
    Model OpenAI.gpt4o // switch to a more powerful model for this task; gpt4o_mini is not very good at rendering charts
    User """
        Given the above table, render a Mermaid directed graph with:
        - nodes: countries that won the contest (labeled with country name)
        - edges: time sequence of wins representing how the 'trophy' moved from one country to another (labeled with year)

        Example:
        ```mermaid
        %%{init: {'theme': 'default', 'themeVariables': { 'fontSize': '10px' }}}%%
        graph LR
            DK[Denmark]
            AT[Austria]
            DK -->|2014| AT
        ```
        Make sure each country appears exactly once in the graph:
        if a country won the competition multiple times, then the country's node should have multiple incoming and outgoing edges.
    """
    Temperature 0.0  // set LLM temperature to 0.0 to avoid hallucinations
    Seed 42          // set a fixed random seed to make responses reproducible
    MaxTokens 5000   // limit number of output tokens
]
````
Hint:  
you'll get better results, if you prompt it with a short example of expected chart code.
![fschat-chart](https://github.com/user-attachments/assets/0ba9a21d-1694-4299-a99b-9e36d9aa2498)


## Context state: `messages`

Each chat agent maintains a context of previous interactions with LLM.  
This context is stored in `chat.messages`:

```fsharp
chat.messages -> [
  { role: system;    content: "You're a helpful assistant" }
  { role: user;      content: "Say a random number" }
  { role: assistant; content: "Sure! How about 42?" }
  { role: user;      content: "Why did you say 42?" }
  { role: assistant; content: "Because it’s the answer to the ultimate question of life, the universe, and everything" }
]
```
When you `chat.send` a prompt, both your prompt as well as LLM response are added to context.

Context can be accessed using the `chat.messages` property (it returns a C# List / F# ResizeArray of previous interactions like the example above)
and it can be freely modified.

Context can also be cleared with `chat.clear()` or alternatively you can delete just the last interaction (your last `User` prompt plus GPT's response) with `chat.undo()`.

## Response caching

If you wish to reduce the amount of LLM API calls while testing your prompts,  
you can enable API response caching by setting `FSCHAT_CACHE` variable in your `.env` to a file path of your choice:

```sh
FSCHAT_CACHE="llm-cache.sqlite"
```

FsChat will create and initialize a small single-file SQLite database at the specified path on your computer  
and start storing API responses in it.

Each time a LLM API call with the same prompt configuration is made, FsChat will return response from cache.

Notice that this will make your responses reproducible even if LLM random seed is not set to a fixed value.

**Warning:** Jupyter notebook rendering may not work correctly with response caching enabled.

## Agent interaction

The result of each call to `chat.send` is a `Response` record with:
- `text: string` containing the response generated by GPT,
- `result: Result<status*statistics, error_text>` some response metadata.



### Multi-agent example

Below is an example of instatiating 2 chat agents  
and making them playin the 20 questions game against one another  
by accessing `response.text`.

```fsharp
let agent1 = Chat(model=OpenAI.gpt4o_mini, prompt=[
    System """
        You're playing the 20 questions game.
        Your role is to ask 'Is it a ...' questions with Yes-or-No answers
        in order to narrow down your options and guess the word.
        ----
        Hint: You're guessing an animal.
    """
  ])

let agent2 = Chat(model=OpenAI.gpt4o_mini, prompt=[
    System """
        You're playing the 20 questions game.
        Your role is the one who thinks of a word and responds to my questions
        with simple "yes" or "no" answers (no additional text)
        Once I guess the correct word, respond with “CORRECT.”
        ----
        The word I'll be trying to guess is: parrot.
    """
])

let rec play timesLeft (text:string) =
    if timesLeft=0 then
        printfn "Game over"
    else
        let quess = agent1.send(text).text
        let assess = agent2.send(quess).text
        if assess.Contains "CORRECT." then
            ()
        else
            play (timesLeft-1) assess

play 20 "Which word is it? Ask the first question."
```

![fschat-dialog](https://github.com/user-attachments/assets/b5f6f9e8-bf75-4ea8-9d3f-74addfca4331)

The above animation contains some fancy HTML/CSS formatting. Look at [dialog.ipynb](docs/dialog.ipynb) for more details and read about how to customize live output rendering below.  
Note: *unfortunately, GitHub's .ipynb renderer won't show colored bubbles: they are there but GitHub doesn't show them.*

## Choosing what kind of output do you want to see

There is an interface called `IChatRenderer` with three implementations:
- `StdoutRenderer()` is the default for **FsChat** and renders live outputs to console.
- `NotebookRenderer()` is the default for **FsChat.Interactive** and renders live outputs as HTML to Dotnet Interactive (Polyglot) notebooks.
- `NoRenderer()` is a dummy renderer that doesn't output anything. Choose this if you're writing non-interactive apps.

There are multiple ways of specifying a renderer:
```fsharp
// passing it as a parameter to Chat constructor
let chat = Chat(Gpt4o_mini, renderer=StdoutRenderer())

// setting it on an existing Chat instance
chat.setRenderer(StdoutRenderer())

// or setting it globally.
// This will cause all new Chat instances to use this renderer by default.
Chat.defaultRenderer <- StdoutRenderer()

// note: `#r FsChat.Interactive` sets it to NotebookRenderer()
// otherwise it defaults to StdoutRenderer().
```

### Customizing the NotebookRenderer

NotebookRenderer accepts two optional parameters:
- `props` a string with HTML tag attributes that will be added to the output div element,
- `css` a string containing CSS stylesheet that will be injected into html.

Behind the scenes `props` and `css` get inserted into HTML as follows:
```html
<div class='chat-response'>
    {{css}}
    <div {{props}}>
        <p>GPT rendered Markdown response</p>
    </div>
</div>
```
As you can see, `css` should contain full HTML tags, e.g.
- `<style>...</style>` tag, or
- `<link rel='stylesheet' href='...'>`.



#### Example
```fsharp
let greenHeaderRenderer = NotebookRenderer(
    props = "class='my-class'",
    css = "<style>.my-class th { color: #080; }</style>"
)
let chat = Chat(Gpt4o, renderer=greenHeaderRenderer)
```
![image](https://github.com/user-attachments/assets/5a2ffa11-7960-484f-a11e-433aaf6b625e)

See [dialog.ipynb](docs/dialog.ipynb) for sample code.

# Problems & Caveats

### Rendered content does not show when opening .ipynb files

Dotnet.Interactive (Polyglot) notebooks inside **Visual Studio Code** sometimes won't load content.  
Notebook files when opened may (or will?) lose (or delete) rendered content.  
*(Can someone test if full-blown Visual Studio on Windows behaves any better?)*

### All new cells are created as C# by default

Dotnet.Interactive (Polyglot) notebooks inside Visual Studio Code can occasionally change notebook's default language to C#.  
You need to open your .ipynb file with a text editor, scroll all the way down and set:
```json
"polyglot_notebook": {
   "kernelInfo": {
    "defaultKernelName": "fsharp",
   }
}
```



# Development

Note: this an early beta release, there will be API changes in the future,
particularly in the naming and specifying GptModels.

For developing and modifying this library outside of Dotnet Interactive (Polyglot) notebooks, look at [example.fsx](./example.fsx).

For fiddling with Dotnet Interactive (Polyglot) notebooks:  
> Todo:  
> Figure out how to [checkout the code](https://github.com/pkese/FsChat) and
`#load "src/FsChat/FsChat.Chat.fsx"`
into the notebook context.  
> Currently the FsChat from Nuget gets registered as default FsChat library and Chat class.


# TODO
- [x] improve GptModel configuration (-beta2)
  - [x] simplify customization
- [x] record examples
- [x] add README
- [ ] Fix Jupyter-notebook renderin of cached responses
- [ ] Add cache tags to sqlite records
- [ ] extract code snippets from markdown frames
- [ ] parametize `parseTableAs` table cell values that map to `None`
  - [ ] add some form of a `startsWithN/A` option (or regex)
- [ ] make Mermaid dark-mode friendly
- [ ] improve Mermaid diagram sizes
- [x] add API token limit
- [x] parse tables
- [ ] parse Json
- [ ] render Json schemas from types
- [ ] add `prompt` notebook kernel
- [ ] Add C# support
- [ ] Write tests




