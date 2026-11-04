using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace RouteGo.Reporting.Api.Features.ReadModel.ApplyPaymentEvent;

[Collection(DatabaseCollection.Name)]
public sealed class ApplyPaymentEventTests
{
    private static readonly DateTimeOffset _occurredAt = new DateTimeOffset(2026, 10, 4, 9, 30, 0, TimeSpan.Zero);
    private static readonly Guid _bookingId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid _paymentId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private const string PaymentSql = "SELECT status, amount FROM reporting.payments WHERE payment_id = @PaymentId";
    private const string BookingSql = """
        SELECT status, amount, refund_amount AS RefundAmount, refund_completed_at AS RefundCompletedAt
        FROM reporting.bookings WHERE booking_id = @BookingId
        """;
    private const string BookingCountSql = "SELECT count(*) FROM reporting.bookings";
    private const string SeedBookingSql = """
        INSERT INTO reporting.bookings (booking_id, status, amount, status_changed_at)
        VALUES (@BookingId, 'cancelled', 2400, @At)
        """;

    private readonly DatabaseFixture _database;

    public ApplyPaymentEventTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task HandleAsync_RefundCompleted_RecordsAmountAndTimeOnTheBooking()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        await _database.SeedAsync(SeedBookingSql, new { BookingId = _bookingId, At = _occurredAt.AddDays(-1) });
        ApplyPaymentEventHandler handler = CreateHandler();

        // Act
        EventOutcome outcome = await handler.HandleAsync(Refund(2160), CancellationToken.None);

        // Assert
        Assert.Equal(EventOutcomeKind.Applied, outcome.Kind);
        BookingRow row = await TestQueries.QuerySingleAsync<BookingRow>(_database, BookingSql, new { BookingId = _bookingId });
        Assert.Equal(2160, row.RefundAmount);
        Assert.Equal(_occurredAt, row.RefundCompletedAt);
        Assert.Equal(2400, row.Amount);
    }

    [Fact]
    public async Task HandleAsync_SucceededAndFailedPayments_AreHeldByOutcome()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        ApplyPaymentEventHandler handler = CreateHandler();
        var failedPaymentId = Guid.NewGuid();

        // Act
        await handler.HandleAsync(Payment("PaymentSucceeded", _paymentId, "\"amount\":2400"), CancellationToken.None);
        await handler.HandleAsync(Payment("PaymentFailed", failedPaymentId, "\"reason\":\"declined\""), CancellationToken.None);

        // Assert
        PaymentRow succeeded = await TestQueries.QuerySingleAsync<PaymentRow>(_database, PaymentSql, new { PaymentId = _paymentId });
        PaymentRow failed = await TestQueries.QuerySingleAsync<PaymentRow>(_database, PaymentSql, new { PaymentId = failedPaymentId });
        Assert.Equal("succeeded", succeeded.Status);
        Assert.Equal(2400, succeeded.Amount);
        Assert.Equal("failed", failed.Status);
        Assert.Equal(0, await _database.QueryScalarAsync<int>(BookingCountSql));
    }

    [Fact]
    public async Task HandleAsync_RefundTwice_KeepsTheFirst()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        ApplyPaymentEventHandler handler = CreateHandler();
        await handler.HandleAsync(Refund(2160), CancellationToken.None);

        // Act
        await handler.HandleAsync(Refund(100), CancellationToken.None);

        // Assert
        BookingRow row = await TestQueries.QuerySingleAsync<BookingRow>(_database, BookingSql, new { BookingId = _bookingId });
        Assert.Equal("cancelled", row.Status);
        Assert.Equal(2160, row.RefundAmount);
    }

    [Fact]
    public async Task HandleAsync_RefundOfNothing_RecordsNothing()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        ApplyPaymentEventHandler handler = CreateHandler();

        // Act
        EventOutcome outcome = await handler.HandleAsync(Refund(0), CancellationToken.None);

        // Assert
        Assert.Equal(EventOutcomeKind.Applied, outcome.Kind);
        Assert.Equal(0, await _database.QueryScalarAsync<int>(BookingCountSql));
    }

    [Fact]
    public async Task HandleAsync_SucceededWithoutAmount_IsRejected()
    {
        // Arrange
        await _database.ResetBusinessTablesAsync();
        ApplyPaymentEventHandler handler = CreateHandler();

        // Act
        EventOutcome outcome = await handler.HandleAsync(Payment("PaymentSucceeded", _paymentId, "\"currency\":\"INR\""), CancellationToken.None);

        // Assert
        Assert.Equal(EventOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal(EventOutcome.InvalidEventData, outcome.Reason);
    }

    private ApplyPaymentEventHandler CreateHandler()
    {
        IWriterConnectionFactory writerFactory = TestWriters.CreateWriter(_database);
        return new ApplyPaymentEventHandler(writerFactory, NullLogger<ApplyPaymentEventHandler>.Instance);
    }

    private static ReceivedEvent Refund(long amount)
    {
        string json = $$"""{"bookingId":"{{_bookingId}}","paymentId":"{{_paymentId}}","amount":{{amount}},"reason":"cancelled"}""";
        return Wrap("RefundCompleted", json);
    }

    private static ReceivedEvent Payment(string eventType, Guid paymentId, string extraMember)
    {
        string json = $$"""{"bookingId":"{{_bookingId}}","paymentId":"{{paymentId}}",{{extraMember}}}""";
        return Wrap(eventType, json);
    }

    private static ReceivedEvent Wrap(string eventType, string dataJson)
    {
        using var document = JsonDocument.Parse(dataJson);
        return new ReceivedEvent
        {
            EventId = "evt-" + Guid.NewGuid(),
            EventType = eventType,
            OccurredAt = _occurredAt,
            CorrelationId = "corr-1",
            Data = document.RootElement.Clone(),
        };
    }

    private sealed class BookingRow
    {
        public string Status { get; set; } = string.Empty;

        public long? Amount { get; set; }

        public long? RefundAmount { get; set; }

        public DateTimeOffset? RefundCompletedAt { get; set; }
    }

    private sealed class PaymentRow
    {
        public string Status { get; set; } = string.Empty;

        public long? Amount { get; set; }
    }
}
