using BillingApi.Data;
using Grpc.Core;
using Lab.Contracts.Billing;
using Microsoft.EntityFrameworkCore;

namespace BillingApi.Grpc;

/// <summary>Server side of billing.proto, called by orders-api over HTTP/2 on port 8081.</summary>
public class BillingGrpcService(BillingDbContext db, ILogger<BillingGrpcService> logger) : BillingService.BillingServiceBase
{
    public override async Task<InvoiceReply> CreateInvoice(CreateInvoiceRequest request, ServerCallContext context)
    {
        // Idempotent: if orders-api retries the call, return the invoice that already exists
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.OrderId == request.OrderId, context.CancellationToken);
        if (invoice is null)
        {
            invoice = new Invoice { OrderId = request.OrderId, CustomerId = request.CustomerId, Amount = request.AmountCents / 100m };
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync(context.CancellationToken);
            logger.LogInformation("Invoice {InvoiceId} created for order {OrderId}: {Amount}", invoice.Id, invoice.OrderId, invoice.Amount);
        }
        else
        {
            logger.LogInformation("Invoice {InvoiceId} already exists for order {OrderId} (retry), returning it", invoice.Id, invoice.OrderId);
        }
        return ToReply(invoice);
    }

    public override async Task<InvoiceReply> GetInvoiceByOrder(GetInvoiceByOrderRequest request, ServerCallContext context)
    {
        var invoice = await db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.OrderId == request.OrderId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"No invoice for order {request.OrderId}"));
        return ToReply(invoice);
    }

    private static InvoiceReply ToReply(Invoice invoice) => new()
    {
        Id = invoice.Id,
        OrderId = invoice.OrderId,
        AmountCents = (long)(invoice.Amount * 100),
        Status = invoice.Status,
        EmailSent = invoice.EmailSent,
    };
}
