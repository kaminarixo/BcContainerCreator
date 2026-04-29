using System.Net;
using System.Security;
using BcContainerCreator.Core.Containers;
using BcContainerCreator.Core.Models;
using BcContainerCreator.Core.Tests.PowerShell;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BcContainerCreator.Core.Tests.Containers;

public class ContainerServiceTests
{
    private static SecureString MakeSecureString(string s)
    {
        var ss = new SecureString();
        foreach (var c in s) ss.AppendChar(c);
        ss.MakeReadOnly();
        return ss;
    }

    private static ContainerService CreateSut(out FakePowerShellRunner runner)
    {
        runner = new FakePowerShellRunner();
        var metadata = new Mock<IContainerMetadataStore>();
        return new ContainerService(runner, metadata.Object, NullLogger<ContainerService>.Instance);
    }

    [Fact]
    public void BuildCreateScript_OnPremDe_ContainsExpectedFragments()
    {
        var sut = CreateSut(out _);
        var req = new ContainerCreateRequest(
            ContainerName: "bcdev",
            ArtifactType: ArtifactType.OnPrem,
            Country: "DE",
            Version: "latest",
            AuthType: AuthType.NavUserPassword,
            Username: "admin",
            Password: MakeSecureString("P@ssw0rd"));

        var script = sut.BuildCreateScript(req);

        script.Should().Contain("Import-Module BcContainerHelper");
        // 'latest' wird als implizites -select Latest behandelt — ohne explizites -version,
        // weil Get-BcArtifactUrl sonst nach einer Version namens 'latest' suchen würde.
        script.Should().Contain("Get-BcArtifactUrl -type OnPrem -country 'DE' -select Latest");
        script.Should().NotContain("-version 'latest'");
        script.Should().Contain("New-BcContainer");
        script.Should().Contain("-containerName 'bcdev'");
        script.Should().Contain("-auth NavUserPassword");
        script.Should().Contain("-credential $cred");
        // Username + Password kommen aus der externen Param-Datei ($Params),
        // nicht als String-Interpolation ins Skript:
        script.Should().Contain("$Params.Password");
        script.Should().Contain("$Params.Username");
        script.Should().NotContain("'admin'"); // Username sollte nicht hartcodiert sein
        script.Should().NotContain("'P@ssw0rd'"); // Password schon gar nicht
        script.Should().Contain("-accept_eula");
        script.Should().Contain("-includeAL");
        script.Should().Contain("-updateHosts");
    }

    [Fact]
    public void BuildCreateScript_Sandbox_UsesSandboxType()
    {
        var sut = CreateSut(out _);
        var req = new ContainerCreateRequest(
            "bcsandbox", ArtifactType.Sandbox, "W1", "26",
            AuthType.NavUserPassword, "admin", MakeSecureString("x"));

        var script = sut.BuildCreateScript(req);

        script.Should().Contain("-type Sandbox");
        script.Should().Contain("-country 'W1'");
        script.Should().Contain("-version '26'");
    }

    [Fact]
    public void BuildCreateScript_WindowsAuth_OmitsCredentialAndAddsWindowsAuth()
    {
        var sut = CreateSut(out _);
        var req = new ContainerCreateRequest(
            "bcwin", ArtifactType.OnPrem, "DE", "latest",
            AuthType.Windows, Username: "ignored", Password: new SecureString());

        var script = sut.BuildCreateScript(req);

        script.Should().Contain("-auth Windows");
        script.Should().NotContain("-credential");
        script.Should().NotContain("$cred =");
    }

    [Fact]
    public void BuildCreateScript_OptionalFlags_AreOmittedWhenFalse()
    {
        var sut = CreateSut(out _);
        var req = new ContainerCreateRequest(
            "c", ArtifactType.OnPrem, "DE", "latest",
            AuthType.Windows, "u", new SecureString(),
            AcceptEula: false, IncludeAL: false, IncludeTestToolkit: false);

        var script = sut.BuildCreateScript(req);

        script.Should().NotContain("-accept_eula");
        script.Should().NotContain("-includeAL");
        script.Should().NotContain("-includeTestToolkit");
    }

    [Fact]
    public void BuildCreateScript_LicenseAndMemoryLimit_AreIncluded()
    {
        var sut = CreateSut(out _);
        var req = new ContainerCreateRequest(
            "c", ArtifactType.OnPrem, "DE", "latest",
            AuthType.Windows, "u", new SecureString(),
            LicenseFilePath: @"C:\lic\my.flf",
            MemoryLimit: "8G",
            Isolation: "process");

        var script = sut.BuildCreateScript(req);

        script.Should().Contain(@"-licenseFile 'C:\lic\my.flf'");
        script.Should().Contain("-memoryLimit '8G'");
        script.Should().Contain("-isolation 'process'");
    }

    [Fact]
    public void BuildCreateScript_QuoteEscaping_HandlesSingleQuoteInValue()
    {
        var sut = CreateSut(out _);
        var req = new ContainerCreateRequest(
            "c", ArtifactType.OnPrem, "DE", "latest",
            AuthType.Windows, "u", new SecureString(),
            LicenseFilePath: @"C:\lic\d'angelo.flf");

        var script = sut.BuildCreateScript(req);

        // PowerShell-Single-Quote-Escape: ' wird zu ''.
        script.Should().Contain(@"'C:\lic\d''angelo.flf'");
    }

    [Fact]
    public void BuildCreateScript_InvalidContainerName_Throws()
    {
        var sut = CreateSut(out _);
        var req = new ContainerCreateRequest(
            "bad name!", ArtifactType.OnPrem, "DE", "latest",
            AuthType.Windows, "u", new SecureString());

        var act = () => sut.BuildCreateScript(req);

        act.Should().Throw<ArgumentException>().WithMessage("*Ungültiges Zeichen*");
    }

    [Fact]
    public async Task CreateContainerAsync_PassesPasswordAsSecureString_NotInScript()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("New-BcContainer", () => FakePowerShellRunner.Success());

        var pwd = MakeSecureString("super-secret-pwd");
        var req = new ContainerCreateRequest(
            "c", ArtifactType.OnPrem, "DE", "latest",
            AuthType.NavUserPassword, "admin", pwd);

        await sut.CreateContainerAsync(req);

        runner.Calls.Should().HaveCount(1);
        var call = runner.Calls[0];
        call.Script.Should().NotContain("super-secret-pwd");
        // Username + Password gehen über die JSON-Param-Datei des externen Runners
        // (Schlüssel müssen zu $Params.Username / $Params.Password im Skript passen).
        call.Variables.Should().ContainKey("Password");
        call.Variables!["Password"].Should().BeOfType<SecureString>();
        call.Variables.Should().ContainKey("Username");
        call.Variables!["Username"].Should().Be("admin");
    }

    [Fact]
    public async Task CreateContainerAsync_Cancelled_ReportsCancellation()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("New-BcContainer", () =>
            new Core.PowerShell.PSResult(false, Array.Empty<System.Management.Automation.PSObject>(),
                Array.Empty<string>(), TimeSpan.Zero, WasCancelled: true));

        var reports = new List<string>();
        var progress = new Progress<string>(reports.Add);

        var req = new ContainerCreateRequest(
            "c", ArtifactType.OnPrem, "DE", "latest",
            AuthType.Windows, "u", new SecureString());

        var result = await sut.CreateContainerAsync(req, progress);

        result.WasCancelled.Should().BeTrue();
        // Progress wird auf einem ThreadPool-Thread gepostet — kurz abwarten.
        await Task.Delay(50);
        reports.Should().Contain(s => s == "Abgebrochen.");
    }

    [Fact]
    public async Task CreateContainerAsync_NavUserPasswordWithoutPwd_Throws()
    {
        var sut = CreateSut(out _);
        var req = new ContainerCreateRequest(
            "c", ArtifactType.OnPrem, "DE", "latest",
            AuthType.NavUserPassword, "admin", new SecureString());

        var act = async () => await sut.CreateContainerAsync(req);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void BuildCreateScript_FinalWriteInformation_IsCleanlyQuoted()
    {
        var sut = CreateSut(out _);
        var req = new ContainerCreateRequest(
            "bcdev", ArtifactType.OnPrem, "DE", "latest",
            AuthType.NavUserPassword, "admin", MakeSecureString("x"));

        var script = sut.BuildCreateScript(req);

        // Saubere Single-Quote-Variante ohne C#-/PowerShell-String-Konkatenation.
        script.Should().Contain("Write-Information 'Container ''bcdev'' wurde erstellt.'");
        // Alte Form (PS-Konkatenation mit "+") darf nicht mehr enthalten sein.
        script.Should().NotContain("Write-Information \"Container '\" +");
    }

    [Fact]
    public async Task GetVersionOptionsAsync_PassesCountryViaParams_NotInterpolated()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("Get-BCArtifactUrl", () =>
            FakePowerShellRunner.Success("28|28.0.46665.49591"));

        await sut.GetVersionOptionsAsync(ArtifactType.OnPrem, "DE", topMajors: 6);

        runner.Calls.Should().HaveCount(1);
        var call = runner.Calls[0];

        // Country darf NICHT roh ins Skript interpoliert werden.
        call.Script.Should().NotContain("-country 'DE'");
        call.Script.Should().NotContain("-type OnPrem ");
        // Statt dessen über $Params:
        call.Script.Should().Contain("$Params.Country");
        call.Script.Should().Contain("$Params.Type");
        call.Script.Should().Contain("$Params.TopMajors");

        call.Variables.Should().NotBeNull();
        call.Variables!["Country"].Should().Be("DE");
        call.Variables!["Type"].Should().Be("OnPrem");
        call.Variables!["TopMajors"].Should().Be(6);
    }

    [Fact]
    public async Task GetVersionOptionsAsync_CountryWithApostrophe_DoesNotBreakScript()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("Get-BCArtifactUrl", () => FakePowerShellRunner.Success());

        // Apostroph in der Country-Eingabe würde bei naiver String-Interpolation
        // den Quote-Kontext brechen oder zu Skript-Injection führen.
        var malicious = "DE'; Remove-Item C:\\ -Recurse; '";
        await sut.GetVersionOptionsAsync(ArtifactType.OnPrem, malicious, topMajors: 1);

        var call = runner.Calls[0];
        // Roh-Wert taucht NICHT im Skript auf (egal wie quoted).
        call.Script.Should().NotContain(malicious);
        call.Script.Should().NotContain("Remove-Item");
        // Roh-Wert wandert stattdessen sicher in die Param-Datei.
        call.Variables!["Country"].Should().Be(malicious);
    }

    [Fact]
    public async Task GetVersionOptionsAsync_Sandbox_PassesSandboxType()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("Get-BCArtifactUrl", () =>
            FakePowerShellRunner.Success("28|28.0.0.0"));

        await sut.GetVersionOptionsAsync(ArtifactType.Sandbox, "W1", topMajors: 3);

        var call = runner.Calls[0];
        call.Variables!["Type"].Should().Be("Sandbox");
        call.Variables!["Country"].Should().Be("W1");
        call.Variables!["TopMajors"].Should().Be(3);
    }
}
