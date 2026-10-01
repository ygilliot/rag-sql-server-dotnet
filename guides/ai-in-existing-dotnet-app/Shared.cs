namespace AiInExistingApp;

// Stand-ins for what the existing application already has. Not part of the guide's snippets.

public sealed record Order(string Number, string CustomerId, string Status);

/// <summary>The existing repository. The AI code calls it, it does not replace it.</summary>
public interface IOrderRepository
{
    Task<Order?> FindAsync(string orderNumber, CancellationToken cancellationToken = default);

    Task CancelAsync(string orderNumber, string reason, CancellationToken cancellationToken = default);
}

/// <summary>The signed-in user, resolved on the server side (token, session, claims).</summary>
public interface ICurrentUser
{
    string CustomerId { get; }
}

/// <summary>A customer message waiting to be triaged.</summary>
public sealed record IncomingRequest(string Id, string CustomerId, string Text);

/// <summary>Where the application stores requests and their triage decision.</summary>
public interface IRequestStore
{
    /// <summary>Returns false when this request was already taken: a message delivered twice is triaged once.</summary>
    Task<bool> TryBeginAsync(string requestId, CancellationToken cancellationToken = default);

    Task SaveAsync(string requestId, TriageDecision decision, CancellationToken cancellationToken = default);
}
