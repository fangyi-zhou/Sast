﻿namespace ScribbleGenerativeTypeProvider

// Outside namespaces and modules
open System
open FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations
open ProviderImplementation.ProvidedTypes // open the providedtypes.fs file
open System.Reflection // necessary if we want to use the f# assembly
open System.Diagnostics
open System.IO
open System.Collections.Generic
open FSharp.Data
// ScribbleProvider specific namespaces and modules
open ScribbleGenerativeTypeProvider.TypeGeneration
open ScribbleGenerativeTypeProvider.DomainModel
open ScribbleGenerativeTypeProvider.CommunicationAgents
open ScribbleGenerativeTypeProvider.RefinementTypesDict
open ScribbleGenerativeTypeProvider.AsstScribbleParser
open ScribbleGenerativeTypeProvider.Util


type ScribbleSource = 
    | WebAPI = 0 
    | File = 1
    | LocalExecutable = 2

type internal WatchSpec = 
    private 
    | WatchSpec of string * (string -> bool)
    static member File path = WatchSpec (path, fun _ -> true)
    static member FileAndFilter (path, filter) = WatchSpec (path, filter)
    member x.Path = let (WatchSpec(p, _)) = x in p
    member x.Filter = let (WatchSpec(_, f)) = x in f


[<TypeProvider>]
type GenerativeTypeProvider(config : TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces (config)      
    let tmpAsm = ProvidedAssembly ()
    //let s = TimeMeasure.start()
    //TimeMeasure.measureTime "Starting"   

    // ==== cachng ============
    let cachedTypes = Dictionary<string, ProvidedTypeDefinition>()
    let mutable disposals = ResizeArray<(unit -> unit)>()
    let invalidation = new Event<System.EventHandler, _>()
    let mutable invalidationTriggered = 0
    let invalidate() = 
        // FSW can run callbacks in multiple threads - actual event should be raised at most once
        if System.Threading.Interlocked.CompareExchange(&invalidationTriggered, 1, 0) = 0 then
            invalidation.Trigger(null, EventArgs())
    
    let watchPath (spec : WatchSpec) =

        let watcher = 
            let folder = Path.GetDirectoryName spec.Path
            let file = Path.GetFileName spec.Path
            new FileSystemWatcher (folder, file)
        
        watcher.Changed.Add (fun f -> 
            if spec.Filter(f.FullPath) then 
                cachedTypes.Clear()        
                invalidate()
            )
        watcher.Deleted.Add(fun _ -> cachedTypes.Clear(); invalidate())
        watcher.Renamed.Add(fun _ -> cachedTypes.Clear(); invalidate())

        watcher.EnableRaisingEvents <- true
        disposals.Add(fun () -> watcher.Dispose())

    let invokeScribble pathToFile protocol localRole tempFileName assertionsOn =         
        // Configure command line
        // Add -batch (to speed up Z3 by passing one logical formulae for checking the protocol, 
        // hence the check is fast when the protocol is correct, but slow when it is not. 
            
        (* let batFile = if assertionsOn then """%scribble%""" else """%scribbleno%"""
        
        let scribbleArgs = sprintf """/C %s %s -ass %s -ass-fsm %s -Z3  >> %s 2>&1 """ 
                                    batFile pathToFile protocol localRole tempFileName *)

        // Uncomment below for Scribble without assertions 
        let psi_windows () =
            let scribbleArgs =
                match assertionsOn with
                | false ->
                     let batFile = """%scribbleno%"""
                     sprintf """/C %s %s -fsm %s %s >> %s 2>&1 """ batFile pathToFile protocol localRole tempFileName
                | true ->
                     let batFile = """%scribble%"""
                     sprintf """/C %s %s -ass %s -ass-fsm %s -Z3  >> %s 2>&1 """
                                    batFile pathToFile protocol localRole tempFileName
            ProcessStartInfo("cmd.exe", scribbleArgs)
        let psi_unix () =
            let scribbleArgs =
                sprintf "-c \"scribblec.sh %s -ass %s -ass-fsm %s -Z3 > %s 2>&1\"" pathToFile protocol localRole tempFileName
            ProcessStartInfo("bash", scribbleArgs)
        let psi =
            if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform (System.Runtime.InteropServices.OSPlatform.Windows)
                then psi_windows ()
                else psi_unix ()
        psi.UseShellExecute <- false; psi.CreateNoWindow <- true; 
                                                            
        // Run the cmd process and wait for its completion
        let p = new Process()
        p.StartInfo<- psi;                             
        let res = p.Start(); 
        p.WaitForExit()

        // Read the result from the executed script
        let parsedFile = File.ReadAllText(tempFileName) 
        // TODO:  Fix the parser not to care about starting/trailing spaces!
        let parsedScribble = parsedFile.ToString().Replace("\r\n\r\n", "\r\n")
        parsedScribble            

    let parseCFSM parsedScribble protocol localRole typeAliasing = 
        let str = sprintf """{"code":"%s","proto":"%s","role":"%s"}""" "code" protocol localRole
        match Parsing.getFSMJson parsedScribble str typeAliasing with 
            | Some parsed -> 
                parsed
            | None -> failwith "The file given does not contain a valid fsm"

    let generateTypes
        (fsm: string)
        (variablesMap: Map<string, AssertionParsing.Expr>)
        (name: string)
        (parameters: obj[])
        (tmpAsm: ProvidedAssembly) =

        let configFilePath = parameters.[0]  :?> string
        let delimitaters = parameters.[1]  :?> string
        let explicitConnection = parameters.[4] :?> bool

        let protocol = ScribbleProtocole.Parse(fsm)

        let triple = stateSet protocol
        let n, stateSet, firstState = triple
        let listTypes = (Set.toList stateSet) |> List.map (fun x -> makeStateType x )
        let firstStateType = findProvidedType listTypes firstState
        let tupleRole = makeRoleTypes protocol
        let tupleLabel = makeChoiceLabelTypes protocol listTypes (tupleRole |> fst)
        let listOfRoles = makeRoleList protocol
        let labelList = snd(tupleLabel)
        let roleList = snd(tupleRole)
        
        let mutable mapping = Map.empty<string,string list* string list * string list>

        let instance = MappingDelimiters.Parse(delimitaters)
        for elem in instance do
            let label = elem.Label
            let delims = elem.Delims
            let delim1 = delims.Delim1 |> Array.toList
            let delim2 = delims.Delim2 |> Array.toList
            let delim3 = delims.Delim3 |> Array.toList
            mapping <- mapping.Add(label,(delim1,delim2,delim3)) 

        mapping |> DomainModel.modifyMap 

        let naming = Path.Combine(config.ResolutionFolder, configFilePath)
        DomainModel.config.Load(naming)


        (tupleLabel |> fst) |> Runtime.addLabel
        let agentRouter = createRouter (DomainModel.config)  listOfRoles explicitConnection
        Runtime.addAgent "agent" agentRouter 
        let cache = createCache
        let assertionLookUp = createlookUp
        Runtime.initAssertionDict "agent" assertionLookUp
        Runtime.initCache "cache" cache

        addProperties listTypes listTypes (Set.toList stateSet) (fst tupleLabel) (fst tupleRole) protocol

        let ctor = firstStateType.GetConstructors().[0]                                                               
        let ctorExpr = Expr.NewObject(ctor, [])
        let exprCtor = ctorExpr
        let exprStart = <@@ Runtime.startAgentRouter "agent"  @@>
        let expression = Expr.Sequential(exprStart,exprCtor)
            
        let ty = name 
                    |> createProvidedType tmpAsm
                    |> addCstor ( <@@ "hey" + string n @@> |> createCstor [])
                    |> addMethod ( expression |> createMethodType "Init" [] firstStateType)
                    |> addIncludedTypeToProvidedType roleList
                    |> addIncludedTypeToProvidedType labelList
                    |> addIncludedTypeToProvidedType listTypes
        
        ty.SetAttributes(TypeAttributes.Public ||| TypeAttributes.Class)
        printfn "Adding Type %A" ty
        //ty.HideObjectMethods <- true
        tmpAsm.AddTypes [ty]
        ty

    let createOrUseProvidedTypeDefinition (name:string) (parameters:obj[]) =
        let tmpAsm = ProvidedAssembly ()
        match cachedTypes.TryGetValue name with
        | true, typeDef ->
            TimeMeasure.measureTime "From the cache"
            typeDef
        | _ -> 
            let file = parameters.[0] :?> string
            let protocol = parameters.[1] :?> string
            let localRole = parameters.[2] :?> string
            let configFilePath = parameters.[3] :?> string
            let typeAliasingParam = parameters.[5] :?> string

            // TODO: Fix as to replace only types
            let typeAliasing: Map<string, string> = 
                DotNetTypesMapping.Parse(typeAliasingParam) 
                |>  Array.map (fun s -> (s.Alias, s.Type)) 
                |> Map.ofArray


            let naming = Path.Combine(config.ResolutionFolder, configFilePath)
            DomainModel.config.Load(naming)

            let relativePath = Path.Combine(config.ResolutionFolder, file)
            let pathToFile = match File.Exists(file) with 
                            | true -> file 
                            | false -> 
                                match File.Exists(relativePath) with 
                                    | true -> relativePath
                                    | false -> failwith "The given file does not exist"
            
            watchPath (WatchSpec.File pathToFile) 
            watchPath (WatchSpec.File naming) 

            let scribbleSource = parameters.[6] :?> ScribbleSource
            let assertionsOn = parameters.[8] :?> bool


            let fsm = match scribbleSource with 
                        | ScribbleSource.WebAPI ->  
                            let str =  File.ReadAllText(pathToFile) // parse the Scribble code
                            let replace0 = System.Text.RegularExpressions.Regex.Replace(str,"(\s{2,}|\t+)"," ") 
                            let replace2 = System.Text.RegularExpressions.Regex.Replace(replace0,"\"","\\\"")
                            let str = sprintf """{"code":"%s","proto":"%s","role":"%s"}""" replace2 protocol localRole
                            FSharp.Data.Http.RequestString("http://localhost:8083/graph.json", 
                                query = ["json",str] ,
                                headers = [ FSharp.Data.HttpRequestHeaders.Accept HttpContentTypes.Json ],
                                httpMethod = "GET" )
                        |ScribbleSource.File -> 
                            //TimeMeasure.measureTime "Before Scribble"
                            //let parsedScribble = code.ToString()
                            let scribbleCode = File.ReadAllText(pathToFile)
                            parseCFSM scribbleCode protocol localRole typeAliasing
                        |ScribbleSource.LocalExecutable ->  
                            // let batFile = DomainModel.config.ScribblePath.FileName 
                            let tempFileName = Path.GetTempFileName()       
                            try  
                                let parsedScribble = invokeScribble pathToFile protocol localRole tempFileName assertionsOn
                                TimeMeasure.measureTime "After Scribble Compiler"  
                                let p = parseCFSM parsedScribble protocol localRole typeAliasing
                                TimeMeasure.measureTime "After Parsing "
                                p
                            finally 
                                if File.Exists(tempFileName) then File.Delete(tempFileName)
            let variablesMap : Map<string, AssertionParsing.Expr> = Map.empty
            let size = parameters.Length
            //TimeMeasure.measureTime "Before Type generation"
            let genType = generateTypes fsm  variablesMap name parameters.[3..(size-1)] tmpAsm
            cachedTypes.Add(name, genType)
            //TimeMeasure.measureTime "After Type generation"
            genType

    let asm = Assembly.LoadFrom config.RuntimeAssembly
    let providedType = TypeGeneration.createProvidedType asm "TypeProviderFile"
    let parametersTP=  [ProvidedStaticParameter("File Uri",typeof<string>);
                          ProvidedStaticParameter("Global Protocol",typeof<string>);
                          ProvidedStaticParameter("Role",typeof<string>);
                          ProvidedStaticParameter("Config",typeof<string>);
                          ProvidedStaticParameter("Delimiter",typeof<string>);
                          ProvidedStaticParameter("TypeAliasing",typeof<string>); 
                          ProvidedStaticParameter("ScribbleSource",typeof<ScribbleSource>, ScribbleSource.LocalExecutable);
                          ProvidedStaticParameter("ExplicitConnection",typeof<bool>, false);
                          ProvidedStaticParameter("AssertionsOn",typeof<bool>, false);
                          ProvidedStaticParameter("AssertionOptimisationsOn",typeof<bool>, false);]

    do 
        this.Disposing.Add((fun _ ->
            let disposers = disposals |> Seq.toList
            disposals.Clear()
            for disposef in disposers do try disposef() with _ -> ()
        ))

        providedType.DefineStaticParameters(parametersTP, createOrUseProvidedTypeDefinition)
        this.AddNamespace(ns, [providedType])     
        TimeMeasure.measureTime "Assembly"
    
    [<CLIEvent>]
    member x.Invalidate = invalidation.Publish

    (*interface IDisposable with 
        member this.Dispose() = 
           let disposers = disposals |> Seq.toList
           disposals.Clear()
           for disposef in disposers do try disposef() with _ -> ()*)

[<assembly:TypeProviderAssembly>]
    do() 
