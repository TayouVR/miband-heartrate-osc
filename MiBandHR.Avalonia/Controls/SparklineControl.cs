using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MiBandHR.Avalonia.Controls;

public readonly record struct HeartratePoint(DateTime Timestamp, int Bpm);

public class SparklineControl : Control {
    public static readonly StyledProperty<IEnumerable<HeartratePoint>?> PointsProperty =
        AvaloniaProperty.Register<SparklineControl, IEnumerable<HeartratePoint>?>(nameof(Points));

    public static readonly StyledProperty<IBrush?> StrokeBrushProperty =
        AvaloniaProperty.Register<SparklineControl, IBrush?>(nameof(StrokeBrush));

    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<SparklineControl, IBrush?>(nameof(FillBrush));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<SparklineControl, double>(nameof(StrokeThickness), 2.0);

    static SparklineControl() {
        AffectsRender<SparklineControl>(PointsProperty, StrokeBrushProperty, FillBrushProperty, StrokeThicknessProperty);
    }

    public SparklineControl() {
        IsHitTestVisible = false;
    }

    public IEnumerable<HeartratePoint>? Points {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public IBrush? StrokeBrush {
        get => GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public IBrush? FillBrush {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public double StrokeThickness {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);

        if (change.Property == PointsProperty) {
            if (change.OldValue is INotifyCollectionChanged oldCollection) {
                oldCollection.CollectionChanged -= OnCollectionChanged;
            }
            if (change.NewValue is INotifyCollectionChanged newCollection) {
                newCollection.CollectionChanged += OnCollectionChanged;
            }
            InvalidateVisual();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        InvalidateVisual();
    }

    public override void Render(DrawingContext context) {
        base.Render(context);

        var bounds = Bounds;
        var width = bounds.Width;
        var height = bounds.Height;

        if (width <= 0 || height <= 0 || Points == null) {
            return;
        }

        var pointsList = Points as IList<HeartratePoint> ?? Points.ToList();
        if (pointsList.Count == 0) {
            return;
        }

        Color accentColor;
        if (this.TryFindResource("SystemAccentColor", out var res) && res is Color c) {
            accentColor = c;
        }
        else if (Application.Current != null && Application.Current.TryFindResource("SystemAccentColor", out var appRes) && appRes is Color ac) {
            accentColor = ac;
        }
        else {
            accentColor = Color.FromRgb(0, 120, 215);
        }

        var stroke = StrokeBrush ?? new SolidColorBrush(Color.FromArgb(200, accentColor.R, accentColor.G, accentColor.B));
        var fill = FillBrush ?? new LinearGradientBrush {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops {
                new GradientStop(Color.FromArgb(80, accentColor.R, accentColor.G, accentColor.B), 0.0),
                new GradientStop(Color.FromArgb(10, accentColor.R, accentColor.G, accentColor.B), 1.0)
            }
        };

        var pen = new Pen(stroke, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        const double verticalMargin = 6.0;
        var usableHeight = Math.Max(1.0, height - (verticalMargin * 2));

        if (pointsList.Count == 1) {
            var y = height / 2.0;
            var singleLine = new StreamGeometry();
            using (var ctx = singleLine.Open()) {
                ctx.BeginFigure(new Point(0, y), false);
                ctx.LineTo(new Point(width, y));
                ctx.EndFigure(false);
            }

            var singleFill = new StreamGeometry();
            using (var ctx = singleFill.Open()) {
                ctx.BeginFigure(new Point(0, height), true);
                ctx.LineTo(new Point(0, y));
                ctx.LineTo(new Point(width, y));
                ctx.LineTo(new Point(width, height));
                ctx.EndFigure(true);
            }

            context.DrawGeometry(fill, null, singleFill);
            context.DrawGeometry(null, pen, singleLine);
            return;
        }

        var minBpm = pointsList.Min(p => p.Bpm);
        var maxBpm = pointsList.Max(p => p.Bpm);

        double effectiveMin = minBpm;
        double effectiveMax = maxBpm;
        if (effectiveMax - effectiveMin < 10) {
            var pad = (10 - (effectiveMax - effectiveMin)) / 2.0;
            effectiveMin = Math.Max(0, effectiveMin - pad);
            effectiveMax += pad;
        }

        var bpmRange = Math.Max(1.0, effectiveMax - effectiveMin);

        var minTime = pointsList[0].Timestamp;
        var maxTime = pointsList[^1].Timestamp;
        var totalSec = (maxTime - minTime).TotalSeconds;

        var coords = new List<Point>(pointsList.Count);
        for (var i = 0; i < pointsList.Count; i++) {
            var pt = pointsList[i];
            var x = totalSec > 0.001
                ? Math.Clamp((pt.Timestamp - minTime).TotalSeconds / totalSec * width, 0, width)
                : (double)i / (pointsList.Count - 1) * width;

            var normalizedY = (pt.Bpm - effectiveMin) / bpmRange;
            var y = verticalMargin + ((1.0 - normalizedY) * usableHeight);
            coords.Add(new Point(x, y));
        }

        var lineGeometry = new StreamGeometry();
        using (var ctx = lineGeometry.Open()) {
            ctx.BeginFigure(coords[0], false);
            for (var i = 1; i < coords.Count; i++) {
                ctx.LineTo(coords[i]);
            }
            ctx.EndFigure(false);
        }

        var fillGeometry = new StreamGeometry();
        using (var ctx = fillGeometry.Open()) {
            ctx.BeginFigure(new Point(coords[0].X, height), true);
            ctx.LineTo(coords[0]);
            for (var i = 1; i < coords.Count; i++) {
                ctx.LineTo(coords[i]);
            }
            ctx.LineTo(new Point(coords[^1].X, height));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(fill, null, fillGeometry);
        context.DrawGeometry(null, pen, lineGeometry);
    }
}
