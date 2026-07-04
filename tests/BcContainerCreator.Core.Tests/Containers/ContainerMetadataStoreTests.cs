using System.IO;
using System.Security;
using BcContainerCreator.Core.Containers;
using BcContainerCreator.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BcContainerCreator.Core.Tests.Containers;

/// <summary>
/// Tests für <see cref="ContainerMetadataStore"/>. Verwendet temporäre
/// Verzeichnisse pro Test, damit Save/Load/Decrypt isoliert sind und sich
/// keine Tests gegenseitig die Datei-States kaputtmachen. DPAPI-Roundtrip
/// ist die wichtigste Invariante — der frühere Encoding-Mismatch (UTF-16LE
/// schreiben, UTF-8 lesen) lieferte hier einen Müll-String mit eingebetteten
/// Null-Bytes.
/// </summary>
public class ContainerMetadataStoreTests : IDisposable
{
    private readonly string _root;

    public ContainerMetadataStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"bcc-metadata-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    private ContainerMetadataStore CreateSut() =>
        new(NullLogger<ContainerMetadataStore>.Instance, _root);

    private static SecureString MakeSecureString(string s)
    {
        var ss = new SecureString();
        foreach (var c in s) ss.AppendChar(c);
        ss.MakeReadOnly();
        return ss;
    }

    [Fact]
    public async Task SaveAndLoad_RoundtripsAllFields()
    {
        var sut = CreateSut();
        await sut.SaveAsync(
            containerName: "bcdev",
            createdAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
            authType: AuthType.NavUserPassword,
            username: "admin",
            password: MakeSecureString("super-secret-pwd"),
            artifactType: ArtifactType.OnPrem,
            country: "DE",
            versionSelector: "26",
            resolvedBuild: "26.0.0.0",
            webClientUrl: "http://bcdev/BC?tenant=default");

        var loaded = await sut.LoadAsync("bcdev");

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("bcdev");
        loaded.AuthType.Should().Be(AuthType.NavUserPassword);
        loaded.Username.Should().Be("admin");
        loaded.ArtifactType.Should().Be(ArtifactType.OnPrem);
        loaded.Country.Should().Be("DE");
        loaded.VersionSelector.Should().Be("26");
        loaded.ResolvedBuild.Should().Be("26.0.0.0");
        loaded.WebClientUrl.Should().Be("http://bcdev/BC?tenant=default");
        loaded.PasswordCipher.Should().NotBeNull();
        loaded.PasswordCipher!.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DecryptPassword_RoundtripsExactString()
    {
        var sut = CreateSut();
        await sut.SaveAsync(
            "c1", DateTimeOffset.UtcNow, AuthType.NavUserPassword,
            "u", MakeSecureString("P@ssw0rd1"),
            ArtifactType.OnPrem, "DE", "latest", null, "url");

        var loaded = await sut.LoadAsync("c1");
        var plain = sut.DecryptPassword(loaded!.PasswordCipher);

        // Exakter Roundtrip — keine eingebetteten Null-Bytes (Encoding-Mismatch
        // hätte hier "P\0@\0s\0..." geliefert).
        plain.Should().Be("P@ssw0rd1");
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("with spaces and 1234")]
    [InlineData("unicode-äöüß-€-emoji")]
    [InlineData("very-long-pwd-" + "abcdefghij" + "klmnopqrst" + "uvwxyz0123" + "456789!@#$" + "%^&*()_+={}")]
    public async Task DecryptPassword_VariousInputs_RoundtripExact(string input)
    {
        var sut = CreateSut();
        await sut.SaveAsync(
            "c1", DateTimeOffset.UtcNow, AuthType.NavUserPassword,
            "u", MakeSecureString(input),
            ArtifactType.OnPrem, "DE", "latest", null, "url");

        var loaded = await sut.LoadAsync("c1");
        sut.DecryptPassword(loaded!.PasswordCipher).Should().Be(input);
    }

    [Fact]
    public async Task SaveAsync_NullPassword_NoCipher()
    {
        var sut = CreateSut();
        await sut.SaveAsync(
            "c1", DateTimeOffset.UtcNow, AuthType.Windows,
            "u", password: null,
            ArtifactType.OnPrem, "DE", "latest", null, "url");

        var loaded = await sut.LoadAsync("c1");
        loaded.Should().NotBeNull();
        loaded!.PasswordCipher.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_EmptyPassword_NoCipher()
    {
        var sut = CreateSut();
        await sut.SaveAsync(
            "c1", DateTimeOffset.UtcNow, AuthType.Windows,
            "u", password: new SecureString(),
            ArtifactType.OnPrem, "DE", "latest", null, "url");

        var loaded = await sut.LoadAsync("c1");
        loaded!.PasswordCipher.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_Missing_ReturnsNull()
    {
        var sut = CreateSut();
        var loaded = await sut.LoadAsync("does-not-exist");
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile()
    {
        var sut = CreateSut();
        await sut.SaveAsync(
            "c1", DateTimeOffset.UtcNow, AuthType.Windows,
            "u", null,
            ArtifactType.OnPrem, "DE", "latest", null, "url");

        (await sut.LoadAsync("c1")).Should().NotBeNull();
        await sut.DeleteAsync("c1");
        (await sut.LoadAsync("c1")).Should().BeNull();
    }

    [Fact]
    public void DecryptPassword_NullCipher_ReturnsNull()
    {
        var sut = CreateSut();
        sut.DecryptPassword(null).Should().BeNull();
        sut.DecryptPassword(Array.Empty<byte>()).Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_CorruptJson_ReturnsNullAndQuarantinesFile()
    {
        var sut = CreateSut();
        var path = Path.Combine(_root, "broken.json");
        await File.WriteAllTextAsync(path, "{ dies ist kein gültiges JSON ");

        var loaded = await sut.LoadAsync("broken");

        loaded.Should().BeNull();
        File.Exists(path).Should().BeFalse("die korrupte Datei muss in Quarantäne verschoben sein");
        File.Exists(path + ".corrupt").Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_OverExistingFile_ReplacesContentCompletely()
    {
        var sut = CreateSut();
        await sut.SaveAsync(
            "c1", DateTimeOffset.UtcNow, AuthType.NavUserPassword,
            "erster-user", MakeSecureString("pwd-eins"),
            ArtifactType.OnPrem, "DE", "26", null, "url-1");

        await sut.SaveAsync(
            "c1", DateTimeOffset.UtcNow, AuthType.Windows,
            "zweiter-user", password: null,
            ArtifactType.Sandbox, "W1", "latest", null, "url-2");

        var loaded = await sut.LoadAsync("c1");
        loaded.Should().NotBeNull();
        loaded!.Username.Should().Be("zweiter-user");
        loaded.AuthType.Should().Be(AuthType.Windows);
        loaded.Country.Should().Be("W1");
        loaded.PasswordCipher.Should().BeNull("der zweite Save hatte kein Passwort");
        Directory.GetFiles(_root, "*.tmp").Should().BeEmpty("atomares Schreiben darf keine tmp-Dateien hinterlassen");
    }

    [Fact]
    public async Task SaveAndLoad_RoundtripsCreatedBy()
    {
        var sut = CreateSut();
        await sut.SaveAsync(
            "c1", DateTimeOffset.UtcNow, AuthType.NavUserPassword,
            "u", MakeSecureString("pwd"),
            ArtifactType.OnPrem, "DE", "latest", null, "url",
            createdBy: @"FIRMA\thomas");

        var loaded = await sut.LoadAsync("c1");

        loaded!.CreatedBy.Should().Be(@"FIRMA\thomas");
    }

    [Fact]
    public async Task LoadAsync_LegacyFileWithoutCreatedBy_LoadsWithNull()
    {
        // Abwärtskompatibilität: Metadaten-Dateien älterer App-Versionen haben
        // kein createdBy-Feld — Laden darf nicht scheitern, CreatedBy ist null.
        var sut = CreateSut();
        var legacyJson = """
            {
              "name": "altbestand",
              "createdAt": "2026-01-15T10:00:00+00:00",
              "authType": "NavUserPassword",
              "username": "admin",
              "passwordCipher": null,
              "artifactType": "OnPrem",
              "country": "DE",
              "versionSelector": "26",
              "resolvedBuild": null,
              "webClientUrl": "http://altbestand/BC?tenant=default"
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(_root, "altbestand.json"), legacyJson);

        var loaded = await sut.LoadAsync("altbestand");

        loaded.Should().NotBeNull();
        loaded!.Username.Should().Be("admin");
        loaded.CreatedBy.Should().BeNull();
    }

    [Fact]
    public void DecryptPassword_GarbageCipher_ReturnsNull()
    {
        // Zufällige Bytes sind kein gültiger DPAPI-Blob → CryptographicException
        // wird intern gefangen und als null gemeldet, nicht geworfen.
        var sut = CreateSut();
        var garbage = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        sut.DecryptPassword(garbage).Should().BeNull();
    }
}
