﻿module Ptp.UpdateCanonicalData.Legislators

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Microsoft.Azure.WebJobs.Host
open Ptp.Core
open Ptp.Model
open Ptp.Queries
open Ptp.Http
open Ptp.Database
open Ptp.Cache
open Ptp.Logging
open System

let legislatorModel json = 
    { Legislator.Id=0; 
    SessionId=0; 
    FirstName=json?firstName.AsString(); 
    LastName=json?lastName.AsString(); 
    Link=json?link.AsString();
    Party=Enum.Parse(typedefof<Party>, json?party.AsString()) :?> Party;
    Chamber=Enum.Parse(typedefof<Chamber>, json?chamber?name.AsString()) :?> Chamber;
    Image=""; 
    District=0; }

/// Fetch URLs for all legislators in the current session.
let fetchAllLegislatorsFromAPI sessionYear = 
    let url = sprintf "/%s/legislators" sessionYear
    let legislatorUrl result = result?link.AsString()
    url
    |> fetchAllPages
    >>= deserializeAs legislatorUrl

/// Filter out URLs for any legislator that we already have in the database    
let filterOutKnownLegislators allUrls = 
    let query = sprintf "SELECT Link from Legislator WHERE SessionId = %s" SessionIdSubQuery
    let byUrl a b = a = b
    let filter knownUrls = 
        allUrls 
        |> except knownUrls byUrl 
        |> ok
    dbQuery<string> query
    >>= filter

/// Get full metadata for legislators that we don't yet know about
let resolveNewLegislators urls =
    urls
    |> fetchAllParallel
    >>= deserializeAs legislatorModel

/// Add new legislator records to the database
let persistNewLegislators legislators = 
    dbCommand InsertLegislator legislators

/// Invalidate the Redis cache key for legislators
let invalidateLegislatorCache legislators = 
    invalidateCache' LegislatorsKey legislators

/// Log the addition of any new legislators
let logNewLegislators legislators = 
    let describer l = 
        sprintf "%s %s (%A, %A)" l.FirstName l.LastName l.Chamber l.Party
    legislators |> describeNewItems describer

/// Define and execute legislators workflow
let updateLegislators (log:TraceWriter) =
    let workflow = 
        getCurrentSessionYear
        >> bind fetchAllLegislatorsFromAPI
        >> bind filterOutKnownLegislators
        >> bind resolveNewLegislators
        >> bind persistNewLegislators
        >> bind invalidateLegislatorCache
        >> bind logNewLegislators

    workflow
    |> evaluate log Command.UpdateLegislators
    |> throwOnFail