using Avalonia.Controls;
using MiBandHR.Avalonia.ViewModels;

namespace MiBandHR.Avalonia.Views;

public partial class AuthenticationKeyWindow : Window {
    public AuthenticationKeyWindow() {
        InitializeComponent();
        DataContext = new AuthenticationKeyViewModel { View = this };
    }
}
