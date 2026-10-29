namespace OrdersApi.Data;

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public required string Product { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = OrderStatus.Created;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class OrderStatus
{
    public const string Created = "Created";     // saved, but billing-api could not be reached yet (InvoiceRetryWorker retries)
    public const string Invoiced = "Invoiced";   // billing-api created the invoice
}
