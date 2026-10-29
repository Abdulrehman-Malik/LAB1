using System.Diagnostics;
using Grpc.Core;
using Lab.Contracts.Customers;
using Lab.Shared;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Data.SqlClient;
using MimeKit;

namespace NotificationWorker;

/// <summary>
/// Every Worker:IntervalSeconds: find invoices that haven't been emailed, email the customer, record it in MongoDB.
/// <list type="number">
/// <item>Claim up to 10 invoices with ONE atomic SQL statement (safe even if two worker replicas run at once)</item>
/// <item>Ask customers-api (gRPC) for the customer's name and email</item>
/// <item>Send the email over SMTP (Mailpit catches it; open http://localhost:8025)</item>
/// <item>Write an audit event to MongoDB; on failure, release the invoice so the next run retries it</item>
/// </list>
/// Note the coupling: this worker reads billing-api's table (SalesDb.billing.Invoices) with plain SQL.
/// If billing-api renames a column, this worker breaks, and the compiler can't warn you. That's the cost of a shared database.
/// </summary>
public class InvoiceEmailWorker(
    IConfiguration config,
    CustomerService.CustomerServiceClient customers,
    AuditLog audit,
    ILogger<InvoiceEmailWorker> logger) : BackgroundService
{
    private record ClaimedInvoice(int Id, int OrderId, int CustomerId, decimal Amount);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(config.GetValue("Worker:IntervalSeconds", 30));
        var connectionString = DatabaseExtensions.SqlServerConnectionString(config, "SalesDb");
        logger.LogInformation("Checking for new invoices every {Seconds} s", interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Each run gets its own trace id, shared by its log lines, its gRPC calls and its audit events
            using (new Activity("InvoiceEmailRun").Start())
            {
                try
                {
                    await RunOnceAsync(connectionString, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError("Run failed, will try again in {Seconds} s: {Message}", interval.TotalSeconds, ex.Message);
                }
            }
            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task RunOnceAsync(string connectionString, CancellationToken ct)
    {
        await using var sql = new SqlConnection(connectionString);
        await sql.OpenAsync(ct);

        // Atomic claim: mark and return in one statement, so no other worker can pick the same invoices
        var claimed = new List<ClaimedInvoice>();
        await using (var claim = new SqlCommand("""
            UPDATE TOP (10) billing.Invoices
            SET EmailSent = 1
            OUTPUT inserted.Id, inserted.OrderId, inserted.CustomerId, inserted.Amount
            WHERE EmailSent = 0
            """, sql))
        await using (var reader = await claim.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                claimed.Add(new ClaimedInvoice(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetDecimal(3)));
        }

        if (claimed.Count == 0)
        {
            logger.LogDebug("No new invoices");
            return;
        }
        logger.LogInformation("Claimed {Count} invoice(s) to email", claimed.Count);

        foreach (var invoice in claimed)
        {
            try
            {
                await SendEmailAsync(invoice, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Email for invoice {InvoiceId} failed, releasing it for the next run: {Message}", invoice.Id, ex.Message);
                await using var release = new SqlCommand("UPDATE billing.Invoices SET EmailSent = 0 WHERE Id = @id", sql);
                release.Parameters.AddWithValue("@id", invoice.Id);
                await release.ExecuteNonQueryAsync(ct);
                await audit.WriteAsync(NewEvent("InvoiceEmailFailed", invoice, recipient: null, detail: ex.Message), ct);
            }
        }
    }

    private async Task SendEmailAsync(ClaimedInvoice invoice, CancellationToken ct)
    {
        CustomerReply customer;
        try
        {
            customer = await customers.GetCustomerAsync(new GetCustomerRequest { Id = invoice.CustomerId },
                deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
        }
        catch (RpcException ex)
        {
            throw new InvalidOperationException($"customers-api lookup for customer {invoice.CustomerId} failed: {ex.Status.StatusCode}", ex);
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("LAB1 Billing", config["Smtp:From"] ?? "billing@lab.local"));
        message.To.Add(new MailboxAddress(customer.Name, customer.Email));
        message.Subject = $"Invoice #{invoice.Id} for your order #{invoice.OrderId}";
        message.Body = new TextPart("plain")
        {
            Text = $"""
                Hello {customer.Name},

                We received your order #{invoice.OrderId}.
                Invoice #{invoice.Id}: PKR {invoice.Amount:N2}

                Sent by {Environment.MachineName}, trace id {Activity.Current?.TraceId}
                """,
        };

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(config["Smtp:Host"] ?? "localhost",config.GetValue("Smtp:Port", 1025), SecureSocketOptions.None, ct);
        await smtp.SendAsync(message, ct);
        await smtp.DisconnectAsync(quit: true, ct);

        logger.LogInformation("Emailed invoice {InvoiceId} to {Email}", invoice.Id, customer.Email);
        await audit.WriteAsync(NewEvent("InvoiceEmailSent", invoice, customer.Email, detail: message.Subject), ct);
    }

    private static AuditEvent NewEvent(string name, ClaimedInvoice invoice, string? recipient, string? detail) => new()
    {
        Event = name,
        InvoiceId = invoice.Id,
        OrderId = invoice.OrderId,
        Recipient = recipient,
        Detail = detail,
        TraceId = Activity.Current?.TraceId.ToString(),
    };
}
