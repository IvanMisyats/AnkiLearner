namespace AnkiLearner.Core.Abstractions;

/// <summary>Resolves the authenticated user's id. Every user-data query must be scoped by it.</summary>
public interface ICurrentUser
{
    /// <summary>Throws if the request is not authenticated.</summary>
    Guid UserId { get; }
}
