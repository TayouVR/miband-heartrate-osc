using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using MiBand_Heartrate.Extras;
using MiBand_Heartrate.ViewModels;
using Window = Avalonia.Controls.Window;

namespace MiBand_Heartrate.Views; 

public partial class MainWindow : Window {
    MainWindowViewModel _model = null;
    private static MainWindow _current;

    public static MainWindow Current {
        get => _current ??= new MainWindow();
    }

    public MainWindow() {
        InitializeComponent();
        _current = this;

        _model = (MainWindowViewModel)DataContext;
        _model.View = this;

        // Restore window position
        
        Position = new PixelPoint(Setting.Get("WindowLeft", Position.X), Setting.Get("WindowTop", Position.Y));
        Width = Setting.Get("WindowWidth", (int)MinWidth);
        Height = Setting.Get("WindowHeight", (int)MinHeight);
    }

    private void Window_Closing(object sender, WindowClosingEventArgs e)
    {
        if (_model != null)
        {
            _model.CommandDisconnect.Execute(null);
        }

        // Save window size and positions
        Setting.Set("WindowLeft", Position.X);
        Setting.Set("WindowTop", Position.Y);
        Setting.Set("WindowWidth", (int)Width);
        Setting.Set("WindowHeight", (int)Height);
    }
}