// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Localization;
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Desktop;

// Twelve-lead page: paper snapshot, calipers toggle, clear action and readout.
// The paper and calipers share one scaled canvas so measurements stay aligned.
internal sealed class Ecg12PaperPage : Grid
{
    private readonly DesktopLocalization _localization;

    internal Ecg12PaperPage(DesignPreviewTrace paper, Ecg12PaperMeasurement measurement, DesktopLocalization localization, bool measuring)
    {
        _localization = localization;
        Paper = paper;
        Overlay = new Ecg12CaliperOverlay(measurement);
        RowDefinitions = new RowDefinitions("Auto,*");
        localization.Bind(Calipers, ContentControl.ContentProperty, "ecg12.measure");
        localization.Bind(ClearCalipers, ContentControl.ContentProperty, "ecg12.clear");
        localization.Bind(Overlay, AutomationProperties.NameProperty, "ecg12.overlay");
        Calipers.IsChecked = measuring && measurement.CanMeasure;
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 0, 8) };
        toolbar.Children.Add(Calipers);
        toolbar.Children.Add(ClearCalipers);
        toolbar.Children.Add(Readout);
        Children.Add(toolbar);
        var canvas = new Panel { Width = paper.Width, Height = paper.Height };
        canvas.Children.Add(paper);
        canvas.Children.Add(Overlay);
        var viewbox = new Viewbox { Stretch = Stretch.Uniform, Child = canvas };
        SetRow(viewbox, 1);
        Children.Add(viewbox);
        Calipers.IsCheckedChanged += (_, _) =>
        {
            if (Calipers.IsChecked != true) { Overlay.Clear(); }
            Refresh();
            MeasuringChanged?.Invoke(Calipers.IsChecked == true);
        };
        ClearCalipers.Click += (_, _) => Overlay.Clear();
        Overlay.Changed += Refresh;
        Refresh();
    }

    internal DesignPreviewTrace Paper { get; }
    internal Ecg12CaliperOverlay Overlay { get; }
    internal ToggleButton Calipers { get; } = new() { MinHeight = 36, VerticalContentAlignment = VerticalAlignment.Center };
    internal Button ClearCalipers { get; } = new() { MinHeight = 36, VerticalContentAlignment = VerticalAlignment.Center };
    internal TextBlock Readout { get; } = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 14 };
    internal event Action<bool>? MeasuringChanged;

    private void Refresh()
    {
        var measurement = Overlay.Measurement;
        bool measuring = Calipers.IsChecked == true && measurement.CanMeasure;
        Calipers.IsEnabled = measurement.CanMeasure;
        Overlay.IsHitTestVisible = measuring;
        ClearCalipers.IsEnabled = measurement.Display.Region is not null;
        _localization.Bind(Readout, TextBlock.TextProperty, text => Describe(text, measurement.Display, measuring));
    }

    internal static string Describe(ITextLocalizer text, Ecg12PaperMeasurementDisplay display, bool measuring)
    {
        if (display.ReasonCode == "Ecg12Measurement.Disabled") { return text.GetString("ecg12.disabled"); }
        if (display.ReasonCode == "Ecg12Measurement.CourseLocked") { return text.GetString("ecg12.courseLocked"); }
        if (display.Region is not { } region) { return measuring ? text.GetString("ecg12.idle") : ""; }
        string lead = region.Lead == Ecg12PaperLayout.LongLeadIndex
            ? text.GetString("ecg12.rhythmLead")
            : ProjectedEcgDemoSource.LeadNames[region.Lead];
        if (display.Result is not { } result) { return text.Format("ecg12.placing", lead); }
        string elapsed = MeasurementReadout.Exact(result.ElapsedMilliseconds);
        string amplitude = MeasurementReadout.Exact(result.AmplitudeChangeMillivolts);
        return result.AuxiliaryRatePerMinute is { } rate
            ? text.Format("ecg12.resultRate", lead, elapsed, amplitude, Rate(rate))
            : text.Format("ecg12.result", lead, elapsed, amplitude);
    }

    // Rates rarely divide evenly; show one decimal and mark rounding explicitly.
    internal static string Rate(EcgMeasurementRatio rate)
    {
        string exact = MeasurementReadout.Exact(rate);
        int point = exact.IndexOf('.', StringComparison.Ordinal);
        if (!exact.Contains('/', StringComparison.Ordinal) && (point < 0 || exact.Length - point <= 2)) { return exact; }
        double value = (double)rate.Numerator / (double)rate.Denominator;
        return "≈" + value.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
