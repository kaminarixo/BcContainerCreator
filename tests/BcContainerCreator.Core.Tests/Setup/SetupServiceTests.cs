using BcContainerCreator.Core.Docker;
using BcContainerCreator.Core.Setup;
using BcContainerCreator.Core.Tests.PowerShell;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BcContainerCreator.Core.Tests.Setup;

public class SetupServiceTests
{
    private static (SetupService Sut, FakePowerShellRunner Runner, Mock<IDockerService> Docker, Mock<IElevationService> Elevation) CreateSut(bool isAdmin = false)
    {
        var runner = new FakePowerShellRunner();
        var docker = new Mock<IDockerService>();
        var elevation = new Mock<IElevationService>();
        elevation.Setup(e => e.RunElevatedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);
        // Admin-Probe wird injiziert, damit der Test-Runner-Kontext (z. B. ein
        // GitHub-Actions-Windows-Runner, der per Default elevated ist) das
        // Verhalten nicht verfälscht.
        var sut = new SetupService(runner, docker.Object, elevation.Object, NullLogger<SetupService>.Instance, () => isAdmin);
        return (sut, runner, docker, elevation);
    }

    [Theory]
    [InlineData("set-execution-policy", "Set-ExecutionPolicy")]
    [InlineData("install-nuget-provider", "PSResourceGet")]
    [InlineData("trust-psgallery", "Set-PSResourceRepository")]
    [InlineData("install-bccontainerhelper", "Install-PSResource -Name BcContainerHelper")]
    [InlineData("remove-legacy-module", "Uninstall-PSResource -Name navcontainerhelper")]
    public async Task ApplyFixAsync_RunsExpectedCommand(string fixId, string mustContain)
    {
        var (sut, runner, _, _) = CreateSut();

        var ok = await sut.ApplyFixAsync(fixId);

        ok.Should().BeTrue();
        runner.Calls.Should().ContainSingle();
        runner.Calls[0].Script.Should().Contain(mustContain);
    }

    [Theory]
    [InlineData("trust-psgallery")]
    [InlineData("install-bccontainerhelper")]
    [InlineData("remove-legacy-module")]
    public async Task ApplyFixAsync_PSGalleryFixes_BootstrapPSResourceGet(string fixId)
    {
        // Alle PSGallery-orientierten Fixes sollen PSResourceGet vor dem
        // eigentlichen Cmdlet bootstrappen, damit der PowerShellGet-1.0.0.1-Bug
        // unter PS7-In-Process umgangen wird.
        var (sut, runner, _, _) = CreateSut();

        await sut.ApplyFixAsync(fixId);

        runner.Calls.Should().ContainSingle();
        runner.Calls[0].Script.Should().Contain("Install-PSResource"); // Cmdlet-Check
        runner.Calls[0].Script.Should().Contain("Microsoft.PowerShell.PSResourceGet");
    }

    [Fact]
    public async Task ApplyFixAsync_SwitchToWindowsMode_NonAdmin_ElevatesViaUac()
    {
        // Non-Admin-Pfad: der Fix muss den ElevationService aufrufen, NICHT den
        // DockerService direkt. CreateSut(false) injiziert die Admin-Probe als
        // 'false', damit das auch auf elevated Test-Runnern (z. B. GitHub
        // Actions Windows-Runner) deterministisch durchläuft.
        var (sut, runner, docker, elevation) = CreateSut(isAdmin: false);

        var ok = await sut.ApplyFixAsync("switch-to-windows-mode");

        ok.Should().BeTrue();
        elevation.Verify(e => e.RunElevatedAsync(
            It.Is<string>(s => s.Contains("DockerCli.exe")),
            It.Is<string>(a => a.Contains("-SwitchDaemon")),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
        docker.Verify(d => d.SwitchToWindowsModeAsync(It.IsAny<CancellationToken>()), Times.Never);
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyFixAsync_SwitchToWindowsMode_Admin_CallsDockerDirectly()
    {
        // Admin-Pfad: der Fix umgeht den UAC-Prompt und ruft DockerService direkt.
        var (sut, runner, docker, elevation) = CreateSut(isAdmin: true);
        docker.Setup(d => d.SwitchToWindowsModeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var ok = await sut.ApplyFixAsync("switch-to-windows-mode");

        ok.Should().BeTrue();
        docker.Verify(d => d.SwitchToWindowsModeAsync(It.IsAny<CancellationToken>()), Times.Once);
        elevation.Verify(e => e.RunElevatedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyFixAsync_UnknownId_Throws()
    {
        var (sut, _, _, _) = CreateSut();

        var act = async () => await sut.ApplyFixAsync("does-not-exist");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Unbekannte Fix-ID*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ApplyFixAsync_EmptyId_Throws(string fixId)
    {
        var (sut, _, _, _) = CreateSut();

        var act = async () => await sut.ApplyFixAsync(fixId);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ApplyFixAsync_InstallDockerDesktop_ElevatesWingetScript()
    {
        var (sut, runner, _, elevation) = CreateSut();

        var ok = await sut.ApplyFixAsync("install-docker-desktop");

        ok.Should().BeTrue();
        // Docker-Desktop-Install läuft immer elevated über powershell.exe
        // (winget --scope machine braucht Admin) — nie durch den Runner.
        elevation.Verify(e => e.RunElevatedAsync(
            It.Is<string>(s => s.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase)),
            It.Is<string>(a => a.Contains("-File")),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyFixAsync_ElevatedFixes_LeaveNoScriptFilesBehind()
    {
        // Die Elevated-Fixes legen ihr Skript im Runtime-Verzeichnis ab und
        // müssen es im finally wieder löschen.
        var (sut, _, _, _) = CreateSut();
        var runtimeDir = Core.PowerShell.RuntimePaths.GetRuntimeDirectory();

        await sut.ApplyFixAsync("install-docker-desktop");
        await sut.ApplyFixAsync("fix-bccontainerhelper-permissions");

        Directory.GetFiles(runtimeDir, "bccl-*.ps1").Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyFixAsync_RunnerFix_FailurePropagatesAsFalse()
    {
        var (sut, runner, _, _) = CreateSut();
        runner.WhenScriptContains("Set-ExecutionPolicy", () =>
            FakePowerShellRunner.Failure("Security error"));

        var ok = await sut.ApplyFixAsync("set-execution-policy");

        ok.Should().BeFalse();
    }

    [Fact]
    public void AvailableFixes_ContainsAllKnownIds()
    {
        var (sut, _, _, _) = CreateSut();

        sut.AvailableFixes.Keys.Should().Contain(new[]
        {
            "set-execution-policy", "install-nuget-provider", "trust-psgallery",
            "install-bccontainerhelper", "remove-legacy-module", "switch-to-windows-mode",
            "fix-bccontainerhelper-permissions"
        });
    }

    [Fact]
    public async Task ApplyFixAsync_BcchPermissions_ElevatesPowerShell()
    {
        var (sut, runner, _, elevation) = CreateSut();

        var ok = await sut.ApplyFixAsync("fix-bccontainerhelper-permissions");

        ok.Should().BeTrue();
        // Permissions-Fix läuft elevated über powershell.exe — NICHT durch den
        // unprivilegierten Runner. Sonst wäre das Cmdlet wirkungslos.
        elevation.Verify(e => e.RunElevatedAsync(
            It.Is<string>(s => s.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase)),
            It.Is<string>(a => a.Contains("-File")),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
        runner.Calls.Should().BeEmpty();
    }
}
