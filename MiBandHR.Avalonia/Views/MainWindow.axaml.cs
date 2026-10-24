using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using MiBandHR.Avalonia.ViewModels;
using MiBandHR.Core.Configuration;

namespace MiBandHR.Avalonia.Views;

public partial class MainWindow : Window {
    private readonly MainViewModel _model;
    private bool _shutdownStarted;
    private bool _allowClose;

    public MainWindow() {
        InitializeComponent();

        _model = new MainViewModel { View = this };
        DataContext = _model;

        RestoreWindowPosition();

        if (ConfigurationManager.Instance.CurrentConfig.Device.TryConnectOnStartup) {
            _model.CommandAutoConnect.Execute(null);
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
        if (!_allowClose) {
            e.Cancel = true;
            if (!_shutdownStarted) {
                _shutdownStarted = true;
                _ = ShutdownThenCloseAsync();
            }

            base.OnClosing(e);
            return;
        }

        // Persist window geometry.
        var config = ConfigurationManager.Instance.CurrentConfig.Window;
        config.Left = Position.X;
        config.Top = Position.Y;
        config.Width = (int)Width;
        config.Height = (int)Height;
        ConfigurationManager.Instance.SaveConfiguration();

        base.OnClosing(e);
    }

    private async Task ShutdownThenCloseAsync() {
        try {
            await _model.ShutdownAsync();
        } catch (Exception exception) {
            MiBandHR.Core.Bluetooth.BluetoothLog.Error($"Unable to shut down cleanly: {exception.Message}");
        }

        _allowClose = true;
        Close();
    }
}
