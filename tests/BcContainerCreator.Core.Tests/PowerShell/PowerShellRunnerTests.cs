using BcContainerCreator.Core.PowerShell;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BcContainerCreator.Core.Tests.PowerShell;

/// <summary>
/// Pfade des echten <see cref="PowerShellRunner"/>, die ohne tatsächliches
/// Starten von <c>powershell.exe</c> testbar sind. Alles, was den externen
/// Subprozess braucht, gehört in Integrations-Tests — hier wird nur die
/// Cancellation-vor-Start-Semantik abgedeckt.
/// </summary>
public class PowerShellRunnerTests
{
    [Fact]
    public async Task ExecuteAsync_CancelledBeforeStart_ReturnsCancelledResult()
    {
        await using var runner = new PowerShellRunner(NullLogger<PowerShellRunner>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await runner.ExecuteAsync("Write-Output 'hi'", cancellationToken: cts.Token);

        // Cancellation VOR dem Gate liefert das gleiche Resultat-Schema wie
        // Cancellation während eines Runs — kein OperationCanceledException
        // rauspropagiert, sondern PSResult mit WasCancelled=true.
        result.WasCancelled.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
    }
}
