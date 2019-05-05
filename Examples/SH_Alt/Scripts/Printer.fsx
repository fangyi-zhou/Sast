#r "../../src/Sast/bin/Debug/net452/Sast.dll"

open ScribbleGenerativeTypeProvider

open ScribbleGenerativeTypeProvider.DomainModel
                        
[<Literal>]
let delims = """ [ {"label" : "Above", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } },
                   {"label" : "Res", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } }, 
                   {"label" : "hello", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } }, 
                   {"label" : "BothIn", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } }, 
                   {"label" : "BothOut", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } }, 
                   {"label" : "Inersect", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } }, 
                   {"label" : "Res1", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } }, 
                   {"label" : "One", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } }, 
                   {"label" : "Two", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } }, 
                   {"label" : "Close", "delims": {"delim1": [":"] , "delim2": [","] , "delim3": [";"] } }]"""

[<Literal>]
let typeAliasing =
    """ [ {"alias" : "int", "type": "System.Int32"} ] """

// C:/cygwin64/home/rhu/code/vs/scribble/github.com/rumineykova/Sast/Examples/Fibonacci/
type SH = 
    Provided.TypeProviderFile<"../../../Examples/SH/SHNew.scr" // Fully specified path to the scribble file
                               ,"SH" // name of the protocol
                               ,"C" // local role
                               ,"../../../Examples/SH/configP.yaml" // config file containing IP and port for each role and the path to the scribble script
                               ,Delimiter=delims 
                               ,TypeAliasing=typeAliasing // give mapping from scribble base files to F# types
                               ,ScribbleSource = ScribbleSource.LocalExecutable // choose one of the following options: (LocalExecutable | WebAPI | File)
                               ,ExplicitConnection = true
                               ,AssertionsOn=true>
let numIter = 3
let P = SH.P.instance
//let R = SH.R.instance

let rec printPoints (c:SH.State27) =
    let res = new DomainModel.Buf<int>()    
    match c.branch() with 
    | :? SH.BothIn as point -> 
        let c1 = point.receive(P, res)
        //printf "POint received: %i" (res.getValue()) 
        printPoints c1  
    | :? SH.BothOut as none->  
        printPoints (none.receive(P))

    | :? SH.One as one -> 
        let c1 = one.receive(P)
        //printf "POint received: one"
        printPoints c1  
    | :? SH.Two as two -> 
        let res2 = new DomainModel.Buf<int>()    

        let c1 = two.receive(P)
        //printf "POint received: two" 
        printPoints c1
    | :? SH.Close as close -> close.receive(P).finish() 
         

let sh = new SH()
let dum = new DomainModel.Buf<int>()   
printPoints (sh.Start().receivehello(P, dum))

//fun p2 res z -> z = p2 && res = 0
