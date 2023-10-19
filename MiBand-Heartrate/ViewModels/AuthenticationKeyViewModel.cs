using System.Windows;
using System.Windows.Input;
using System.Text.RegularExpressions;
using MiBand_Heartrate.Views;

namespace MiBand_Heartrate; 

public class AuthenticationKeyViewModel : ViewModel {
    private string _key = "";

    public string Key
    {
        get => _key;
        set
        {
            _key = value;
            InvokePropertyChanged("Key");
        }
    }

    public AuthenticationKeyWindow View { get; set; } = null;

    // --------------------------------------

    private ICommand? _commandValid;

    public ICommand CommandValid
    {
        get {
            return _commandValid ??= new RelayCommand<object>("auth.valid",
                "Validate authentication key", o => {
                    if (!Regex.IsMatch(Key, @"^[0-9a-f]{32}$", RegexOptions.IgnoreCase)) {
                        Extras.MessageWindow.ShowError("Authentication key is not valid.", MessageBoxButton.OK);
                        return;
                    }

                    View.AuthenticationKeyResult = Key;
                    //View.DialogResult = true;
                    View.Close();
                });
        }
    }

    private ICommand? _commandCancel;

    public ICommand CommandCancel
    {
        get {
            return _commandCancel ??= new RelayCommand<object>("auth.cancel", "Cancel authentication", o => {
                //View.DialogResult = false;
                View.Close(); });
        }
    }
}