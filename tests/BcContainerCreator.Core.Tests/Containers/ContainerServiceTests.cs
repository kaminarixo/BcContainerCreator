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

    private static ContainerService CreateSut(out FakePowerShellRunner runner) =>
        CreateSut(out runner, out _);

    private static ContainerService CreateSut(out FakePowerShellRunner runner, out Mock<IContainerMetadataStore> metadata)
    {
        runner = new FakePowerShellRunner();
        metadata = new Mock<IContainerMetadataStore>();
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

    [Theory]
    [InlineData("DE'; Get-Process; '")]           // Injection-Versuch via Quote-Ausbruch
    [InlineData("DE\"; Remove-Item x; \"")]        // Injection-Versuch via Double-Quote
    [InlineData("Deutschland")]                    // zu lang
    [InlineData("D")]                              // zu kurz
    [InlineData("D E")]                            // Leerzeichen
    public void BuildCreateScript_InvalidCountry_Throws(string country)
    {
        var sut = CreateSut(out _);
        var req = new ContainerCreateRequest(
            "c", ArtifactType.OnPrem, country, "latest",
            AuthType.Windows, "u", new SecureString());

        var act = () => sut.BuildCreateScript(req);

        act.Should().Throw<ArgumentException>().WithMessage("*Country-Code*");
    }

    [Theory]
    [InlineData("26'; Get-Process; '")]
    [InlineData("neueste")]
    [InlineData("v26")]
    public void BuildCreateScript_InvalidVersion_Throws(string version)
    {
        var sut = CreateSut(out _);
        var req = new ContainerCreateRequest(
            "c", ArtifactType.OnPrem, "DE", version,
            AuthType.Windows, "u", new SecureString());

        var act = () => sut.BuildCreateScript(req);

        act.Should().Throw<ArgumentException>().WithMessage("*Version*");
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("LATEST")]
    [InlineData("26")]
    [InlineData("26.5")]
    [InlineData("26.0.12345.67890")]
    public void BuildCreateScript_ValidVersionSelectors_DoNotThrow(string version)
    {
        var sut = CreateSut(out _);
        var req = new ContainerCreateRequest(
            "c", ArtifactType.OnPrem, "DE", version,
            AuthType.Windows, "u", new SecureString());

        var act = () => sut.BuildCreateScript(req);

        act.Should().NotThrow();
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
            new Core.PowerShell.PSResult(false, Array.Empty<string>(),
                Array.Empty<string>(), TimeSpan.Zero, WasCancelled: true));

        var reports = new List<string>();
        // SyncProgress statt Progress<T>: deterministisch, kein ThreadPool-Race.
        var progress = new SyncProgress<string>(reports.Add);

        var req = new ContainerCreateRequest(
            "c", ArtifactType.OnPrem, "DE", "latest",
            AuthType.Windows, "u", new SecureString());

        var result = await sut.CreateContainerAsync(req, progress);

        result.WasCancelled.Should().BeTrue();
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

    [Fact]
    public void BuildCreateScript_Multitenant_TogglesFlag()
    {
        var sut = CreateSut(out _);
        var baseReq = new ContainerCreateRequest(
            "c", ArtifactType.OnPrem, "DE", "latest",
            AuthType.Windows, "u", new SecureString());

        sut.BuildCreateScript(baseReq with { Multitenant = true }).Should().Contain("-multitenant");
        sut.BuildCreateScript(baseReq with { Multitenant = false }).Should().NotContain("-multitenant");
    }

    // ----- GetVersionOptionsAsync: Randfälle -----

    [Fact]
    public async Task GetVersionOptionsAsync_RunnerFails_ReturnsEmptyList()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("Get-BCArtifactUrl", () =>
            FakePowerShellRunner.Failure("Artifact-Endpoint nicht erreichbar"));

        var options = await sut.GetVersionOptionsAsync(ArtifactType.OnPrem, "DE");

        options.Should().BeEmpty();
    }

    [Fact]
    public async Task GetVersionOptionsAsync_TopMajorsZero_ReturnsEmptyWithoutRunnerCall()
    {
        var sut = CreateSut(out var runner);

        var options = await sut.GetVersionOptionsAsync(ArtifactType.OnPrem, "DE", topMajors: 0);

        options.Should().BeEmpty();
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetVersionOptionsAsync_MalformedLines_AreSkipped()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("Get-BCArtifactUrl", () => FakePowerShellRunner.Success(
            "28|28.0.46665.49591",
            "ohne-pipe-zeile",
            "27|",           // leeres Build-Feld
            "|27.0.0.0",     // leeres Major-Feld
            "",
            "26|26.5.12345.0"));

        var options = await sut.GetVersionOptionsAsync(ArtifactType.OnPrem, "DE");

        // 'latest' + die zwei gültigen Zeilen; Müll-Zeilen still übersprungen.
        options.Should().HaveCount(3);
        options[0].Selector.Should().Be("latest");
        options[0].LatestBuild.Should().Be("28.0.46665.49591", "'latest' übernimmt den neuesten Build");
        options.Select(o => o.Selector).Should().ContainInOrder("latest", "28", "26");
    }

    [Fact]
    public async Task GetVersionOptionsAsync_NoRows_ReturnsOnlyLatestFallback()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("Get-BCArtifactUrl", () => FakePowerShellRunner.Success());

        var options = await sut.GetVersionOptionsAsync(ArtifactType.OnPrem, "DE");

        options.Should().ContainSingle();
        options[0].Selector.Should().Be("latest");
        options[0].LatestBuild.Should().BeNull();
    }

    // ----- ListContainersAsync -----

    private const string RunningBcContainerJson =
        """{"ID":"abc123","Names":"bcdev","Image":"mcr.microsoft.com/businesscentral:10.0.20348.169","Status":"Up 2 hours","State":"running","Ports":"80/tcp"}""";

    [Fact]
    public async Task ListContainersAsync_ParsesNdjsonLines()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("docker ps", () => FakePowerShellRunner.Success(
            RunningBcContainerJson,
            """{"ID":"def456","Names":"redis","Image":"redis:7","Status":"Exited (0) 3 days ago","State":"exited","Ports":""}"""));

        var list = await sut.ListContainersAsync();

        list.Should().HaveCount(2);

        var bc = list[0];
        bc.Id.Should().Be("abc123");
        bc.Name.Should().Be("bcdev");
        bc.IsRunning.Should().BeTrue();
        bc.IsBcContainer.Should().BeTrue("Image enthält 'businesscentral'");
        bc.WebClientUrl.Should().Be("http://bcdev/BC?tenant=default");
        bc.Ports.Should().Be("80/tcp");

        var redis = list[1];
        redis.IsRunning.Should().BeFalse();
        redis.IsBcContainer.Should().BeFalse();
        redis.WebClientUrl.Should().BeNull("nur BC-Container bekommen eine Web-Client-URL");
    }

    [Fact]
    public async Task ListContainersAsync_UpStatusWithoutState_CountsAsRunning()
    {
        // Ältere Docker-Versionen liefern kein 'State'-Feld — dann zählt 'Up …'.
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("docker ps", () => FakePowerShellRunner.Success(
            """{"ID":"x","Names":"c1","Image":"bcartifacts/onprem","Status":"Up 5 minutes","Ports":""}"""));

        var list = await sut.ListContainersAsync();

        list.Should().ContainSingle();
        list[0].IsRunning.Should().BeTrue();
        list[0].IsBcContainer.Should().BeTrue("Image enthält 'bcartifacts'");
    }

    [Fact]
    public async Task ListContainersAsync_MissingProperties_DefaultToEmpty()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("docker ps", () => FakePowerShellRunner.Success("{}"));

        var list = await sut.ListContainersAsync();

        list.Should().ContainSingle();
        list[0].Name.Should().BeEmpty();
        list[0].Image.Should().BeEmpty();
        list[0].IsRunning.Should().BeFalse();
        list[0].WebClientUrl.Should().BeNull();
    }

    [Fact]
    public async Task ListContainersAsync_BrokenJsonLine_SkipsLineAndParsesRest()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("docker ps", () => FakePowerShellRunner.Success(
            "{ kaputtes json",
            "--- FEHLER ---",   // Wrapper-Zeilen beginnen nicht mit '{' → übersprungen
            RunningBcContainerJson));

        var list = await sut.ListContainersAsync();

        list.Should().ContainSingle();
        list[0].Name.Should().Be("bcdev");
    }

    [Fact]
    public async Task ListContainersAsync_RunnerFails_ReturnsEmptyList()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("docker ps", () =>
            FakePowerShellRunner.Failure("docker daemon nicht erreichbar"));

        var list = await sut.ListContainersAsync();

        list.Should().BeEmpty();
    }

    // ----- Start / Stop / Remove / Logs -----

    [Fact]
    public async Task StartContainerAsync_BuildsQuotedScript()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("docker start", () => FakePowerShellRunner.Success());

        var ok = await sut.StartContainerAsync("bcdev");

        ok.Should().BeTrue();
        runner.Calls.Should().ContainSingle();
        runner.Calls[0].Script.Should().Contain("docker start bcdev");
        runner.Calls[0].Script.Should().Contain("$LASTEXITCODE");
    }

    [Fact]
    public async Task StartContainerAsync_Failure_ReturnsFalse()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("docker start", () =>
            FakePowerShellRunner.Failure("No such container"));

        (await sut.StartContainerAsync("bcdev")).Should().BeFalse();
    }

    [Fact]
    public async Task StopContainerAsync_BuildsQuotedScript()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("docker stop", () => FakePowerShellRunner.Success());

        var ok = await sut.StopContainerAsync("bcdev");

        ok.Should().BeTrue();
        runner.Calls[0].Script.Should().Contain("docker stop bcdev");
    }

    [Theory]
    [InlineData("bad;name")]
    [InlineData("bad name")]
    [InlineData("bad`name")]
    public async Task StartStopRemove_InvalidName_Throws(string name)
    {
        // QuoteForDocker ist die letzte Verteidigungslinie gegen gefährliche
        // Argumente an die docker-CLI — auch wenn die UI das schon verhindert.
        var sut = CreateSut(out _);

        await ((Func<Task>)(() => sut.StartContainerAsync(name))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => sut.StopContainerAsync(name))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => sut.RemoveContainerAsync(name))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => sut.GetContainerLogsAsync(name))).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RemoveContainerAsync_ForceFlag_Toggles()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("docker rm", () => FakePowerShellRunner.Success());

        await sut.RemoveContainerAsync("bcdev", force: true);
        await sut.RemoveContainerAsync("bcdev", force: false);

        runner.Calls[0].Script.Should().Contain("docker rm -f bcdev");
        runner.Calls[1].Script.Should().Contain("docker rm bcdev");
        runner.Calls[1].Script.Should().NotContain("-f");
    }

    [Fact]
    public async Task RemoveContainerAsync_Success_DeletesMetadata()
    {
        var sut = CreateSut(out var runner, out var metadata);
        runner.WhenScriptContains("docker rm", () => FakePowerShellRunner.Success());

        var ok = await sut.RemoveContainerAsync("bcdev");

        ok.Should().BeTrue();
        metadata.Verify(m => m.DeleteAsync("bcdev", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveContainerAsync_MetadataDeleteThrows_StillReturnsTrue()
    {
        // Der Container ist weg — ein Metadaten-Aufräumfehler darf das
        // Ergebnis nicht kippen.
        var sut = CreateSut(out var runner, out var metadata);
        runner.WhenScriptContains("docker rm", () => FakePowerShellRunner.Success());
        metadata.Setup(m => m.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Datei gesperrt"));

        var ok = await sut.RemoveContainerAsync("bcdev");

        ok.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveContainerAsync_DockerFails_ReturnsFalseAndKeepsMetadata()
    {
        var sut = CreateSut(out var runner, out var metadata);
        runner.WhenScriptContains("docker rm", () =>
            FakePowerShellRunner.Failure("conflict"));

        var ok = await sut.RemoveContainerAsync("bcdev");

        ok.Should().BeFalse();
        metadata.Verify(m => m.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetContainerLogsAsync_JoinsOutputLines()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("docker logs", () =>
            FakePowerShellRunner.Success("Zeile 1", "Zeile 2"));

        var logs = await sut.GetContainerLogsAsync("bcdev", tail: 500);

        logs.Should().Be($"Zeile 1{Environment.NewLine}Zeile 2");
        runner.Calls[0].Script.Should().Contain("--tail 500");
    }

    [Fact]
    public async Task GetContainerLogsAsync_NonPositiveTail_FallsBackTo1000()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("docker logs", () => FakePowerShellRunner.Success());

        await sut.GetContainerLogsAsync("bcdev", tail: 0);

        runner.Calls[0].Script.Should().Contain("--tail 1000");
    }

    [Fact]
    public async Task GetContainerLogsAsync_FailureWithoutOutput_ReturnsErrors()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("docker logs", () =>
            FakePowerShellRunner.Failure("No such container: bcdev"));

        var logs = await sut.GetContainerLogsAsync("bcdev");

        logs.Should().Contain("No such container");
    }
}
