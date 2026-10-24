using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using MiBandHR.Avalonia.ViewModels;

namespace MiBandHR.Avalonia.Views;

public partial class LogWindow : Window {
    private readonly MainViewModel? _model;
    private SelectableTextBlock TextControl => LogText!;
    private ScrollViewer ScrollViewerControl => LogScrollViewer!;
    private bool HasSelection => TextControl.SelectionStart != TextControl.SelectionEnd;

    public LogWindow() {
        InitializeComponent();
    }

    public LogWindow(MainViewModel model) : this() {
        _model = model;
        DataContext = _model;

        _model.LogEntries.CollectionChanged += LogEntries_CollectionChanged;
        foreach (var entry in _model.LogEntries) {
            AppendLogEntry(entry);
        }

        ScrollLogToEnd();
    }

    private void LogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        switch (e.Action) {
            case NotifyCollectionChangedAction.Add when e.NewItems != null:
                if (e.NewStartingIndex != TextControl.Inlines.Count) {
                    RebuildLogText();
                    return;
                }

                foreach (var item in e.NewItems) {
                    if (item is LogEntryViewModel entry) {
                        AppendLogEntry(entry);
                    }
                }

                if (!HasSelection) {
                    ScrollLogToEnd();
                }

                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems != null:
                RemoveLogEntries(e);
                break;
            case NotifyCollectionChangedAction.Reset:
                TextControl.ClearSelection();
                TextControl.Inlines.Clear();
                foreach (var entry in _model!.LogEntries) {
                    AppendLogEntry(entry);
                }

                break;
            default:
                RebuildLogText();
                break;
        }
    }

    private void RemoveLogEntries(NotifyCollectionChangedEventArgs e) {
        if (e.OldStartingIndex < 0) {
            RebuildLogText();
            return;
        }

        var hasSelection = HasSelection;
        var selectionStart = TextControl.SelectionStart;
        var selectionEnd = TextControl.SelectionEnd;
        var removalStart = GetTextOffset(e.OldStartingIndex);
        var removedLength = 0;

        foreach (var item in e.OldItems!) {
            if (item is LogEntryViewModel entry) {
                removedLength += GetEntryLength(entry);
            }

            TextControl.Inlines.RemoveAt(e.OldStartingIndex);
        }

        if (hasSelection) {
            var removalEnd = removalStart + removedLength;
            RestoreSelection(
                AdjustSelectionPosition(selectionStart, removalStart, removalEnd, removedLength),
                AdjustSelectionPosition(selectionEnd, removalStart, removalEnd, removedLength)
            );
        } else {
            ScrollLogToEnd();
        }
    }

    private static int AdjustSelectionPosition(int position, int removalStart, int removalEnd, int removedLength) {
        if (position <= removalStart) {
            return position;
        }

        return position >= removalEnd ? position - removedLength : removalStart;
    }

    private int GetTextOffset(int entryIndex) {
        var offset = 0;
        for (var i = 0; i < entryIndex && i < _model!.LogEntries.Count; i++) {
            offset += GetEntryLength(_model.LogEntries[i]);
        }

        return offset;
    }

    private static int GetEntryLength(LogEntryViewModel entry) => entry.Text.Length + 1;

    private void AppendLogEntry(LogEntryViewModel entry) {
        TextControl.Inlines.Add(new Run(entry.Text + "\n") {
            Foreground = new SolidColorBrush(Color.Parse(entry.Foreground))
        });
    }

    private void RebuildLogText() {
        var hasSelection = HasSelection;
        var selectionStart = TextControl.SelectionStart;
        var selectionEnd = TextControl.SelectionEnd;
        TextControl.Inlines.Clear();
        foreach (var entry in _model!.LogEntries) {
            AppendLogEntry(entry);
        }

        if (hasSelection) {
            RestoreSelection(selectionStart, selectionEnd);
        } else {
            ScrollLogToEnd();
        }
    }

    private void RestoreSelection(int selectionStart, int selectionEnd) {
        var textLength = 0;
        foreach (var entry in _model!.LogEntries) {
            textLength += GetEntryLength(entry);
        }

        TextControl.SelectionStart = System.Math.Clamp(selectionStart, 0, textLength);
        TextControl.SelectionEnd = System.Math.Clamp(selectionEnd, 0, textLength);
    }

    private void ScrollLogToEnd() {
        ScrollViewerControl.ScrollToEnd();
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) {
        Close();
    }

    protected override void OnClosed(System.EventArgs e) {
        if (_model != null) {
            _model.LogEntries.CollectionChanged -= LogEntries_CollectionChanged;
        }

        base.OnClosed(e);
    }
}
