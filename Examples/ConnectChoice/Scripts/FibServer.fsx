﻿#r "../../../src/Sast/bin/Debug/Sast.dll"

open ScribbleGenerativeTypeProvider
                        
[<Literal>]
let delims = """ [ {"label" : "ADD", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } },
                   {"label" : "RES", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } }, 
                   {"label" : "BYE", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } }, 
                   {"label" : "HELLO", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } }]"""


[<Literal>]
let typeAliasing =
    """ [ {"alias" : "int", "type": "System.Int32"} ] """

type Fib = 
    Provided.TypeProvider<"../../../Examples/ConnectChoice/FibnoAss.scr"
                               ,"Adder"
                               ,"S"
                               ,"../../../Examples/ConnectChoice/configServer.yaml"
                               ,Delimiter=delims
                               ,TypeAliasing=typeAliasing
                               ,ScribbleSource = ScribbleSource.LocalExecutable
                               ,ExplicitConnection = true>



let numIter = 10-2
let C = Fib.C.instance

(*
let rec fibServer (c0:Fib.State21) =
    let res1 = new DomainModel.Buf<int>()
    let res2 = new DomainModel.Buf<int>()
    let c = c0.receiveHELLO(C, res1)
    printfn"received Hello %i" (res1.getValue())

    match c.branch() with 
        | :? Fib.BYE as bye-> 
            printfn"receive bye"
            bye.receive(C).sendBYE(C).finish()
        | :? Fib.ADD as add -> 
            printfn"receive add" 
            let c1 = add.receive(C, res2).sendRES(C, 1)
            fibServer c1
*)

let res = new DomainModel.Buf<int>()
let res1 = new DomainModel.Buf<int>()
let session = new Fib()
let sessionCh = session.Start().accept(C).receiveHELLO(C, res)

match sessionCh.branch() with 
        | :? Fib.BYE as bye-> 
            printfn"receive bye"
            bye.receive(C).sendBYE(C).finish()
        | :? Fib.ADD as add -> 
            add.receive(C, res1).sendRES(C, res1.getValue()).finish()
   

//let branch =  sessionCh.branch() 
//fibServer(sessionCh)

(*  
let rec fibrec a b iter (c:Fib.State14) =
    let res = new DomainModel.Buf<int>()
    printfn"number of iter: %d" (numIter - iter)
    match iter with
        |0 -> c.sendBYE(S).receiveBYE(S).finish()
        |n -> let c1 = c.sendADD(S, a, b)
              let c2 = c1.receiveRES(S, res)
              printfn "Fibo : %d" (res.getValue())
              Async.RunSynchronously(Async.Sleep(1000))
              fibrec b (res.getValue()) (n-1) c2

let fibo = new Fib()
let first = fibo.Start()
first |> fibrec 1 1 numIter

*)