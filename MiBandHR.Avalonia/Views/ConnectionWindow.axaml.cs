using Avalonia.Controls;
using MiBandHR.Avalonia.ViewModels;

namespace MiBandHR.Avalonia.Views;

public partial class ConnectionWindow : Window
{
    public ConnectionWindow()
    {
        InitializeComponent();
        DataContext = new ConnectionViewModel { View = this };
    }
}
