#r "../../src/Sast/bin/Debug/net452/Sast.dll"

open ScribbleGenerativeTypeProvider

open ScribbleGenerativeTypeProvider.DomainModel
                        
[<Literal>]
let delims = """ [ {"label" : "ADD", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } },
                   {"label" : "RES", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } }, 
                   {"label" : "BYE", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } }, 
                   {"label" : "HELLO", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } }]"""


[<Literal>]
let typeAliasing =
    """ [ {"alias" : "int", "type": "System.Int32"} ] """

// C:/cygwin64/home/rhu/code/vs/scribble/github.com/rumineykova/Sast/Examples/Fibonacci/
type Fib = 
    Provided.TypeProviderFile<"../../../Examples/ConnectChoice/FibnoAss.scr" // Fully specified path to the scribble file
                               ,"Adder" // name of the protocol
                               ,"P" // local role
                               ,"../../../Examples/ConnectChoice/configP.yaml" // config file containing IP and port for each role and the path to the scribble script
                               ,Delimiter=delims 
                               ,TypeAliasing=typeAliasing // give mapping from scribble base files to F# types
                               ,ScribbleSource = ScribbleSource.LocalExecutable // choose one of the following options: (LocalExecutable | WebAPI | File)
                               ,ExplicitConnection = true>

let C = Fib.C.instance
(*let numIter = 3
let S = Fib.S.instance
let rec fibrec a b iter (c:Fib.State13) = 
            let res = new DomainModel.Buf<int>()
            printfn "number of iter: %d" (numIter - iter)
            //let c = c0.sendHELLO(S, a)
            match iter with
                |0 -> 
                    let c1 = c.sendBYE(S)
                    let c2 = c1.receiveBYE(S)
                    c2.finish()
                |n -> 
                    let c1 = c.sendADD(S, a)
                    let c2 = c1.receiveRES(S, res)

                    printfn "Fibo : %d" (res.getValue())
                    Async.RunSynchronously(Async.Sleep(1000))

                    fibrec b (res.getValue()) (n-1) c2
*)
let res = new DomainModel.Buf<int>()
let fibo = new Fib()
let first = fibo.Start().accept(C).receiveRES(C, res).finish()
printf "value received is: %i" (res.getValue())

//first |> fibrec 1 1 numIter

(*let rec fibrec a b iter (c0:Fib.State7) =
    let res = new DomainModel.Buf<int>()
    printfn"number of iter: %d" (numIter - iter)
    let c = c0.sendHELLO(S, a)


    
    match iter with
        |0 -> c.sendBYE(S).receiveBYE(S).finish()
        |n -> let c1 = c.sendADD(S, a)
              let c2 = c1.receiveRES(S, res)
              printfn "Fibo : %d" (res.getValue())
              Async.RunSynchronously(Async.Sleep(1000))
              fibrec b (res.getValue()) (n-1) c2
*)


//let check func dict = 


    
