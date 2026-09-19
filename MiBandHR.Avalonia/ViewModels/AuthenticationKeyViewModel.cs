using System.Text.RegularExpressions;
using MiBandHR.Avalonia.Views;

namespace MiBandHR.Avalonia.ViewModels;

public partial class AuthenticationKeyViewModel : ViewModelBase
{
    private string _key = "";
    private string _errorText = "";

    public AuthenticationKeyViewModel()
    {
        CommandOk = new RelayCommand(_ => Validate());
        CommandCancel = new RelayCommand(_ => View?.Close(null));
    }

    /// <summary>The window this view model belongs to (set by the view).</summary>
    public AuthenticationKeyWindow? View { get; set; }

    public string Key
    {
        get => _key;
        set
        {
            _key = value;
            OnPropertyChanged(nameof(Key));
        }
    }

    /// <summary>Validation error shown under the text box (empty when valid).</summary>
    public string ErrorText
    {
        get => _errorText;
        set
        {
            _errorText = value;
            OnPropertyChanged(nameof(ErrorText));
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public RelayCommand CommandOk { get; }
    public RelayCommand CommandCancel { get; }

    private void Validate()
    {
        if (!KeyRegex().IsMatch(Key))
        {
            ErrorText = "Authentication key is not valid (expected 32 hexadecimal characters).";
            return;
        }

        View?.Close(Key);
    }

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.IgnoreCase)]
    private static partial Regex KeyRegex();
}
