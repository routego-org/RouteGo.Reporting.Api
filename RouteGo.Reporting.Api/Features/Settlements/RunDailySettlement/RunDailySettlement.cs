using System.Globalization;
using System.Text;
using Amazon.S3.Model;

namespace RouteGo.Reporting.Api.Features.Settlements.RunDailySettlement;

/// <summary>One operator's figures for the day being settled, in whole rupees.</summary>
public sealed class OperatorFigures
{
    public required string OperatorId { get; init; }

    public required long Gross { get; init; }

    public required long Refunds { get; init; }

    public long Net
    {
        get { return Gross - Refunds; }
    }
}

/// <summary>Finds the operators that have activity on a day but no settlement yet.</summary>
internal sealed class PendingSettlementReader
{
    // Gross counts on the day a booking was confirmed and a refund on the day it completed. Only
    // operators with activity appear, and only those without a settlement yet.
    private const string SelectPendingSql = """
        WITH gross AS (
            SELECT operator_id, sum(amount) AS gross
            FROM reporting.bookings
            WHERE operator_id IS NOT NULL AND confirmed_at >= @FromAt AND confirmed_at < @ToAt
            GROUP BY operator_id
        ),
        refunded AS (
            SELECT operator_id, sum(refund_amount) AS refunds
            FROM reporting.bookings
            WHERE operator_id IS NOT NULL AND refund_completed_at >= @FromAt AND refund_completed_at < @ToAt
            GROUP BY operator_id
        )
        SELECT COALESCE(gross.operator_id, refunded.operator_id) AS OperatorId,
               COALESCE(gross.gross, 0)::bigint AS Gross,
               COALESCE(refunded.refunds, 0)::bigint AS Refunds
        FROM gross
        FULL JOIN refunded ON refunded.operator_id = gross.operator_id
        WHERE NOT EXISTS (
            SELECT 1 FROM reporting.settlements s
            WHERE s.settlement_date = @Day AND s.operator_id = COALESCE(gross.operator_id, refunded.operator_id))
        ORDER BY 1
        """;

    private readonly IReaderConnectionFactory _readerFactory;

    public PendingSettlementReader(IReaderConnectionFactory readerFactory)
    {
        _readerFactory = readerFactory;
    }

    public async Task<List<OperatorFigures>> ListAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var parameters = new { Day = day, FromAt = BusinessDay.StartOf(day), ToAt = BusinessDay.EndOf(day) };
        IEnumerable<OperatorFigures> rows = await _readerFactory.ExecuteReadAsync(
            connection => connection.QueryAsync<OperatorFigures>(new CommandDefinition(SelectPendingSql, parameters, cancellationToken: cancellationToken)),
            cancellationToken);
        return rows.ToList();
    }
}

/// <summary>
/// Writes a settlement's report, a one-line CSV, to the reports bucket. The method is virtual so a test
/// can stand in for the bucket.
/// </summary>
public class SettlementReportWriter
{
    private const string ContentType = "text/csv";
    private const string Header = "settlementDate,operatorId,gross,refunds,net,currency";

    private readonly IAmazonS3 _s3;
    private readonly string _bucket;

    public SettlementReportWriter(IAmazonS3 s3, string bucket)
    {
        _s3 = s3;
        _bucket = bucket;
    }

    /// <summary>Stores the report for one settlement, encrypted at rest.</summary>
    public virtual async Task WriteAsync(DateOnly settlementDate, OperatorFigures figures, CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = SettlementReportKey.For(settlementDate, figures.OperatorId),
            ContentBody = ToCsv(settlementDate, figures),
            ContentType = ContentType,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
        };
        await _s3.PutObjectAsync(request, cancellationToken);
    }

    private static string ToCsv(DateOnly settlementDate, OperatorFigures figures)
    {
        var line = new StringBuilder();
        line.Append(CultureInfo.InvariantCulture, $"{settlementDate:yyyy-MM-dd},{Quote(figures.OperatorId)},");
        line.Append(CultureInfo.InvariantCulture, $"{figures.Gross},{figures.Refunds},{figures.Net},INR");
        return Header + "\n" + line + "\n";
    }

    // A value with a comma, quote or line break is quoted, with quotes doubled, so it stays one field.
    private static string Quote(string value)
    {
        bool needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!needsQuotes)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
