namespace RagSqlServer.Security;

/// <summary>
/// The authenticated caller, as your application already knows it.
/// Roles come from the server-side identity (claims, session), never from the model.
/// </summary>
public interface ICallerContext
{
    IReadOnlyCollection<string> Roles { get; }
}

/// <summary>A fixed caller, for samples and tests.</summary>
public sealed record StaticCallerContext(params string[] RoleNames) : ICallerContext
{
    public IReadOnlyCollection<string> Roles => RoleNames;
}
