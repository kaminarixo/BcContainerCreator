using System.ComponentModel;
using System.Diagnostics;
using BcContainerCreator.Core.Setup;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BcContainerCreator.Core.Tests.Setup;

/// <summary>
/// Tests für <see cref="ElevationService"/> über den injizierbaren
/// Prozess-Start-Hook (analog zur Admin-Probe im SetupService). Es wird nie
/// ein echter UAC-Prompt ausgelöst — der Hook startet stattdessen harmlose
/// cmd.exe-Prozesse oder wirft die erwarteten Win32-Fehler.
/// </summary>
public class ElevationServiceTests
{
    private static ElevationService CreateSut(Func<ProcessStartInfo, Process?> startProcess) =>
        new(NullLogger<ElevationService>.Instance, startProcess);

    /// <summary>Startet einen echten, unsichtbaren cmd.exe-Prozess mit gegebenem Exit-Code.</summary>
    private static Process StartCmd(string arguments) =>
        Process.Start(new ProcessStartInfo("cmd.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

    [Fact]
    public async Task RunElevatedAsync_UacCancelled_ReturnsFalseInsteadOfThrowing()
    {
        // ERROR_CANCELLED (1223): User hat den UAC-Prompt verworfen — das ist
        // ein regulärer "nicht ausgeführt"-Fall, kein Fehler.
        var sut = CreateSut(_ => throw new Win32Exception(1223));

        var ok = await sut.RunElevatedAsync("whatever.exe", "");

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task RunElevatedAsync_OtherWin32Error_ReturnsFalse()
    {
        // 2 = ERROR_FILE_NOT_FOUND — z. B. DockerCli.exe existiert nicht.
        var sut = CreateSut(_ => throw new Win32Exception(2));

        var ok = await sut.RunElevatedAsync("missing.exe", "");

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task RunElevatedAsync_ProcessStartReturnsNull_ReturnsFalse()
    {
        var sut = CreateSut(_ => null);

        var ok = await sut.RunElevatedAsync("whatever.exe", "");

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task RunElevatedAsync_ExitCodeZero_ReturnsTrue()
    {
        var sut = CreateSut(_ => StartCmd("/c exit 0"));

        var ok = await sut.RunElevatedAsync("ignored.exe", "");

        ok.Should().BeTrue();
    }

    [Fact]
    public async Task RunElevatedAsync_NonZeroExitCode_ReturnsFalse()
    {
        var sut = CreateSut(_ => StartCmd("/c exit 3"));

        var ok = await sut.RunElevatedAsync("ignored.exe", "");

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task RunElevatedAsync_Timeout_ReturnsFalse()
    {
        // Lang laufender Prozess + sehr kurzer Timeout → false statt Hängen.
        // Der Prozess wird nach dem Assert aufgeräumt (der Service killt nicht).
        Process? spawned = null;
        var sut = CreateSut(_ => spawned = StartCmd("/c ping 127.0.0.1 -n 10 > nul"));

        try
        {
            var ok = await sut.RunElevatedAsync(
                "ignored.exe", "", timeout: TimeSpan.FromMilliseconds(200));

            ok.Should().BeFalse();
        }
        finally
        {
            try { spawned?.Kill(entireProcessTree: true); } catch { /* schon beendet */ }
        }
    }

    [Fact]
    public async Task RunElevatedAsync_CancelledToken_ReturnsFalse()
    {
        Process? spawned = null;
        var sut = CreateSut(_ => spawned = StartCmd("/c ping 127.0.0.1 -n 10 > nul"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var ok = await sut.RunElevatedAsync("ignored.exe", "", cancellationToken: cts.Token);

            ok.Should().BeFalse();
        }
        finally
        {
            try { spawned?.Kill(entireProcessTree: true); } catch { /* schon beendet */ }
        }
    }

    [Fact]
    public async Task RunElevatedAsync_EmptyFileName_Throws()
    {
        var sut = CreateSut(_ => null);

        var act = async () => await sut.RunElevatedAsync("", "");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RunElevatedAsync_UsesRunasVerbAndShellExecute()
    {
        // Ohne UseShellExecute=true greift Verb=runas nicht — Regression
        // würde die UAC-Elevation still deaktivieren.
        ProcessStartInfo? captured = null;
        var sut = CreateSut(psi =>
        {
            captured = psi;
            return StartCmd("/c exit 0");
        });

        await sut.RunElevatedAsync("some.exe", "-arg");

        captured.Should().NotBeNull();
        captured!.Verb.Should().Be("runas");
        captured.UseShellExecute.Should().BeTrue();
        captured.FileName.Should().Be("some.exe");
        captured.Arguments.Should().Be("-arg");
    }
}
