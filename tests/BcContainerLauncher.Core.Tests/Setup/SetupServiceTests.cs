using BcContainerLauncher.Core.Docker;
using BcContainerLauncher.Core.Setup;
using BcContainerLauncher.Core.Tests.PowerShell;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BcContainerLauncher.Core.Tests.Setup;

public class SetupServiceTests
{
    private static (SetupService Sut, FakePowerShellRunner Runner, Mock<IDockerService> Docker) CreateSut()
    {
        var runner = new FakePowerShellRunner();
        var docker = new Mock<IDockerService>();
        var sut = new SetupService(runner, docker.Object, NullLogger<SetupService>.Instance);
        return (sut, runner, docker);
    }

    [Theory]
    [InlineData("set-execution-policy", "Set-ExecutionPolicy")]
    [InlineData("install-nuget-provider", "PSResourceGet")]
    [InlineData("trust-psgallery", "Set-PSResourceRepository")]
    [InlineData("install-bccontainerhelper", "Install-PSResource -Name BcContainerHelper")]
    [InlineData("remove-legacy-module", "Uninstall-PSResource -Name navcontainerhelper")]
    public async Task ApplyFixAsync_RunsExpectedCommand(string fixId, string mustContain)
    {
        var (sut, runner, _) = CreateSut();

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
        var (sut, runner, _) = CreateSut();

        await sut.ApplyFixAsync(fixId);

        runner.Calls.Should().ContainSingle();
        runner.Calls[0].Script.Should().Contain("Install-PSResource"); // Cmdlet-Check
        runner.Calls[0].Script.Should().Contain("Microsoft.PowerShell.PSResourceGet");
    }

    [Fact]
    public async Task ApplyFixAsync_SwitchToWindowsMode_DelegatesToDocker()
    {
        var (sut, runner, docker) = CreateSut();
        docker.Setup(d => d.SwitchToWindowsModeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var ok = await sut.ApplyFixAsync("switch-to-windows-mode");

        ok.Should().BeTrue();
        docker.Verify(d => d.SwitchToWindowsModeAsync(It.IsAny<CancellationToken>()), Times.Once);
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyFixAsync_UnknownId_Throws()
    {
        var (sut, _, _) = CreateSut();

        var act = async () => await sut.ApplyFixAsync("does-not-exist");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Unbekannte Fix-ID*");
    }

    [Fact]
    public void AvailableFixes_ContainsAllKnownIds()
    {
        var (sut, _, _) = CreateSut();

        sut.AvailableFixes.Keys.Should().Contain(new[]
        {
            "set-execution-policy", "install-nuget-provider", "trust-psgallery",
            "install-bccontainerhelper", "remove-legacy-module", "switch-to-windows-mode"
        });
    }
}
