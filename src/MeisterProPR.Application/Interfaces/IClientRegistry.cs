namespace MeisterProPR.Application.Interfaces;

/// <summary>
///     Registry of known clients and their keys.
/// </summary>
public interface IClientRegistry
{
    /// <summary>Returns true if the provided client key is registered and valid.</summary>
    bool IsValidKey(string clientKey);

    /// <summary>Returns the client's unique ID for a valid active key, or null if not found/inactive.</summary>
    Task<Guid?> GetClientIdByKeyAsync(string key, CancellationToken ct = default);
}