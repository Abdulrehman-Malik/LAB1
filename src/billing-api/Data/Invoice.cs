namespace BillingApi.Data;

public class Invoice
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = InvoiceStatus.Pending;
    public string? PaymentReference { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }

    // Set by notification-worker (plain SQL, not EF) once the "invoice received" email is sent
    public bool EmailSent { get; set; }
}

public static class InvoiceStatus
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";
}
