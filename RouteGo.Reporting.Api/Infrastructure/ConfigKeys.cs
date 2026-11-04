namespace RouteGo.Reporting.Api.Infrastructure;

/// <summary>
/// The name of every environment-supplied configuration value, spelled exactly once. The names are
/// deliberately FLAT: each maps 1:1 to an environment variable of the same spelling, so no ':' or
/// '__' separator can be mistyped between the deployment template, the pipeline, and the reader
/// here. LocalEntryPoints and every consumer go through these constants, so a rename is a compile
/// error rather than a value that silently arrives null (references/configuration.md).
/// </summary>
public static class ConfigKeys
{
    /// <summary>The AWS SDK's own region variable, and the ONLY region source the app reads.
    /// Lambda sets it; LocalEntryPoints seeds it on a laptop. There is deliberately no app-owned
    /// AWS:Region key that could disagree with the runtime's own value.</summary>
    public const string AwsRegion = "AWS_REGION";

    // ══════════════════════════════════════════════════════════════════════════
    // FILLED BY the sibling slice skill and by feature work: one const per value
    // that varies by environment — a base URL, a client id, a public origin.
    //
    //   public const string BackendBaseUrl = "BackendBaseUrl";
    //
    // Adding one is four more edits, none of them a new file: a local value in
    // LocalEntryPoints, a parameter + env var in the deployment template, a
    // pipeline variable, and a RequireKeys entry if the app cannot start without
    // it. NEVER add a configuration key to an appsettings*.json.
    //
    // Otel:Exporter:* and AWS:Profile do NOT belong here — see
    // references/configuration.md for why those two keep their nested names.
    // ══════════════════════════════════════════════════════════════════════════
    /// <summary>Which Lambda execution model this process runs on — Lambda (one invocation per process,
    /// frozen between them) or ManagedInstances (many concurrent invocations in one always-on process).
    /// It picks the database pool profile (Infrastructure/Pgsql/ConnectionProfile). Set by the deployment
    /// template, never sniffed from the runtime. Required.</summary>
    public const string DbConnectionProfile = "DB_CONNECTION_PROFILE";

    /// <summary>The most database connections one process may hold: 2-4 on Lambda (a handler's opens are
    /// sequential), at least the per-environment concurrency on ManagedInstances. Set by the deployment
    /// template beside DbConnectionProfile. Required.</summary>
    public const string DbMaxPoolSize = "DB_MAX_POOL_SIZE";

    /// <summary>An AWS-compatible endpoint (LocalStack) for SQS and S3 on a developer machine. Left unset
    /// when deployed, so the SDK's regional endpoint and the execution role apply.</summary>
    public const string AwsServiceUrl = "AwsServiceUrl";

    /// <summary>The queue every booking, payment and trip-scheduled event reaches Reporting on.</summary>
    public const string ReportingQueueUrl = "ReportingQueueUrl";

    /// <summary>Where an event Reporting can never apply is set aside for the platform engineer.</summary>
    public const string ReportingDeadLetterQueueUrl = "ReportingDeadLetterQueueUrl";

    /// <summary>The queue the nightly schedule drops its settlement trigger on.</summary>
    public const string ScheduleQueueUrl = "ScheduleQueueUrl";

    /// <summary>Where a settlement trigger that can never be run is set aside.</summary>
    public const string ScheduleDeadLetterQueueUrl = "ScheduleDeadLetterQueueUrl";

    /// <summary>The bucket the settlement reports are written to.</summary>
    public const string ReportsBucket = "ReportsBucket";

    /// <summary>How many minutes a settlement download link stays valid.</summary>
    public const string PresignMinutes = "PresignMinutes";

    /// <summary>Where Identity publishes the keys that sign access tokens.</summary>
    public const string IdentityJwksUrl = "IdentityJwksUrl";

    /// <summary>The issuer every valid access token names.</summary>
    public const string IdentityIssuer = "IdentityIssuer";

    /// <summary>The audience every valid access token names.</summary>
    public const string IdentityAudience = "IdentityAudience";
}
