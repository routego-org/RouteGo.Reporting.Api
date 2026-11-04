// Slice test files (linked in from {Project}/Features/**/*.Tests.cs) carry no using directives —
// this file holds the test project's shared set. The sibling slice skill EXTENDS it as its
// test helpers need more (e.g. Testcontainers, AngleSharp).
global using Xunit;
global using RouteGo.Reporting.Api.Common;             // HttpClients, WireJson
global using RouteGo.Reporting.Api.Tests;              // ApiFactory
global using RouteGo.Reporting.Api.Tests.Database;     // DatabaseFixture, DatabaseCollection
global using RouteGo.Reporting.Api.Tests.Support;      // FixedClock, TestQueries, TestWriters
global using RouteGo.Reporting.Api.Common.Ports;       // IClock and the ports the fakes stand in for
global using RouteGo.Reporting.Api.Common.Messaging;   // ReceivedEvent, EventOutcome
global using System.Net;                             // HttpStatusCode in slice tests
