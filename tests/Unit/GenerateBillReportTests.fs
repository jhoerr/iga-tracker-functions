﻿module GenerateBillReportTests

open Chessie.ErrorHandling
open Swensen.Unquote
open System.Net
open Xunit
open System.Net.Http
open Ptp.Common.Core
open Ptp.API.GetBillReport

[<Fact>] 
let ``User id is required (no content)`` ()=
    let req = new HttpRequestMessage()
    let expected = Result.FailWith((RequestValidationError("A user id is expected in the form '{ Id: INT }'")))
    test <@ deserializeId req = expected @>

[<Fact>] 
let ``User id is required (empty content)`` ()=
    let req = new HttpRequestMessage(Content=new StringContent(""))
    let expected = Result.FailWith((RequestValidationError("A user id is expected in the form '{ Id: INT }'")))
    test <@ deserializeId req = expected @>
