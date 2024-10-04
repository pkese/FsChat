#r "nuget: Microsoft.DotNet.Interactive, 1.0.0-beta.24229.4"

open System
open System.Threading.Tasks
open Microsoft.DotNet.Interactive
open Microsoft.DotNet.Interactive.Commands
open Microsoft.DotNet.Interactive.Formatting

module DomAppend =

    type AppendData(text:string, id:string) =
        inherit KernelCommand("javascript")
        member _.Text with get () = text
        member _.Id with get () = id

    let private jsKernel =
        let jsKernel = Kernel.Root.FindKernelByName("javascript")
        jsKernel.RegisterCommandType<AppendData>()
        jsKernel

    let domAppend (id:string) (text:string) =
        jsKernel.SendAsync(AppendData(text, id)) |> ignore

    let domAppendAsync (id:string) (text:string) =
        jsKernel.SendAsync(AppendData(text, id)) :> Task


let domAppend = DomAppend.domAppend
let domAppendAsync = DomAppend.domAppendAsync

// load the js bundle into the javascript kernel
do
    let jsBundle = __SOURCE_DIRECTORY__ + "/dist/bundle.js"
    let jsCode = System.IO.File.ReadAllText(jsBundle)
    //printfn "bundle size: %d" jsCode.Length
    Kernel.Root.SendAsync(SubmitCode(jsCode, "javascript")) |> ignore


