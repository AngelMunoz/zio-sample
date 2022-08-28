#r "nuget: Zio, 0.15.0"
#r "nuget: FSharp.Control.Reactive, 5.0.5"

open FSharp.Control.Reactive
open System
open System.IO
open Zio
open Zio.FileSystems

let ps = new PhysicalFileSystem()
let mfs = new MountFileSystem(new MemoryFileSystem(), true)

// Convert an absolute System.IO Path to a UPath that is required by
// the zio's file systems
let psRoot = ps.ConvertPathFromInternal(Path.GetFullPath("./sample"))

// Create target paths for future use
let psOut = UPath.Combine(psRoot.FullName, "out")
let psSrc = UPath.Combine(psRoot.FullName, "src")
let psAssets = UPath.Combine(psRoot.FullName, "assets")

// our physical file system is just like using System.IO API's
// but we can work directly with sub folders as if they were
// the root of their own file system
let fsSrc = ps.GetOrCreateSubFileSystem(psSrc)
let fsAssets = ps.GetOrCreateSubFileSystem(psAssets)

// the mount file system is very useful if we match it to the
// mountDirectories option used in perla projects
// we can literally match the server's URLs with paths in the mounted file system
mfs.Mount("/src", fsSrc)
mfs.Mount("/assets", fsAssets)

// in this case we will watch only what we're observing
// this watcher (and others) could be narrowed or widened by the watchConfig
// option in Perla's config
let srcWatcher = mfs.Watch("/")

// In a similar fashion of what we already do within Perla's watcher
// we can observe file system changes but rather than doing it in the file system
// we can watch our mounted file system knowing it has less things to worry about than
// monitoring the physical file system
[| srcWatcher.Changed |> Observable.map (fun event -> event)
   srcWatcher.Created
   srcWatcher.Deleted |]
|> Observable.mergeArray
// File System events tend to fire in quick succession, usually we don't want
// Every instance of those events hence why the throttle
|> Observable.throttle (TimeSpan.FromMilliseconds(400.))
|> Observable.add (fun event ->
    printfn $"Mount FS Event / -> %A{event.ChangeType} - {event.FullPath}"
    printfn "Enumerating Changes in Mounted File System"

    mfs.EnumerateItems("/", SearchOption.AllDirectories)
    |> Seq.iter (fun fse -> printfn $"{fse.FullName}"))

srcWatcher.EnableRaisingEvents <- true
srcWatcher.IncludeSubdirectories <- true

// when we're done (in the build phase as an example)
// we can copy the mounted file system to the outDir directory in perla's config
// and then knowing that there's only HTML/CSS/JS there we can simply run esbuild
// one last time on the system's file system and let it bundle all of our sources
let copyAggregateToOutDir () = mfs.CopyDirectory("/", ps, psOut, true)

// In cases where plugins might add/delete or modify content after they run
// we can update our virtual file system if required which can be useful
// to re-target files or injects or other stuff that might be needed
let addExtraFile () =
    let name = $"/{Guid.NewGuid()}.txt"
    let content = name |> Text.Encoding.UTF8.GetBytes

    use file =
        mfs.OpenFile(name, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite)

    file.Write(ReadOnlySpan(content))

backgroundTask {
    printfn "Current Directories in Mounted File System"

    mfs.EnumerateItems("/", SearchOption.AllDirectories)
    |> Seq.iter (fun fse -> printfn $"{fse.FullName}")

    printfn "Starting Task"

    while true do
        let! line = Console.In.ReadLineAsync()
        printfn "Got %s" line

        if line = "q" then
            printfn "good bye!"
            exit (0)
        elif line = "out" then
            copyAggregateToOutDir ()
        elif line = "add file" then
            addExtraFile ()

}
|> Async.AwaitTask
|> Async.RunSynchronously
