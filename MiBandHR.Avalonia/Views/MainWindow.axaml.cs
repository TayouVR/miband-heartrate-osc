using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using MiBandHR.Avalonia.ViewModels;
using MiBandHR.Core.Configuration;

namespace MiBandHR.Avalonia.Views;

public partial class MainWindow : Window {
    private readonly MainViewModel _model;

    public MainWindow() {
        InitializeComponent();

        _model = new MainViewModel { View = this };
        DataContext = _model;

        // Keep the log pinned to the newest entry.
        _model.LogEntryAdded += ScrollLogToEnd;

        RestoreWindowPosition();
    }

    private void ScrollLogToEnd() {
        if (LogList.ItemCount > 0) {
            LogList.ScrollIntoView(LogList.ItemCount - 1);
        }
    }

    /// <summary>
    /// Restores the window geometry from the shared configuration.
    /// Falls back to the default size/centred position when the stored
    /// position is off-screen (e.g. a monitor was disconnected).
    /// </summary>
    private void RestoreWindowPosition() {
        var config = ConfigurationManager.Instance.CurrentConfig.Window;

        Width = config.Width;
        Height = config.Height;

        if (config.Left >= 0 && config.Top >= 0) {
            var position = new PixelPoint(config.Left, config.Top);
            if (IsOnScreen(position)) {
                Position = position;
                return;
            }
        }

        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    private bool IsOnScreen(PixelPoint position) {
        foreach (var screen in Screens.All) {
            var area = screen.WorkingArea;
            if (position.X >= area.X && position.Y >= area.Y &&
                position.X < area.X + area.Width && position.Y < area.Y + area.Height) {
                return true;
            }
        }

        return false;
    }

    protected override void OnClosing(WindowClosingEventArgs e) {
        // Gracefully disconnect the device (mirrors the WPF behaviour).
        _model.CommandDisconnect.Execute(null);

        // Persist window geometry.
        var config = ConfigurationManager.Instance.CurrentConfig.Window;
        config.Left = Position.X;
        config.Top = Position.Y;
        config.Width = (int)Width;
        config.Height = (int)Height;
        ConfigurationManager.Instance.SaveConfiguration();

        base.OnClosing(e);
    }
}
