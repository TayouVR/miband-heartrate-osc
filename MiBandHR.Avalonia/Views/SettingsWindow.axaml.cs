using System;
using Avalonia.Controls;
using MiBandHR.Avalonia.ViewModels;

namespace MiBandHR.Avalonia.Views;

public partial class SettingsWindow : Window {
    public SettingsWindow() : this(null) { }

    public SettingsWindow(Action? onSettingsChanged) {
        InitializeComponent();
        DataContext = new SettingsViewModel(onSettingsChanged) { View = this };
    }
}
