using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BcContainerCreator.Core.Models;
using Microsoft.Extensions.Logging;

namespace BcContainerCreator.Core.Containers;

/// <summary>
/// JSON-Dateien pro Container unter
/// <c>%APPDATA%\BcContainerCreator\containers\&lt;name&gt;.json</c>.
/// Passwort wird via <see cref="ProtectedData"/> CurrentUser verschlüsselt.
/// </summary>
public sealed class ContainerMetadataStore : IContainerMetadataStore
{
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("BcContainerCreator-v1");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly ILogger<ContainerMetadataStore> _logger;
    private readonly string _root;

    public ContainerMetadataStore(ILogger<ContainerMetadataStore> logger)
    {
        _logger = logger;
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BcContainerCreator", "containers");
        Directory.CreateDirectory(_root);
    }

    public async Task SaveAsync(
        string containerName,
        DateTimeOffset createdAt,
        AuthType authType,
        string username,
        SecureString? password,
        ArtifactType artifactType,
        string country,
        string versionSelector,
        string? resolvedBuild,
        string webClientUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);

        var cipher = password is null ? null : EncryptSecure(password);
        var meta = new ContainerMetadata(
            Name: containerName,
            CreatedAt: createdAt,
            AuthType: authType,
            Username: username,
            PasswordCipher: cipher,
            ArtifactType: artifactType,
            Country: country,
            VersionSelector: versionSelector,
            ResolvedBuild: resolvedBuild,
            WebClientUrl: webClientUrl);

        var path = PathFor(containerName);
        var json = JsonSerializer.Serialize(meta, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Metadaten gespeichert: {Path}", path);
    }

    public async Task<ContainerMetadata?> LoadAsync(string containerName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        var path = PathFor(containerName);
        if (!File.Exists(path)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ContainerMetadata>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata-Load fehlgeschlagen für {Name}", containerName);
            return null;
        }
    }

    public string? DecryptPassword(byte[]? cipher)
    {
        if (cipher is null || cipher.Length == 0) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(cipher, DpapiEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DPAPI-Decrypt fehlgeschlagen — falscher User oder Profil-Migration?");
            return null;
        }
    }

    public Task DeleteAsync(string containerName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        try
        {
            var path = PathFor(containerName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata-Delete fehlgeschlagen für {Name}", containerName);
        }
        return Task.CompletedTask;
    }

    private string PathFor(string name)
    {
        // Container-Namen sind a-z A-Z 0-9 - _ . — direkt als Dateiname verwendbar.
        return Path.Combine(_root, $"{name}.json");
    }

    private static byte[] EncryptSecure(SecureString password)
    {
        // SecureString → BSTR → byte[] → DPAPI. Plain-Bytes werden danach gewiped.
        IntPtr bstr = IntPtr.Zero;
        byte[]? plain = null;
        try
        {
            bstr = Marshal.SecureStringToBSTR(password);
            var len = Marshal.ReadInt32(bstr, -4); // BSTR length-prefix
            plain = new byte[len];
            Marshal.Copy(bstr, plain, 0, len);
            // BSTR ist UTF-16LE — DPAPI verschlüsselt das transparent.
            return ProtectedData.Protect(plain, DpapiEntropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            if (bstr != IntPtr.Zero) Marshal.ZeroFreeBSTR(bstr);
            if (plain is not null) Array.Clear(plain, 0, plain.Length);
        }
    }
}
