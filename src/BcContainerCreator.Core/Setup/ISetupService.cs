namespace BcContainerCreator.Core.Setup;

/// <summary>
/// Führt einzelne Fix-Aktionen aus, die von <see cref="IPreflightCheck"/>
/// als <c>FixId</c> referenziert werden.
/// </summary>
public interface ISetupService
{
    /// <summary>Führt eine Fix-Aktion über ihre ID aus.</summary>
    Task<bool> ApplyFixAsync(string fixId, CancellationToken cancellationToken = default);

    /// <summary>Bekannte Fix-IDs.</summary>
    IReadOnlyDictionary<string, string> AvailableFixes { get; }
}
