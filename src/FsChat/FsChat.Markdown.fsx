#if INTERACTIVE
//
//#r "nuget: FSharp.Formatting, 20.0.1"
#r "../../submodules/FSharp.Formatting/src/FSharp.Formatting.Markdown/bin/Debug/netstandard2.1/FSharp.Formatting.Markdown.dll"
#load "FsChat.TableReader.fsx"
#else
module FsChat.Markdown
open FsChat.TableReader
#endif

open FSharp.Formatting.Markdown

/// Extract text from MarkdownParagraphs (e.g. table cells)
/// Note: in cese of links and images, we return the URL instead of text
/// ideally this should be parametrized
let private extractText (paragraphs:MarkdownParagraphs) =
    let sb = System.Text.StringBuilder()
    let inline append (x:string) = sb.Append(x) |> ignore

    let rec extractInline = function
        | AnchorLink (link, range) -> append link
        | DirectImage (alt, src, title, range) -> append src
        | DirectLink (text, href, title, range) -> append href
        | EmbedSpans (embedSpans, range) ->
            let spans' = embedSpans.Render()
            extractInlines spans'
        | Emphasis (spans, range) -> extractInlines spans
        | HardLineBreak _ -> append "\n"
        | IndirectImage (alt, src, title, range) -> append src
        | IndirectLink (text, href, title, range) -> append href
        | InlineCode (code, range) -> append code
        | LatexDisplayMath (code, range) -> ()
        | LatexInlineMath (code, range) -> ()
        | Literal (text, range) -> append text
        | Strong (spans, range) -> extractInlines spans

    and extractInlines (inlines:MarkdownSpans) : unit =
        match inlines with
        | [] -> ()
        | h :: t -> extractInline h; extractInlines t

    let rec extractText (p:MarkdownParagraph) =
        match p with
        | CodeBlock _ -> ()
        | EmbedParagraphs (customParagraphs, range) -> ()
        | Heading (level, text, range) -> ()
        | HorizontalRule _ -> ()
        | InlineHtmlBlock _ -> ()
        | LatexBlock _ -> ()
        | ListBlock _ -> ()
        | OtherBlock (lines, range) -> ()
        | OutputBlock _ -> ()
        | Paragraph (inlines, range) ->
            extractInlines inlines
        | QuotedBlock _ -> ()
        | Span (body, range) -> ()
        | TableBlock (headers, alignments, rows, range) -> ()
        | YamlFrontmatter _ -> ()

    for p in paragraphs do
        extractText p
    sb.ToString()

let private extractTableRow (row:MarkdownTableRow) =
    row
    |> List.map extractText

(*
let analyze (doc:MarkdownDocument) =
    for p in doc.Paragraphs do
        match p with
        | CodeBlock _ -> ()
        | EmbedParagraphs (customParagraphs, range) -> printfn "EmbedParagraphs: %A" customParagraphs
        | Heading (level, text, range) -> printfn "Heading %d: %A" level text
        | HorizontalRule _ -> ()
        | InlineHtmlBlock _ -> ()
        | LatexBlock _ -> ()
        | ListBlock _ -> ()
        | OtherBlock (lines, range) -> printfn "OtherBlock: %A" lines
        | OutputBlock _ -> ()
        | Paragraph (inlines, range) -> printfn "Paragraph: %A" inlines
        | QuotedBlock _ -> ()
        | Span (body, range) -> ()
        | TableBlock (headers, alignments, rows, range) ->
            headers
            |> Option.map extractTableRow
            |> Option.defaultValue []
            |> printfn "Table: %A"
        | YamlFrontmatter _ -> ()
*)

let getTables (doc:MarkdownDocument) =
    let rec getTables' (doc:MarkdownDocument) =
        [ for p in doc.Paragraphs do
            match p with
            | TableBlock (headers, alignments, rows, range) ->
                yield {
                    headers =
                        headers
                        |> Option.map extractTableRow
                        |> Option.defaultValue []
                    rows =
                        rows
                        |> List.map extractTableRow
                }
            | _ -> ()
        ]
    getTables' doc

let inline parse (text:string) = Markdown.Parse(text)
