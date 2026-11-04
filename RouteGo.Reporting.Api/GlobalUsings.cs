// Slice files carry no using directives — this file holds the shared set.
// The sibling slice skill and database setup EXTEND this list beneath the '// FILLED BY' marker below.
global using System.Text.Json;          // JsonSerializer, WireJson.Options
global using Microsoft.AspNetCore.Mvc;
global using RouteGo.Reporting.Api.Common;             // HttpClients, WireJson
global using RouteGo.Reporting.Api.Common.Ports;       // IClock
global using RouteGo.Reporting.Api.Common.Problems;    // ProblemResult, ProblemTypes (catalog projection)
global using RouteGo.Reporting.Api.Common.Behaviors;   // UnhandledExceptionHandler, ResponseMetadataMiddleware, ResponseHeadersTransformer
// FILLED BY the sibling slice skill — and by skills-db-postgres (copy-assets.ps1) at database setup:
global using System.Data.Common;   // skills-db-postgres (copy-assets.ps1)
global using Dapper;   // skills-db-postgres (copy-assets.ps1)
global using Amazon.SQS;   // IAmazonSQS - the reporting event and schedule queues
global using Amazon.S3;    // IAmazonS3 - the settlement reports bucket
global using RouteGo.Reporting.Api.Common.Messaging; // EventQueueService, IEventWorker, ReceivedEvent - the queue workers
global using FluentValidation;                     // slice validators
global using FluentValidation.Results;             // ValidationResult in the controller
global using Microsoft.AspNetCore.Authorization;   // [Authorize] on slice endpoints
global using RouteGo.Reporting.Api.Common.Security;  // Policies, Caller
global using RouteGo.Reporting.Api.Common.Sales;     // BusinessDay, SalesSummary, SalesSummaryReader
global using RouteGo.Reporting.Api.Infrastructure;   // ConfigKeys - slice settings read from configuration
