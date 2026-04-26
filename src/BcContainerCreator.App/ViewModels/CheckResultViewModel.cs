using BcContainerCreator.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BcContainerCreator.App.ViewModels;

/// <summary>
/// View-Wrapper um <see cref="CheckResult"/>, damit Status-Updates pro Check
/// einzeln in die Liste fließen können.
/// </summary>
public sealed partial class CheckResultViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private CheckStatus _status = CheckStatus.Pending;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isFixable;

    [ObservableProperty]
    private string? _fixId;

    [ObservableProperty]
    private bool _isFixing;

    [ObservableProperty]
    private bool _requiresAdminForFix;

    [ObservableProperty]
    private string? _helpUrl;

    public void Apply(CheckResult result)
    {
        Name = result.Name;
        Status = result.Status;
        Message = result.Message;
        IsFixable = result.IsFixable;
        FixId = result.FixId;
        RequiresAdminForFix = result.RequiresAdminForFix;
        HelpUrl = result.HelpUrl;
    }
}
