namespace SkToAgentFramework;

/// <summary>The existing repository the tools call. Not part of the guide's snippets.</summary>
public interface IOrderRepository
{
    Task<Order?> FindAsync(string orderNumber);
}

public sealed record Order(string Number, string Status);
