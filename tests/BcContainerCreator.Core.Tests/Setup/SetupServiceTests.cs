using BcContainerCreator.Core.Docker;
using BcContainerCreator.Core.Setup;
using BcContainerCreator.Core.Tests.PowerShell;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BcContainerCreator.Core.Tests.Setup;

public class SetupServiceTests
{
    private static (SetupService Sut, FakePowerShellRunner Runner, Mock<IDockerService> Docker, Mock<IElevationService> Elevation) CreateSut()
    {
        var runner = new FakePowerShellRunner();
        var docker = new Mock<IDockerService>();
        var elevation = new Mock<IElevationService>();
        elevation.Setup(e => e.RunElevatedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);
        var sut = new SetupService(runner, docker.Object, elevation.Object, NullLogger<SetupService>.Instance);
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
        // Default: AdminContext.IsCurrentProcessAdmin == false (Test-Process ist
        // nicht elevated). Damit muss der Fix den ElevationService aufrufen, NICHT
        // den DockerService direkt.
        var (sut, runner, docker, elevation) = CreateSut();
        elevation.Setup(e => e.RunElevatedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

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
    public async Task ApplyFixAsync_UnknownId_Throws()
    {
        var (sut, _, _, _) = CreateSut();

        var act = async () => await sut.ApplyFixAsync("does-not-exist");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Unbekannte Fix-ID*");
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
