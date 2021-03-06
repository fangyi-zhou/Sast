﻿module ScribbleGenerativeTypeProvider.IO

open ProviderImplementation.ProvidedTypes // open the providedtypes.fs file
open System.Net.Sockets
open System.IO
open System.Text
open System
open Microsoft.FSharp.Quotations
open ScribbleGenerativeTypeProvider.DomainModel
open ScribbleGenerativeTypeProvider.CommunicationAgents
open System.Threading.Tasks
open ScribbleGenerativeTypeProvider.Util


/// this way of defining failures can be used in an exception raising manner
/// or in a RoP manner if at then of the day we can raise IFailure or RoP on IFailure
type IOFailures =
    | Encoding of string*Exception
    | SerializationPayload of obj*string
    | DeserializationConvertion of (byte [])*string
    interface IFailure with
        member this.Description =
            match this with
            | Encoding (encode,exn)                     -> sprintf "IOFailures[Encoding] : Impossible to encode( %s ) \-> exception( %s )" encode (exn.Message)
            | SerializationPayload (arg,typing)         -> sprintf "IOFailures[SerializationPayload] : Cannot serialize payloads( %A ) to type( %A )" arg typing
            | DeserializationConvertion (arg,typing)    -> sprintf "IOFailures[DeserializationConvertion] : Cannot deserialze payloads( %A ) to type( %A )" arg typing


// Helpers to write and read bytes with the help of delims
let toBytes (str : string) =
    try
        let encoder = new UTF8Encoding()
        encoder.GetBytes(str)
    with
    | e ->
        Encoding (str,e) |> createFailure

//

// Serialization + Deserialization + DesAsync + DesChoice
// Currently only working for basic types.
// Need to find a better way to handle IO properly!!
let serLabel (label:string) (delim:string) =
    let labelBytes = label |> toBytes
    let delimBytes = delim |> toBytes
    delimBytes |> Array.append labelBytes


let getIntValues (args: Expr list) =
    args |> List.map (fun arg -> Expr.Coerce(arg,typeof<int>) |> unbox<int []>)


let serPayloads (args:Expr list) (listTypes:string list) (payloadDelim:string)
                (endDelim:string) (argsNames:string list) foo (payloadNames: string list) =
    let mutable assArgIndex = 0
    let listPayloads =
        args
        |> List.mapi (fun i arg ->
            let currentType = listTypes.[i]
            let argName =
                if (foo <> "" && assArgIndex < argsNames.Length && argsNames.[assArgIndex].Equals(payloadNames.[i])) then
                    printfn "Argument name %s" argsNames.[assArgIndex]
                    assArgIndex <- assArgIndex + 1
                    argsNames.[assArgIndex-1]
                else
                    ""
            match currentType with
            |"System.String" | "System.Char"->
                <@
                    let spliced = %%(Expr.Coerce(arg,typeof<obj>))

                    let ranFoo =
                        // No Assertion provided
                        if (foo <> "" &&  argName <> "") then
                            Runtime.addArgValueToAssertionDict "agent" argName spliced
                            Runtime.runFooFunction "agent" foo
                        else
                            Some true
                    match ranFoo with
                    | None -> failwith "Incorrect type evaluated : Either not enough argument given to the assertion or the assertion is not a function that returns a boolean"
                    | Some false -> failwith "Assertion Constraint not met"
                    | Some true ->

                        try
                            Type.GetType("System.Text.UTF8Encoding")
                                .GetMethod("GetBytes",[|Type.GetType(currentType)|])
                                .Invoke(new UTF8Encoding(),[|spliced|]) |> unbox<byte []>
                        with
                        | _ ->
                            printing "Failed to serialize 1" ""
                            SerializationPayload (spliced,currentType) |> createFailure

                @>
            | _ ->
                <@
                    let spliced = %%(Expr.Coerce(arg,typeof<obj>))

                    let ranFoo =
                        // No Assertion provided
                        if (foo <> "" &&  argName <> "") then
                            // TimeMeasure.measureTime "before assertion"
                            Runtime.addArgValueToAssertionDict "agent" argName spliced
                            let res = Runtime.runFooFunction "agent" foo
                            // TimeMeasure.measureTime "after assertion res"
                            res
                        else
                            Some true
                    match ranFoo with
                    | None -> failwith "Incorrect type evaluated : Either not enough argument given to the assertion or the assertion is not a function that returns a boolean"
                    | Some false -> failwith "Assertion Constraint not met"
                    | Some true ->

                        try
                            Type.GetType("System.BitConverter")
                                .GetMethod("GetBytes",[|Type.GetType(currentType)|])
                                .Invoke(null,[|spliced|] ) |> unbox<byte []>
                        with
                        | _ ->
                            printing "Failed to serialize 2" ""
                            SerializationPayload (spliced,currentType) |> createFailure
                @>
           )

    let numberOfPayloads = listPayloads.Length

    let listDelims =
        [
            for i in 0..(numberOfPayloads-1) do
                if ( i = (numberOfPayloads-1) ) then
                    yield <@ (endDelim |> toBytes) @>
                else
                    yield <@ (payloadDelim |> toBytes) @> ]


    listPayloads
    |> List.fold2 (fun acc f1 f2 ->
        <@
            let f1 = %f1
            let f2 = %f2
            let acc = %acc

            Array.append (Array.append acc f1) f2
        @>
       ) <@ [||] @> <| listDelims



let serialize (label:string) (args:Expr list) (listTypes:string list) (payloadDelim:string)
              (endDelim:string) (labelDelim:string) argsNames foo payloadNames =

    let labelSerialized = <@ serLabel label labelDelim @> //

//    let payloadSerialized = serPayloads args listTypes payloadDelim endDelim
    <@
        printing "About to serialized" ""
        let payloadSerialized = %( serPayloads args listTypes payloadDelim endDelim argsNames foo payloadNames)
        let labelSerialized  = %(labelSerialized)
        printing "Serialization done" ""
        Array.append labelSerialized payloadSerialized
    @>

let convert (arrayList:byte[] list) (elemTypelist:string list) =
    let rec aux (arrList:byte[] list) (elemList:string list) (acc:obj list) =
        match arrList with
        |[] -> List.rev acc
        |hd::tl ->
            let sub = elemList.Head.Split('.')
            let typing = sub.[sub.Length-1]
            try
                let mymethod = Type.GetType("System.BitConverter")
                                    .GetMethod("To"+typing,[|typeof<byte []>;typeof<int>|])
                let invoke = mymethod.Invoke(null,[|box hd;box 0|])
                aux tl (elemList.Tail) (invoke::acc)
            with
            | e ->
                DeserializationConvertion (hd,typing) |> createFailure

    aux arrayList elemTypelist []

(*let mergeReceivedAndInferred (payloads: string list)
                             (inferred:Map<string, Expr>)
                             received =
    <@

    @>*)
let deserialize (messages: _ list) (role:string) (args: Expr list) (listTypes:string list) (argsNames:string list) foo  =
    let buffer = [for elem in args do
                    yield Expr.Coerce(elem,typeof<ISetResult>) ]
    // reduce only the list types
    <@
        //
        let result = Runtime.receiveMessage "agent" messages role [listTypes]
        //printfn " deserialize Normal : %A || Role : %A || listTypes : %A" messages role listTypes
        printing " received Bytes: " result

        let received = convert (result.Tail) listTypes

        let ranFoo =
            // No Assertion provided
            if foo <> "" then
                (argsNames,received)
                ||> List.map2(fun argName rcv -> Runtime.addArgValueToAssertionDict "agent" argName rcv )
                |> ignore
                Runtime.runFooFunction "agent" foo
            else
                Some true

        match ranFoo with
        | None -> failwith "Incorrect type evaluated : Either not enough argument given to the assertion or the assertion is not a function that returns a boolean"
        | Some false -> failwith "Assertion Constraint not met"
        | Some true ->
            let received = List.toSeq received

            Runtime.setResults received (%%(Expr.NewArray(typeof<ISetResult>, buffer)):ISetResult [])

    @>

let deserializeAsync (messages: _ list) (role:string) (args: Expr list)  (listTypes:string list)  argsNames foo =
    let buffer = [for elem in args do
                    yield Expr.Coerce(elem,typeof<ISetResult>) ]
    <@
        let work =
            async{
                let! res = Runtime.receiveMessageAsync "agent" messages role listTypes
                let res =
                    let received = (res.Tail |> convert <| listTypes )

                    let ranFoo =
                        // No Assertion provided
                        if foo <> "" then
                            (argsNames,received)
                            ||> List.map2(fun argName rcv -> Runtime.addArgValueToAssertionDict "agent" argName rcv )
                            |> ignore
                            Runtime.runFooFunction "agent" foo
                        else
                            Some true
                    match ranFoo with
                    | None -> failwith "Incorrect type evaluated : Either not enough argument given to the assertion or the assertion is not a function that returns a boolean"
                    | Some false -> failwith "Assertion Constraint not met"
                    | Some true ->

                        let received = received |> List.toSeq
                        Runtime.setResults received (%%(Expr.NewArray(typeof<ISetResult>, buffer)):ISetResult [])
                return res
            }
        Async.Start(work)
     @>


let deserializeChoice (args: Expr list) (listTypes:string list) argsNames foo =
    let buffer = [for elem in args do
                    yield Expr.Coerce(elem,typeof<ISetResult>) ]
    <@
        let result = Runtime.receiveChoice "agent"
        printing "List types" listTypes
        let received = (result |> convert <| listTypes )

        printing "foo is" foo
        let ranFoo =
            // No Assertion provided
            if foo <> "" then
                (argsNames,received)
                ||> List.map2(fun argName rcv -> Runtime.addArgValueToAssertionDict "agent" argName rcv)
                |> ignore
                Runtime.runFooFunction "agent" foo
            else
                Some true

        match ranFoo with
        | None -> failwith "Incorrect type evaluated : Either not enough argument given to the assertion or the assertion is not a function that returns a boolean"
        | Some false -> failwith "Assertion Constraint not met"
        | Some true ->

            let received = received |> List.toSeq
            let test = (%%(Expr.NewArray(typeof<ISetResult>, buffer)):ISetResult [])
            let completed = test |> Array.map(fun t -> t.GetTask())
            printing "Receive a choice" (received,test,completed)
            Runtime.setResults received (%%(Expr.NewArray(typeof<ISetResult>, buffer)):ISetResult [])
    @>
