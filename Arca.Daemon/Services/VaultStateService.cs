using Microsoft.Extensions.Caching.Memory;

namespace Arca.Daemon.Services;

/// <summary>
/// Manages the in-memory state of the vault (Locked/Unlocked) and the derived key.
/// </summary>
public sealed class VaultStateService
{
    private readonly MemoryCache _keyCache;
    private readonly MemoryCacheEntryOptions _cacheOptions;
    private const string DerivedKeyIdentifier = "vault_derived_key";

    public VaultStateService()
    {
        _keyCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 1
        });

        _cacheOptions = new MemoryCacheEntryOptions
        {
            Size = 1,
            Priority = CacheItemPriority.NeverRemove
        };
    }

    public VaultState State { get; private set; } = VaultState.Locked;

    /// <summary>
    /// Unlocks the vault and stores the derived key in protected memory.
    /// </summary>
    public void Unlock(byte[] derivedKey)
    {
        _keyCache.Set(DerivedKeyIdentifier, derivedKey, _cacheOptions);
        State = VaultState.Unlocked;
    }

    /// <summary>
    /// Locks the vault and clears the derived key from memory.
    /// </summary>
    public void Lock()
    {
        if (_keyCache.TryGetValue(DerivedKeyIdentifier, out byte[]? key) && key is not null)
        {
            Array.Clear(key, 0, key.Length);
        }
        _keyCache.Remove(DerivedKeyIdentifier);
        State = VaultState.Locked;
    }

    /// <summary>
    /// Gets the derived key if the vault is unlocked.
    /// </summary>
    public byte[]? GetDerivedKey()
    {
        if (State == VaultState.Locked)
            return null;

        _keyCache.TryGetValue(DerivedKeyIdentifier, out byte[]? key);
        return key;
    }
}

public enum VaultState
{
    Locked,
    Unlocked
}
