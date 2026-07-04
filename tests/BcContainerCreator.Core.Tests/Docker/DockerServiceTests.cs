using BcContainerCreator.Core.Docker;
using BcContainerCreator.Core.Models;
using BcContainerCreator.Core.Tests.PowerShell;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BcContainerCreator.Core.Tests.Docker;

/// <summary>
/// Tests für die Parsing-/Mapping-Logik des <see cref="DockerService"/> —
/// alle Docker-Aufrufe laufen über den <see cref="FakePowerShellRunner"/>,
/// es wird nie eine echte docker-CLI benötigt.
/// </summary>
public class DockerServiceTests
{
    private static DockerService CreateSut(out FakePowerShellRunner runner)
    {
        runner = new FakePowerShellRunner();
        return new DockerService(runner, NullLogger<DockerService>.Instance);
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("YES", true)]   // Groß-/Kleinschreibung darf keine Rolle spielen
    [InlineData("no", false)]
    [InlineData("", false)]
    public async Task IsInstalledAsync_MapsOutputToBool(string output, bool expected)
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("Get-Command docker", () =>
            string.IsNullOrEmpty(output)
                ? FakePowerShellRunner.Success()
                : FakePowerShellRunner.Success(output));

        (await sut.IsInstalledAsync()).Should().Be(expected);
    }

    [Fact]
    public async Task IsInstalledAsync_RunnerFails_ReturnsFalse()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("Get-Command docker", () =>
            FakePowerShellRunner.Failure("powershell kaputt"));

        (await sut.IsInstalledAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task IsRunningAsync_SuccessMeansDaemonReachable()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("docker info", () => FakePowerShellRunner.Success());

        (await sut.IsRunningAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task IsRunningAsync_NonZeroExit_ReturnsFalse()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("docker info", () =>
            FakePowerShellRunner.Failure("error during connect"));

        (await sut.IsRunningAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData("windows", ContainerMode.Windows)]
    [InlineData("WINDOWS", ContainerMode.Windows)]  // docker liefert lowercase, aber defensiv
    [InlineData("  windows  ", ContainerMode.Windows)]
    [InlineData("linux", ContainerMode.Linux)]
    [InlineData("solaris", ContainerMode.Unknown)]
    public async Task GetContainerModeAsync_MapsOsType(string osType, ContainerMode expected)
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("OSType", () => FakePowerShellRunner.Success(osType));

        (await sut.GetContainerModeAsync()).Should().Be(expected);
    }

    [Fact]
    public async Task GetContainerModeAsync_EmptyOutput_ReturnsUnknown()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("OSType", () => FakePowerShellRunner.Success());

        (await sut.GetContainerModeAsync()).Should().Be(ContainerMode.Unknown);
    }

    [Fact]
    public async Task GetContainerModeAsync_RunnerFails_ReturnsUnknown()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("OSType", () => FakePowerShellRunner.Failure("daemon down"));

        (await sut.GetContainerModeAsync()).Should().Be(ContainerMode.Unknown);
    }

    [Fact]
    public async Task SwitchToWindowsModeAsync_UsesDockerCliSwitchDaemon()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("-SwitchDaemon", () => FakePowerShellRunner.Success());

        (await sut.SwitchToWindowsModeAsync()).Should().BeTrue();
        runner.Calls.Should().ContainSingle();
        runner.Calls[0].Script.Should().Contain("DockerCli.exe");
    }

    [Fact]
    public async Task SwitchToWindowsModeAsync_Failure_ReturnsFalse()
    {
        var sut = CreateSut(out var runner);
        runner.WhenScriptContains("-SwitchDaemon", () =>
            FakePowerShellRunner.Failure("DockerCli.exe nicht gefunden"));

        (await sut.SwitchToWindowsModeAsync()).Should().BeFalse();
    }
}
