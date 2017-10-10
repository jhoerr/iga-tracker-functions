﻿module Ptp.Logging

open Ptp.Core
open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs.Host
open System

let success msg = 
    sprintf "[SUCCESS] %s" msg

let flatten (msgs:WorkFlowFailure list) = 
    msgs 
    |> List.rev 
    |> List.map (fun wf -> wf.ToString())
    |> String.concat "\n" 

/// Log succesful or failed completion of the function, along with any warnings.
let runWorkflow (log:TraceWriter) source workflow = 

    log.Info(sprintf "[START] %A" source)

    let result = workflow()
    match result with
    | Fail (boo) ->  
        boo |> flatten |> log.Error
    | Warn (yay,boo) ->  
        boo |> flatten |> log.Warning
        yay |> success |> log.Info
    | Pass (yay) -> 
        yay |> success |> log.Info
    
    log.Info(sprintf "[FINISH] %A" source)
    
    result
   
let throwOnFail result =
    match result with   
    | Fail (boo) ->
        boo |> flatten |> Exception |> raise
    | _ -> ignore

let describeList (items: string seq) = 
    match items with
    | EmptySeq -> 
        "No new items."
    | _  ->
        items 
        |> String.concat "\n"
        |> sprintf "Found new items:\n%s"

let inline describeNewItems toString items =
    items 
    |> Seq.map toString
    |> describeList
    |> ok