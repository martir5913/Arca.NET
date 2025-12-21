using Arca.Core.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace Arca.Daemon.Services;

// gestión del estado del vault en memoria
public sealed class VaultStateService
{
    private readonly object _lock = new();
    private readonly MemoryCache _keyCache;
    private readonly MemoryCacheEntryOptions _cacheOptions;
    private byte[]? _derivedKey;
    private List<SecretEntry> _secrets = [];
    private string? _vaultPath;
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

    // desbloquea el vault con la clave derivada y los secretos en memoria
    public void Unlock(byte[] derivedKey, IEnumerable<SecretEntry> secrets, string vaultPath)
    {
        lock (_lock)
        {
            _derivedKey = derivedKey;
            _secrets = secrets.ToList();
            _vaultPath = vaultPath;
            State = VaultState.Unlocked;
        }
    }

    // bloquea el vault y limpia los datos sensibles de la memoria
    public void Lock()
    {
        lock (_lock)
        {
            if (_derivedKey is not null)
            {
                Array.Clear(_derivedKey, 0, _derivedKey.Length);
                _derivedKey = null;
            }

            // Limpiar valores sensibles de los secretos
            foreach (var secret in _secrets)
            {
                // Los records son inmutables, solo limpiamos la lista
            }
            _secrets.Clear();

            _vaultPath = null;
            State = VaultState.Locked;
        }
    }

    // Actualiza la lista de secretos en memoria.
    public void UpdateSecrets(IEnumerable<SecretEntry> secrets)
    {
        lock (_lock)
        {
            _secrets = secrets.ToList();
        }
    }

    public SecretEntry? GetSecret(string key)
    {
        lock (_lock)
        {
            return _secrets.FirstOrDefault(s =>
                s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        }
    }

    public (List<SecretEntry> found, List<string> notFound) GetSecrets(IEnumerable<string> keys)
    {
        lock (_lock)
        {
            var found = new List<SecretEntry>();
            var notFound = new List<string>();

            foreach (var key in keys)
            {
                var secret = _secrets.FirstOrDefault(s =>
                    s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

                if (secret is not null)
                    found.Add(secret);
                else
                    notFound.Add(key);
            }

            return (found, notFound);
        }
    }

    public IReadOnlyList<string> ListKeys(string? filter = null)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return _secrets.Select(s => s.Key).ToList();
            }

            return _secrets
                .Where(s => s.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Key)
                .ToList();
        }
    }

    public bool KeyExists(string key)
    {
        lock (_lock)
        {
            return _secrets.Any(s =>
                s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        }
    }

    public int SecretCount
    {
        get
        {
            lock (_lock)
            {
                return _secrets.Count;
            }
        }
    }

    public string? VaultPath
    {
        get
        {
            lock (_lock)
            {
                return _vaultPath;
            }
        }
    }
}

public enum VaultState
{
    Locked,
    Unlocked
}
