﻿#I @"../../FAKE/tools/"
#r @"../../FAKE/tools/FakeLib.dll"
#r @"../../FAKE/tools/Fake.Deploy.Lib.dll"
#r @"../../Nuget.Core/lib/net40-Client/NuGet.Core.dll"

open System
open System.Collections.Generic
open System.IO
open Fake
open Fake.Git
open Fake.FSharpFormatting
open Fake.AssemblyInfoFile
open Fake.ReleaseNotesHelper
open Fake.ProcessHelper
open Fake.Json

type System.String with member x.endswith (comp:System.StringComparison) str = x.EndsWith(str, comp)

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  BEGIN EDIT

let appName = getBuildParamOrDefault "appName" ""
let appSummary = getBuildParamOrDefault "appSummary" ""
let appDescription = getBuildParamOrDefault "appDescription" ""
let appAuthors = ["Nikolai Mynkow"; "Simeon Dimov"; "Blagovest Petrov";]

//  END EDIT
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
let appType = match appName.ToLowerInvariant() with
                  | EndsWith "msi" -> "msi"
                  | EndsWith "cli" -> "cli"
                  | _ -> "lib"

let sourceDir = "./src"
let appDir = sourceDir @@ appName
let assemblyInfoFile = appDir @@ "Properties/AssemblyInfo.cs"
let deploymentDir = appDir @@ "deployment"
let buildDir  = @"./bin/Release" @@ appName
let websiteDir = buildDir @@ "_publishedWebsites" @@ appName
let msiDir = buildDir @@ "_publishedMsi" @@ appName
let toolDir = buildDir @@ "_tools" @@ appName

let releaseNotes = sourceDir @@ appName @@ @"RELEASE_NOTES.md"
let release = LoadReleaseNotes releaseNotes

let nuget = environVar "NUGET"
let nugetWorkDir = "./bin/nuget" @@ appName
let nugetLibDir = nugetWorkDir @@ "lib" @@ "net45-full"
let nugetToolsDir = nugetWorkDir @@ "tools"
let nugetContentDir = nugetWorkDir @@ "content"

let gitversion = environVar "GITVERSION"

Target "Clean" (fun _ -> CleanDirs [buildDir; nugetWorkDir;])

Target "RestoreNugetPackages" (fun _ ->
  let packagesDir = @"./src/packages"
  let nugetSources = environVarOrDefault "NUGET_SOURCES" "https://www.nuget.org/api/v2"
  !! "./src/*/packages.config"
  |> Seq.iter (RestorePackage (fun p ->
      { p with
          Sources = nugetSources :: "https://www.nuget.org/api/v2" :: p.Sources
          ToolPath = nuget
          OutputPath = packagesDir }))
)

Target "RestoreBowerPackages" (fun _ ->
    !! "./src/*/package.json"
    |> Seq.iter (fun config ->
        config.Replace("package.json", "")
        |> fun cfgDir ->
            printf "Bower working dir: %s" cfgDir
            let result = ExecProcess (fun info ->
                            info.FileName <- "cmd"
                            info.WorkingDirectory <- cfgDir
                            info.Arguments <- "/c npm install") (TimeSpan.FromMinutes 20.0)
            if result <> 0 then failwithf "'npm install' returned with a non-zero exit code")
)

type Version = {
    Major : int
    Minor : string
    Patch : string
    PreReleaseTag : string
    PreReleaseTagWithDash : string
    BuildMetaData : string
    BuildMetaDataPadded : string
    FullBuildMetaData : string
    MajorMinorPatch : string
    SemVer : string
    LegacySemVer : string
    LegacySemVerPadded : string
    AssemblySemVer : string
    FullSemVer : string
    InformationalVersion : string
    BranchName : string
    Sha : string
    NuGetVersionV2 : string
    NuGetVersion : string
    CommitDate : string
}

let mutable gitVer = Unchecked.defaultof<Version>

Target "UpdateAssemblyInfo" (fun _ ->
    let result = ExecProcessAndReturnMessages (fun info ->
        info.FileName <- gitversion
        info.WorkingDirectory <- "."
        info.Arguments <- "/output json") (TimeSpan.FromMinutes 5.0)
        
    if result.ExitCode <> 0 then failwithf "'GitVersion.exe' returned with a non-zero exit code"
    
    let jsonResult = System.String.Concat(result.Messages)
    
    jsonResult |> deserialize<Version> |> fun ver -> gitVer <- ver

    let copyright = "Copyright ©  " + DateTime.UtcNow.Year.ToString()
     
    CreateCSharpAssemblyInfo assemblyInfoFile
         [Attribute.Title appName
          Attribute.Description appDescription
          Attribute.ComVisible false
          Attribute.Product appName
          Attribute.Copyright copyright
          Attribute.Version gitVer.AssemblySemVer
          Attribute.FileVersion (gitVer.MajorMinorPatch + ".0")
          Attribute.InformationalVersion gitVer.InformationalVersion]
)

Target "Build" (fun _ ->
    let appBuildFile = sourceDir @@ appName @@ "build.cmd"
    if File.Exists appBuildFile then
                                    let result = ExecProcess (fun info ->
                                            info.FileName <- "cmd"
                                            info.WorkingDirectory <- sourceDir @@ appName
                                            info.Arguments <- "/c build.cmd") (TimeSpan.FromMinutes 5.0)
                                    if result <> 0 then failwithf "'build.cmd' returned with a non-zero exit code"

    match appType with
    | "msi" -> sourceDir @@ appName + ".sln" |> fun dir -> !!dir |> MSBuildRelease msiDir "Build" |> Log "Build-Output: "
    | "cli" -> sourceDir @@ appName @@ appName + ".csproj" |> fun dir -> !!dir |> MSBuildRelease toolDir "Build" |> Log "Build-Output: "
    | _ -> sourceDir @@ appName @@ appName + ".csproj" |> fun dir -> !!dir |> MSBuildRelease buildDir "Build" |> Log "Build-Output: "
)

Target "PrepareReleaseNotes" (fun _ ->
    let isNOTValid = (gitVer.NuGetVersionV2, release.NugetVersion) |> String.Equals |> not

    if isNOTValid then
                    Console.ForegroundColor <- ConsoleColor.Red
                    printfn "Unable to find release notes for version '%s'" gitVer.NuGetVersionV2
                    Console.ForegroundColor <- ConsoleColor.White
                    Environment.Exit(1)
)

Target "PrepareNuGet" (fun _ ->
    //  Exclude libraries which are part of the packages.config file only when nuget package is created.
    let nugetPackagesFile = sourceDir @@ appName @@ "packages.config"
    let dependencies = match File.Exists nugetPackagesFile with
                        | true -> getDependencies nugetPackagesFile
                        | _ -> []
    let dependencyFiles = dependencies
                          |> Seq.map(fun (name,ver) -> name + "." + ver)
                          |> Seq.collect(fun pkgName -> !! ("./src/packages/*/" + pkgName + ".nupkg"))
                          |> Seq.collect(fun pkg -> global.NuGet.ZipPackage(pkg).GetFiles())
                          |> Seq.map(fun file -> "\\" + filename file.Path)
                          |> fun gga -> Collections.Set(gga)
                          |> Set.toList
    
    printfn "Nuget dependencies:"
    dependencyFiles |> Seq.iter(fun file -> printfn "%s" file)
    let excludePaths (pathsToExclude : string list) (path: string) = pathsToExclude |> List.exists (path.endswith StringComparison.OrdinalIgnoreCase)|> not
    let exclude = excludePaths dependencyFiles

    let buildDirList = Directory.GetDirectories buildDir
    buildDirList
    |> Seq.iter(fun dir ->
        if dir.ToLowerInvariant().Contains "_published" then CopyDir nugetContentDir (dir @@ appName) allFiles
        if dir.ToLowerInvariant().Contains "_tools" then CopyDir nugetToolsDir (dir @@ appName) allFiles)

    if buildDirList.Length.Equals 0
    then CopyDir nugetLibDir buildDir exclude

    //if Directory.Exists toolDir then CopyDir nugetToolsDir toolDir allFiles
    if Directory.Exists deploymentDir then CopyDir nugetToolsDir deploymentDir allFiles
)

Target "CreateNuget" (fun _ ->
    let nugetPackagesFile = sourceDir @@ appName @@ "packages.config"
    let dependencies = match File.Exists nugetPackagesFile && appType.Equals "cli" |> not with
                        | true -> getDependencies nugetPackagesFile
                        | _ -> []

    let nugetAccessKey = getBuildParamOrDefault "nugetkey" ""
    let nugetPackageName = getBuildParamOrDefault "nugetPackageName" appName
    let nuspecFile = sourceDir @@ appName @@ nugetPackageName + ".nuspec"
    let nugetDoPublish = nugetAccessKey.Equals "" |> not
    let nugetPublishUrl = getBuildParamOrDefault "nugetserver" "https://nuget.org"

    //  Create/Publish the nuget package
    NuGet (fun app ->
        {app with
            NoPackageAnalysis = true
            Authors = appAuthors
            Project = nugetPackageName
            Description = appDescription
            Version = gitVer.NuGetVersionV2
            Summary = appSummary
            ReleaseNotes = release.Notes |> toLines
            Dependencies = dependencies
            AccessKey = nugetAccessKey
            Publish = nugetDoPublish
            PublishUrl = nugetPublishUrl
            ToolPath = nuget
            OutputPath = nugetWorkDir
            WorkingDir = nugetWorkDir
        }) nuspecFile
)

Target "ReleaseLocal" (fun _ ->
   printfn "Release local"
)

Target "Release" (fun _ ->
    StageAll ""
    let notes = String.concat "; " release.Notes
    Commit "" (sprintf "%s" notes)
    Branches.push ""

    Branches.tag "" gitVer.NuGetVersionV2
    Branches.pushTag "" "origin" gitVer.NuGetVersionV2
)

"Clean"
    ==> "RestoreNugetPackages"
    ==> "RestoreBowerPackages"
    ==> "UpdateAssemblyInfo"
    ==> "Build"
    ==> "PrepareReleaseNotes"
    ==> "PrepareNuGet"
    ==> "CreateNuget"
    ==> "ReleaseLocal"
    ==> "Release"

RunParameterTargetOrDefault "target" "Build"
