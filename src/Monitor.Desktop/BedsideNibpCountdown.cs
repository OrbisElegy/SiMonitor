// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed class BedsideNibpCountdown : Grid
{
    private readonly Border _detail = new();
    private readonly Grid _progress = new() { ColumnDefinitions = new("*,0*") };
    internal TextBlock RemainingText { get; } = new() { FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center };
    internal TextBlock IntervalText { get; } = new() { FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right };
    internal Border Track { get; } = new() { Height = 2, Background = Brush.Parse("#263135"), VerticalAlignment = VerticalAlignment.Bottom };
    internal Border Fill { get; } = new();
    internal double RemainingFraction { get; private set; }
    internal int RemainingSeconds { get; private set; }
    internal bool IsActive { get; private set; }
    internal Control? Detail { get => _detail.Child; set => _detail.Child = value; }

    internal BedsideNibpCountdown()
    {
        ColumnDefinitions = new("*,Auto,*");
        RowDefinitions = new("*,2");
        Children.Add(_detail);
        SetColumn(RemainingText, 1);
        Children.Add(RemainingText);
        SetColumn(IntervalText, 2);
        Children.Add(IntervalText);
        _progress.Children.Add(Fill);
        Track.Child = _progress;
        SetRow(Track, 1);
        SetColumnSpan(Track, 3);
        Children.Add(Track);
        Clear();
    }

    internal void SetColor(IBrush color)
    {
        RemainingText.Foreground = IntervalText.Foreground = Fill.Background = color;
    }

    internal void Update(NibpMeasurement measurement, long simTimeNs, DesktopLocalization localization)
    {
        if (!measurement.Enabled || !measurement.AutoEnabled || measurement.NextMeasurementAtNs is not { } next)
        {
            Clear();
            return;
        }

        long intervalNs = measurement.AutoIntervalMinutes * 60_000_000_000L;
        long remainingNs = Math.Clamp(next - simTimeNs, 0, intervalNs);
        RemainingFraction = (double)remainingNs / intervalNs;
        RemainingSeconds = (int)((remainingNs + 999_999_999) / 1_000_000_000);
        string remaining = RemainingSeconds >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{RemainingSeconds / 3600:00}:{RemainingSeconds / 60 % 60:00}:{RemainingSeconds % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{RemainingSeconds / 60:00}:{RemainingSeconds % 60:00}");
        RemainingText.Text = remaining;
        IntervalText.Text = localization.Format("nibp.minutes", measurement.AutoIntervalMinutes);
        string description = localization.Format("bedside.nibpCountdown", remaining, IntervalText.Text);
        ToolTip.SetTip(this, description);
        AutomationProperties.SetName(this, description);
        _progress.ColumnDefinitions[0].Width = new GridLength(RemainingFraction, GridUnitType.Star);
        _progress.ColumnDefinitions[1].Width = new GridLength(1 - RemainingFraction, GridUnitType.Star);
        IsActive = Track.IsVisible = RemainingText.IsVisible = IntervalText.IsVisible = true;
    }

    internal void Clear()
    {
        RemainingFraction = 0;
        RemainingSeconds = 0;
        RemainingText.Text = IntervalText.Text = "";
        _progress.ColumnDefinitions[0].Width = new GridLength(0, GridUnitType.Star);
        _progress.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        IsActive = Track.IsVisible = RemainingText.IsVisible = IntervalText.IsVisible = false;
        ToolTip.SetTip(this, null);
        AutomationProperties.SetName(this, "");
    }
}
