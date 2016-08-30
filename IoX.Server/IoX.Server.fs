﻿module IoX.Server

open IoX.Modules
open IoX.Utils
open Suave
open Suave.Operators
open System
open System.IO

type ModuleDescriptor = {
  AssemblyPath: string
  ModuleTypeName: string
}

type RootModuleData<'C>(path, url, defaultCfg) =
  interface IModuleData<'C> with
    member this.Id = ""
    member this.Path = path
    member this.BaseUri = url
    member val Configuration = ConfigurationFile(Path.Combine(path, "module.conf"), defaultCfg)

type IoXConfiguration = {
  RootPath: string
  BindingAddress: string
  BindingPort: int
}

let defaultIoXConfiguration = {
  RootPath = "root"
  BindingAddress = "0.0.0.0"
  BindingPort = 8080
}

let loadRootModule path url =
  let descPath = Path.Combine(path, "module.iox.conf")
  let desc =
    try readConfigurationFile(descPath)
    with e ->
      printfn "Could not load root module configuration from %A." descPath
      raise e

  let assembly = Reflection.Assembly.LoadFrom(Path.Combine(path, desc.AssemblyPath))
  let mType =
    match assembly.GetType(desc.ModuleTypeName) with
    | null -> failwith (sprintf "Error loading %s from %s: type not found" desc.ModuleTypeName desc.AssemblyPath)
    | m when m.IsSubclassOf(typeof<Module>) -> m
    | _ -> failwith (sprintf "Error loading %s from %s: it does not inherit from IoX.Modules.Module" desc.ModuleTypeName desc.AssemblyPath)

  let defaultCfg = mType.GetProperty("DefaultConfig").GetValue(null)
  let dataType =
    if isNull defaultCfg then
      typeof<RootModuleData<unit>>
    else
      typedefof<RootModuleData<_>>.MakeGenericType(defaultCfg.GetType())
  let data = Activator.CreateInstance(dataType, path, url, defaultCfg)
  let m = Activator.CreateInstance(mType, data) :?> Module
  let browse ctx =
    match ctx.userState.["relPath"] with
    | :? string as s when m.Browsable ->
      let fileName = sprintf "static/%s" s
      Files.browseFile path fileName ctx
    | _ -> fail
  fun ctx ->
    let relPath = ctx.request.url.AbsolutePath.Substring(1)
    (m.WebPart <|> browse) { ctx with userState = ctx.userState.Add("relPath", relPath) }

let run path =
  let conf = Utils.ConfigurationFile("iox.conf", defaultIoXConfiguration)
  let config = {
    defaultConfig with
      bindings = [ HttpBinding.mkSimple HTTP conf.Data.BindingAddress conf.Data.BindingPort ]
  }
  let rootPath = System.IO.Path.Combine(path, conf.Data.RootPath)
  let rootUri = SuaveConfig.firstBindingUri config "" ""
  let wp = loadRootModule rootPath rootUri
  startWebServer config wp

[<EntryPoint>]
let main argv =
  run System.Environment.CurrentDirectory
  0
