using CustomersApi.Data;
using Grpc.Core;
using Lab.Contracts.Customers;
using Microsoft.EntityFrameworkCore;

namespace CustomersApi.Grpc;

/// <summary>Server side of customers.proto, called by other services over HTTP/2 on port 8081.</summary>
public class CustomerGrpcService(CustomersDbContext db, ILogger<CustomerGrpcService> logger) : CustomerService.CustomerServiceBase
{
    public override async Task<CustomerReply> GetCustomer(GetCustomerRequest request, ServerCallContext context)
    {
        logger.LogDebug("gRPC GetCustomer({CustomerId}) called by {Peer}", request.Id, context.Peer);
        var customer = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.Id, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Customer {request.Id} not found"));

        return new CustomerReply { Id = customer.Id, Name = customer.Name, Email = customer.Email, City = customer.City };
    }
}
