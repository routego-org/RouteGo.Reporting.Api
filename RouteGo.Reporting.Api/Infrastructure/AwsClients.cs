using Amazon.Runtime;

namespace RouteGo.Reporting.Api.Infrastructure;

/// <summary>
/// The AWS clients this service uses: real AWS through the execution role when deployed, or the one
/// AWS-compatible endpoint a developer machine names (LocalStack) for every service at once. Each
/// client is thread-safe, so one instance serves the whole process.
/// </summary>
public static class AwsClients
{
    // LocalStack checks no signatures; the placeholder keys only let the SDK sign, and are worthless elsewhere.
    private const string LocalAccessKey = "local";
    private const string LocalSecretKey = "local";

    /// <summary>The S3 client the settlement reports are written to and downloaded from.</summary>
    public static IAmazonS3 CreateS3(IConfiguration configuration)
    {
        string? serviceUrl = configuration[ConfigKeys.AwsServiceUrl];
        if (string.IsNullOrEmpty(serviceUrl))
        {
            return new AmazonS3Client();
        }

        // LocalStack serves buckets by path, not by a bucket-named host.
        // Presigned links must carry the endpoint's own scheme, which the SDK takes from UseHttp, not from the URL.
        bool isPlainHttp = serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        var config = new AmazonS3Config { ServiceURL = serviceUrl, ForcePathStyle = true, UseHttp = isPlainHttp };
        return new AmazonS3Client(LocalCredentials(), config);
    }

    /// <summary>The SQS client Reporting reads events and settlement triggers with.</summary>
    public static IAmazonSQS CreateSqs(IConfiguration configuration)
    {
        string? serviceUrl = configuration[ConfigKeys.AwsServiceUrl];
        if (string.IsNullOrEmpty(serviceUrl))
        {
            return new AmazonSQSClient();
        }

        var config = new AmazonSQSConfig { ServiceURL = serviceUrl };
        return new AmazonSQSClient(LocalCredentials(), config);
    }

    private static BasicAWSCredentials LocalCredentials()
    {
        return new BasicAWSCredentials(LocalAccessKey, LocalSecretKey);
    }
}
