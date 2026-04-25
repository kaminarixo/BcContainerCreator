using BcContainerLauncher.Core.Docker;
using BcContainerLauncher.Core.Models;
using BcContainerLauncher.Core.Setup;
using BcContainerLauncher.Core.Tests.PowerShell;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BcContainerLauncher.Core.Tests.Setup;

public class PreflightCheckTests
{
    private static (PreflightCheck Sut, FakePowerShellRunner Runner, Mock<IDockerService> Docker) CreateSut()
    {
        var runner = new FakePowerShellRunner();
        var docker = new Mock<IDockerService>();
        docker.Setup(d => d.IsInstalledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        docker.Setup(d => d.IsRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        docker.Setup(d => d.GetContainerModeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ContainerMode.Windows);

        var sut = new PreflightCheck(runner, docker.Object, NullLogger<PreflightCheck>.Instance);
        return (sut, runner, docker);
    }

    [Fact]
    public async Task RunAllAsync_ReportsAllChecks_InOrder()
    {
        var (sut, _, _) = CreateSut();

        var results = await sut.RunAllAsync();

        results.Should().HaveCount(sut.GetCheckIds().Count);
        results.Select(r => r.Name).Should().Contain(new[]
        {
            "Admin-Rechte", "PowerShell-Version", "ExecutionPolicy (CurrentUser)",
            "NuGet-PackageProvider", "PSGallery vertrauenswürdig",
            "Docker installiert", "Docker-Daemon läuft", "Docker im Windows-Modus",
            "BcContainerHelper-Modul", "Kein Legacy-Modul"
        });
    }

    [Fact]
    public async Task RunAllAsync_ProgressReportedPerCheck()
    {
        var (sut, _, _) = CreateSut();
        var reported = new List<CheckResult>();
        var progress = new Progress<CheckResult>(reported.Add);

        var results = await sut.RunAllAsync(progress);

        // Progress-Reports landen über SyncContext eventuell verzögert.
        await Task.Delay(80);
        reported.Should().HaveCount(results.Count);
    }

    [Fact]
    public async Task DockerLinuxMode_ProducesFixableFailure()
    {
        var (sut, _, docker) = CreateSut();
        docker.Setup(d => d.GetContainerModeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ContainerMode.Linux);

        var results = await sut.RunAllAsync();
        var dockerCheck = results.Single(r => r.Name == "Docker im Windows-Modus");

        dockerCheck.Status.Should().Be(CheckStatus.Failed);
        dockerCheck.IsFixable.Should().BeTrue();
        dockerCheck.FixId.Should().Be("switch-to-windows-mode");
    }

    [Fact]
    public async Task BcContainerHelper_NotInstalled_IsFixable()
    {
        var (sut, runner, _) = CreateSut();
        // Default: leerer Output → "nicht installiert".
        runner.WhenScriptContains("Get-Module -ListAvailable -Name BcContainerHelper",
            () => FakePowerShellRunner.Success(/* leer */));

        var results = await sut.RunAllAsync();
        var bcch = results.Single(r => r.Name == "BcContainerHelper-Modul");

        bcch.Status.Should().Be(CheckStatus.Warning);
        bcch.IsFixable.Should().BeTrue();
        bcch.FixId.Should().Be("install-bccontainerhelper");
    }

    [Fact]
    public async Task LegacyModule_Present_IsFixable()
    {
        var (sut, runner, _) = CreateSut();
        runner.WhenScriptContains("Get-Module -ListAvailable -Name navcontainerhelper",
            () => FakePowerShellRunner.Success("yes"));

        var results = await sut.RunAllAsync();
        var legacy = results.Single(r => r.Name == "Kein Legacy-Modul");

        legacy.Status.Should().Be(CheckStatus.Warning);
        legacy.IsFixable.Should().BeTrue();
        legacy.FixId.Should().Be("remove-legacy-module");
    }

    [Fact]
    public async Task ExecutionPolicy_Restricted_IsFixable()
    {
        var (sut, runner, _) = CreateSut();
        runner.WhenScriptContains("Get-ExecutionPolicy",
            () => FakePowerShellRunner.Success("Restricted"));

        var results = await sut.RunAllAsync();
        var policy = results.Single(r => r.Name == "ExecutionPolicy (CurrentUser)");

        policy.Status.Should().Be(CheckStatus.Warning);
        policy.IsFixable.Should().BeTrue();
        policy.FixId.Should().Be("set-execution-policy");
    }
}
