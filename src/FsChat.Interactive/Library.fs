namespace FsChat.Interactive


module rec BundleHelper =

    open System.Reflection

    type ADummyClass() = class end

    let loadBundle () =

        let assembly = Assembly.GetAssembly(typeof<ADummyClass>);
        let dir = System.IO.Path.GetDirectoryName(assembly.Location)
        let bundlePath = System.IO.Path.Combine(dir, "../../main.js")
        //printfn "Bundle Path: %s" bundlePath
        System.IO.File.ReadAllText(bundlePath)
        //printfn $"DLL Location: {assembly.Location}"

        //System.Reflection.Assembly.GetExecutingAssembly()


module Say =
    let hello name =
        printfn "Hello %s" name
