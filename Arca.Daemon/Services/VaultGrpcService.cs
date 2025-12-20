using Arca.Grpc;
using Grpc.Core;

namespace Arca.Daemon.Services;
// implementación del servicio gRPC para manejar solicitudes relacionadas con el vault
public sealed class VaultGrpcService : VaultService.VaultServiceBase
{
    private readonly VaultStateService _vaultState;
    private readonly ILogger<VaultGrpcService> _logger;

    public VaultGrpcService(VaultStateService vaultState, ILogger<VaultGrpcService> logger)
    {
        _vaultState = vaultState;
        _logger = logger;
    }

    public override Task<StatusResponse> GetStatus(StatusRequest request, ServerCallContext context)
    {
        _logger.LogDebug("GetStatus called");

        return Task.FromResult(new StatusResponse
        {
            IsUnlocked = _vaultState.State == VaultState.Unlocked,
            VaultPath = _vaultState.VaultPath ?? "",
            SecretCount = _vaultState.SecretCount
        });
    }

    public override Task<GetSecretResponse> GetSecret(GetSecretRequest request, ServerCallContext context)
    {
        _logger.LogDebug("GetSecret called for key: {Key}", request.Key);

        // Verificar que el vault esté desbloqueado
        if (_vaultState.State == VaultState.Locked)
        {
            return Task.FromResult(new GetSecretResponse
            {
                Found = false,
                Error = "Vault is locked. Please unlock it first."
            });
        }

        var secret = _vaultState.GetSecret(request.Key);

        if (secret is null)
        {
            return Task.FromResult(new GetSecretResponse
            {
                Found = false,
                Key = request.Key
            });
        }

        return Task.FromResult(new GetSecretResponse
        {
            Found = true,
            Key = secret.Key,
            Value = secret.Value,
            Description = secret.Description ?? ""
        });
    }

    public override Task<GetSecretsResponse> GetSecrets(GetSecretsRequest request, ServerCallContext context)
    {
        _logger.LogDebug("GetSecrets called for {Count} keys", request.Keys.Count);

        var response = new GetSecretsResponse();

        // Verificar que el vault esté desbloqueado
        if (_vaultState.State == VaultState.Locked)
        {
            response.Error = "Vault is locked. Please unlock it first.";
            return Task.FromResult(response);
        }

        var (found, notFound) = _vaultState.GetSecrets(request.Keys);

        foreach (var secret in found)
        {
            response.Secrets.Add(new SecretEntry
            {
                Key = secret.Key,
                Value = secret.Value,
                Description = secret.Description ?? ""
            });
        }

        response.NotFoundKeys.AddRange(notFound);

        return Task.FromResult(response);
    }

    public override Task<ListKeysResponse> ListKeys(ListKeysRequest request, ServerCallContext context)
    {
        _logger.LogDebug("ListKeys called with filter: {Filter}", request.Filter);

        var response = new ListKeysResponse();

        // Verificar que el vault esté desbloqueado
        if (_vaultState.State == VaultState.Locked)
        {
            response.Error = "Vault is locked. Please unlock it first.";
            return Task.FromResult(response);
        }

        var keys = _vaultState.ListKeys(request.Filter);
        response.Keys.AddRange(keys);

        return Task.FromResult(response);
    }

    public override Task<KeyExistsResponse> KeyExists(KeyExistsRequest request, ServerCallContext context)
    {
        _logger.LogDebug("KeyExists called for key: {Key}", request.Key);

        // Si el vault está bloqueado, siempre retorna false
        if (_vaultState.State == VaultState.Locked)
        {
            return Task.FromResult(new KeyExistsResponse { Exists = false });
        }

        return Task.FromResult(new KeyExistsResponse
        {
            Exists = _vaultState.KeyExists(request.Key)
        });
    }
}
