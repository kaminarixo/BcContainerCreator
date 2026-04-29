using System.IO;
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
/// <para>
/// Encrypt/Decrypt nutzen konsistent UTF-8 für den Plain-Text-Roundtrip:
/// <see cref="SecureString"/> → Plain-String → UTF-8-Bytes → DPAPI →
/// Cipher-Bytes; und zurück. Ein älterer Stand schrieb BSTR-Bytes (UTF-16LE)
/// und las sie als UTF-8 zurück — der Roundtrip lieferte einen Müll-String
/// mit eingebetteten Null-Bytes.
/// </para>
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
        : this(logger, DefaultRoot())
    {
    }

    /// <summary>
    /// Test-Konstruktor mit explizitem Root-Verzeichnis. Nicht für Produktiv-DI.
    /// </summary>
    public ContainerMetadataStore(ILogger<ContainerMetadataStore> logger, string rootDirectory)
    {
        _logger = logger;
        _root = rootDirectory;
        Directory.CreateDirectory(_root);
    }

    private static string DefaultRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BcContainerCreator", "containers");

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

        var cipher = password is null || password.Length == 0 ? null : EncryptSecure(password);
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
            // Encrypt-Pfad legt UTF-8-Bytes ab — siehe EncryptSecure unten.
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

    /// <summary>
    /// SecureString → Plain-String → UTF-8-Bytes → DPAPI. Der Plain-Buffer
    /// wird nach dem Encrypt explizit auf 0 überschrieben, damit das Klartext-
    /// Passwort möglichst kurz im Managed-Heap liegt.
    /// </summary>
    private static byte[] EncryptSecure(SecureString password)
    {
        var plain = SecureStringToPlain(password);
        byte[]? bytes = null;
        try
        {
            bytes = Encoding.UTF8.GetBytes(plain);
            return ProtectedData.Protect(bytes, DpapiEntropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            if (bytes is not null) Array.Clear(bytes, 0, bytes.Length);
        }
    }

    private static string SecureStringToPlain(SecureString ss)
    {
        if (ss.Length == 0) return string.Empty;
        IntPtr bstr = IntPtr.Zero;
        try
        {
            bstr = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(ss);
            return System.Runtime.InteropServices.Marshal.PtrToStringBSTR(bstr) ?? string.Empty;
        }
        finally
        {
            if (bstr != IntPtr.Zero)
            {
                System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(bstr);
            }
        }
    }
}
