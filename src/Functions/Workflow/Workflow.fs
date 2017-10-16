﻿module Ptp.Workflow.Function

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Ptp.Core
open Ptp.Workflow
open System
open Chessie.ErrorHandling
open Newtonsoft.Json

(* A diagram showing how how workflows are triggered.
   Once daily all legislators, subjects, committees, and bills are updated.
   Every 10 minutes, actions and committee/chamber calendars are updated.
   In the event that an action, or committee/chamber calendar refers to an 
     unkonwn bill or committee, an out-of-band update request is made for that
     bill or committee. New actions/events will be picked up ~10 minutes later.

                                                                                                |--------------------| 
                                                                                              |--------------------| |
                            |--------------------|            |-------------------|         |--------------------| |-|
Once Daily: [TIMER] ------> | Update Legislators | ----+----> | Update Committees | ------> | Update Committee X |-|   ---> [STOP]
                            |--------------------|     |      |-------------------|         |--------------------|  
                                                       |                ^                     
                                                       |                |                        
                                                       |                +------------------------------------------------------------------------------+
                                                       |                                                                                               |
                                                       |                                                             |---------------|                 |
                                                       |                                                           |---------------| |                 |
                                                       |      |-----------------|        |--------------|        |---------------| |-|                 |
                                                       +--->  | Update Subjects | -----> | Update Bills | -----> | Update Bill X |-|   ---> [STOP]     |
                                                              |-----------------|        |--------------|        |---------------|                     |
                                                                                                                           ^                           |
                                                                                                                           |                           |
                                                                                                                           +--------------------+      |
                                                                                                                                                |      |
                                                                                                                         //===============//    |      |
                                                  +-----------------------------+-------------------------------+-----> // Unknown Bill? // ----+      |
                                                  |                             |                               |      //===============//             |
                                                  |                             |                               |                                      |
                                                  |                             |                               |        //====================//      |
                                                  |                             |                               +-----> // Unknown Committee? // ------+ 
                                                  |                             |                               |      //====================//  
                                                  |                             |                               |  
                            |----------------|    |    |--------------------|   |    |-----------------------|  |
Every 10 Mins: [TIMER] ---> | Update Actions | ---+--> | Update Chamber Cal | --+--> | Update Committeee Cal |--+-----> [STOP]
                            |----------------|         |--------------------|        |-----------------------|


*)


let deserialize command =
    try
        match command with 
        | "" -> None
        | _  -> 
            command 
            |> JsonConvert.DeserializeObject<Workflow>
            |> Some
    with ex -> None
    

let chooseWorkflow msg =
    match msg with
    | UpdateLegislators     -> Legislators.workflow
    | UpdateCommittees      -> Committees.workflow
    | UpdateCommittee link  -> Committee.workflow link
    | UpdateSubjects        -> Subjects.workflow
    | UpdateBills           -> Bills.workflow
    | UpdateBill link       -> Bill.workflow link
    | _ -> raise (NotImplementedException())

let enqueueNext (log:TraceWriter) source elapsed (queue:ICollector<string>) result =
    match result with
    | Ok (NextWorkflow next, _) ->
        match next with 
        | EmptySeq    -> 
            sprintf "[%A] succeeded in %d ms. This is a terminal step." source elapsed
            |> timestamped
            |> log.Info 
            |> ignore
        | steps ->
            let next = 
                steps 
                |> Seq.map JsonConvert.SerializeObject
            next 
            |> Seq.map (fun n -> n.ToString())
            |> String.concat "\n"
            |> sprintf "[%A] succeeded in %d ms. Next steps:\n%s" source elapsed
            |> timestamped
            |> log.Info
            next 
            |> Seq.iter queue.Add
    | Bad _ ->
        sprintf "[%A] failed in %d ms. Enqueueing no next step." source elapsed
        |> timestamped
        |> log.Info 
        |> ignore
    result

let Run(log: TraceWriter, command: string, nextCommand: ICollector<string>) =
    try
        let stopwatch = Diagnostics.Stopwatch.StartNew()
        match deserialize command with 
        | Some cmd ->
            sprintf "Start workflow: [%A]" cmd 
            |> timestamped
            |> log.Info
            cmd
            |> chooseWorkflow
            |> executeWorkflow log cmd
            |> enqueueNext log cmd (stopwatch.ElapsedMilliseconds) nextCommand
            |> throwOnFail cmd
            |> ignore
        | None -> 
            "Received empty command"
            |> timestamped 
            |> log.Warning
    with ex -> 
        ex.ToString() |> log.Warning
        raise ex