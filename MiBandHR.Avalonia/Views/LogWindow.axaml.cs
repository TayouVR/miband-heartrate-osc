using Avalonia.Controls;
using Avalonia.Interactivity;
using MiBandHR.Avalonia.ViewModels;

namespace MiBandHR.Avalonia.Views;

public partial class LogWindow : Window {
    private readonly MainViewModel? _model;

    public LogWindow() {
        InitializeComponent();
    }

    public LogWindow(MainViewModel model) : this() {
        _model = model;
        DataContext = _model;

        _model.LogEntryAdded += ScrollLogToEnd;
        ScrollLogToEnd();
    }

    private void ScrollLogToEnd() {
        if (LogList.ItemCount > 0) {
            LogList.ScrollIntoView(LogList.ItemCount - 1);
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) {
        Close();
    }

    protected override void OnClosed(System.EventArgs e) {
        if (_model != null) {
            _model.LogEntryAdded -= ScrollLogToEnd;
        }

        base.OnClosed(e);
    }
}
