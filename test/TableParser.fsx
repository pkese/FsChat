#!/usr/bin/env -S dotnet fsi

#load "../src/FsChat/FsChat.Markdown.fsx" "../src/FsChat/FsChat.TableReader.fsx"

open FsChat.Markdown
open FsChat.TableReader

let mdSample = """

# Predsedniki ZDA

| od         | do         | predsednik          | stranke             | leva/desna |
|------------|------------|---------------------|---------------------|------------|
| 20.01.1989 | 20.01.1993 | George H. W. Bush   | Republikanska,A1    | desna      |
| 20.01.1993 | 20.01.2001 | Bill Clinton        | Demokratska         | leva       |
| 20.01.2001 | 20.01.2009 | George W. Bush      | Republikanska       | desna      |
| 20.01.2009 | 20.01.2017 | Barack Obama        | Demokratska         | leva       |
| 20.01.2017 | 20.01.2021 | Donald Trump        | Republikanska       | desna      |
| 20.01.2021 | sedaj      | Joe Biden           | Demokratska         | leva       |

konec
"""

type Tbl = {
    od: string
    ``do``: string
    predsednik: string
    stranke: string list
    levaDesna: string
}

let actual : Tbl list =
    mdSample
    |> parse
    |> getTables
    |> List.last
    |> parseTableAs<Tbl list>

let expected = [
    {
        od = "20.01.1989"
        ``do`` = "20.01.1993"
        predsednik = "George H. W. Bush"
        stranke = [ "Republikanska"; "A1" ]
        levaDesna = "desna"
    }
    {
        od = "20.01.1993"
        ``do`` = "20.01.2001"
        predsednik = "Bill Clinton"
        stranke = [ "Demokratska" ]
        levaDesna = "leva"
    }
    {
        od = "20.01.2001"
        ``do`` = "20.01.2009"
        predsednik = "George W. Bush"
        stranke = [ "Republikanska" ]
        levaDesna = "desna"
    }
    {
        od = "20.01.2009"
        ``do`` = "20.01.2017"
        predsednik = "Barack Obama"
        stranke = [ "Demokratska" ]
        levaDesna = "leva"
    }
    {
        od = "20.01.2017"
        ``do`` = "20.01.2021"
        predsednik = "Donald Trump"
        stranke = [ "Republikanska" ]
        levaDesna = "desna"
    }
    {
        od = "20.01.2021"
        ``do`` = "sedaj"
        predsednik = "Joe Biden"
        stranke = [ "Demokratska" ]
        levaDesna = "leva"
    }
]

if actual <> expected then
    failwithf "Table parsing regression.\nExpected: %A\nActual:   %A" expected actual

printfn "%A" actual
printfn "OK: parsed %d rows" actual.Length
