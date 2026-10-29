namespace CustomersApi.Data;

public class Customer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string City { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
