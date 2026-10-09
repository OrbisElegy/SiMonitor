// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Monitor.Desktop;

// Grouped settings navigation: compact selectors mirror headers and section state,
// keyboard movement never selects a header, and state summaries only read drafts.
internal static class SettingsNavigationSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow { Width = 1440, Height = 940 }; window.Show(); window.Pause();
        try
        {
            var session = window.Session;
            window.SelectPage(2);
            VerifyAlarmNavigation(window);
            VerifyVitalNavigation(window);
            VerifyAdvancedNavigation(window);
            VerifyCategoryLanguage(window);
            Require(ReferenceEquals(session, window.Session), "navigation and state summaries never replace the running session");
        }
        finally { window.Close(); }
    }

    private static void VerifyAlarmNavigation(DesignPreviewWindow window)
    {
        var settings = window.Settings;
        settings.Tabs.SelectedIndex = 4;
        var alarms = settings.SectionPages[4];
        int measurements = settings.Alerts.Parameters.Count;
        var rows = alarms.Sections.Items.Cast<ListBoxItem>().ToArray();
        Require(!rows[0].IsEnabled && !rows[measurements + 1].IsEnabled, "alarm list keeps both group headers");
        Layout(window);
        Require(!alarms.Compact && alarms.Sections.IsEffectivelyVisible, "keyboard navigation has a visible wide-layout list");
        alarms.SelectedSection = measurements - 1;
        Press(rows[measurements], Key.Down);
        Require(alarms.SelectedSection == measurements && alarms.Sections.SelectedIndex == measurements + 2,
            "Down from the last measurement skips the shared-settings header");
        Press(rows[measurements + 2], Key.Up);
        Require(alarms.SelectedSection == measurements - 1, "Up returns across the header to the last measurement");
        alarms.SelectedSection = 0;
        Press(rows[1], Key.Up);
        Require(alarms.SelectedSection == 0 && alarms.Sections.SelectedIndex == 1, "Up from the first measurement never selects the group header");

        DesktopViewportSmokeChecks.Layout(window, 1000, 720);
        Capture(window, "ui-preview-alarm-compact-navigation.png");
        var compact = Selector(window, "报警参数组");
        Require(alarms.Compact && compact.ItemCount == rows.Length && compact.SelectedIndex == 1,
            "compact alarm selector mirrors list rows, including group headers");
        RequireCaption(compact, "ECG 心率", "开");
        settings.Alerts.EcgMonitoringEnabled.IsChecked = false;
        RequireCaption(compact, "ECG 心率", "关");
        compact.SelectedIndex = 0;
        Require(alarms.SelectedSection == 0 && compact.SelectedIndex == 1, "compact header rows cannot be selected");
        compact.SelectedIndex = measurements + 2;
        Require(alarms.SelectedSection == measurements, "compact selector navigates to shared settings");
        compact.SelectedIndex = 1;
        settings.Alerts.HeartRateEnabled.IsChecked = true;
        RequireCaption(compact, "ECG 心率", "开");
        compact.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        var containers = Enumerable.Range(0, compact.ItemCount).Select(compact.ContainerFromIndex).OfType<ComboBoxItem>().ToArray();
        Require(containers.Length > measurements + 1 && !containers[0].IsEnabled && !containers[0].Focusable &&
            !containers[measurements + 1].IsEnabled && containers[1].IsEnabled &&
            AutomationProperties.GetName(containers[1]) == "ECG 心率，已启用",
            "compact drop-down shows inert group headers and accessible section state");
        compact.IsDropDownOpen = false;
        settings.Alerts.HeartRateEnabled.IsChecked = false;
        DesktopViewportSmokeChecks.Layout(window);
    }

    private static void VerifyVitalNavigation(DesignPreviewWindow window)
    {
        var settings = window.Settings;
        settings.Tabs.SelectedIndex = 5;
        var vitals = settings.SectionPages[5];
        string?[] names = vitals.Sections.Items.Cast<ListBoxItem>().Select(AutomationProperties.GetName).ToArray();
        Require(names.SequenceEqual(["心率，已关闭", "呼吸与 CO₂", "指脉氧，已关闭", "压力", "随机种子", "手动体征"]),
            "vital sections include manual values after the shared seed, with switch state: " + string.Join(" / ", names));
        settings.CardiacRateEnabled.IsChecked = true;
        Require(vitals.DetailFor(0) == "开", "heart-rate state follows its switch");
        string? seed = settings.RateSeed.Text;
        settings.RateSeed.Text = new string('A', 64);
        vitals.SelectedSection = 4;
        Capture(window, "ui-preview-vitals-seed-error.png");
        Require(vitals.DetailFor(4) == "待修正" && settings.SeedError.IsEffectivelyVisible, "invalid seed is flagged before apply");
        settings.RateSeed.Text = seed;
        Dispatcher.UIThread.RunJobs();
        Require(vitals.DetailFor(4) is null && !settings.SeedError.IsVisible, "corrected seed clears the inline error");
        settings.InspirationPercent.Value = null;
        settings.CvpBaseline.Value = null;
        Require(vitals.DetailFor(1) == "待修正" && vitals.DetailFor(3) == "待修正", "missing breathing and pressure inputs are flagged");
        settings.InspirationPercent.Value = 50;
        settings.CvpBaseline.Value = 6;
        Require(vitals.DetailFor(1) is null && vitals.DetailFor(3) is null, "restored inputs clear their flags");

        var oxygenation = settings.Oxygenation;
        vitals.SelectedSection = 2;
        Require(oxygenation.ItemCount == 3 && !oxygenation.Realtime.IsEnabled && !oxygenation.PatientPage.IsEnabled &&
            !oxygenation.VentilationPage.IsEnabled, "disabled optical source keeps realtime-only tabs disabled");
        settings.OpticalEnabled.IsChecked = true;
        Require(vitals.DetailFor(2) == "开" && oxygenation.Realtime.IsEnabled && !oxygenation.PatientPage.IsEnabled,
            "enabling the source alone still keeps realtime tabs disabled");
        oxygenation.Realtime.IsChecked = true;
        oxygenation.SelectedItem = oxygenation.PatientPage;
        Capture(window, "ui-preview-vitals-oxygenation-patient.png");
        Require(oxygenation.PatientPage.IsEnabled && oxygenation.VentilationPage.IsEnabled &&
            oxygenation.Patient.Age.GetVisualAncestors().Contains(settings) && !settings.OpticalTarget.GetVisualAncestors().Contains(settings),
            $"patient baseline is a separate tab from the optical source: {oxygenation.PatientPage.IsEnabled} {oxygenation.VentilationPage.IsEnabled} {oxygenation.Patient.Age.GetVisualAncestors().Contains(settings)} {settings.OpticalTarget.GetVisualAncestors().Contains(settings)} {oxygenation.SelectedIndex}");
        var scroll = oxygenation.Patient.Age.GetVisualAncestors().OfType<ScrollViewer>().First();
        Require(scroll.Extent.Width <= scroll.Viewport.Width + 1, "patient tab has no horizontal scrolling");
        decimal? weight = oxygenation.Patient.Weight.Value;
        oxygenation.Patient.Weight.Value = null;
        Require(vitals.DetailFor(2) == "待修正", "invalid realtime patient draft is flagged in navigation");
        oxygenation.Patient.Weight.Value = weight;
        oxygenation.SelectedItem = oxygenation.VentilationPage;
        Capture(window, "ui-preview-vitals-oxygenation-ventilation.png");
        Require(oxygenation.UpdateVentilation.GetVisualAncestors().Contains(settings) && vitals.DetailFor(2) == "开",
            "ventilation inputs and update action share one tab");
        oxygenation.Realtime.IsChecked = false;
        Require(oxygenation.SelectedIndex == 0 && !oxygenation.PatientPage.IsEnabled, "turning realtime off returns to the source tab");
        Capture(window, "ui-preview-vitals-oxygenation-source.png");
        for (int section = 0; section < 5; section++)
        {
            vitals.SelectedSection = section;
            Dispatcher.UIThread.RunJobs();
            Require(!settings.GetVisualDescendants().OfType<TextBox>().Any(box => box.Text == "尚未接入"),
                "vital pages only contain connected controls");
        }
        settings.OpticalEnabled.IsChecked = false;
        settings.CardiacRateEnabled.IsChecked = false;
        vitals.SelectedSection = 0;
    }

    private static void VerifyAdvancedNavigation(DesignPreviewWindow window)
    {
        var settings = window.Settings;
        settings.EcgSelection = 0;
        settings.Tabs.SelectedIndex = 1;
        settings.Tabs.SelectedIndex = 6;
        var advanced = settings.SectionPages[6];
        string?[] headers = advanced.Sections.Items.Cast<ListBoxItem>().Where(item => !item.IsEnabled)
            .Select(item => ((TextBlock)item.Content!).Text).ToArray();
        Require(headers.Length == 2 && headers[0] == "当前波形" && headers[1] is "概览" or "概览与工具",
            "advanced navigation separates waveform parameters from overview pages");
        Require(advanced.DetailFor(0) is null && settings.PacingPermissions.Parent is not null &&
            advanced.DetailFor(1) is null && advanced.DetailFor(2) == "无参数",
            "all ECG templates expose pacing permission while genuinely empty groups retain their marker");
        Capture(window, "ui-preview-advanced-groups.png");
        settings.EcgSelection = 148;
        settings.Tabs.SelectedIndex = 1;
        settings.OpenAdvanced(0);
        Require(advanced.SelectedSection == 0 && advanced.DetailFor(0) is null && settings.InfarctionParameters.Parent is not null,
            "editable ECG template clears the empty marker");
        settings.OpenAdvanced(3);
        Require(advanced.SelectedSection == 2, "ejection entry still opens its group with headers present");
        settings.EcgSelection = 0;
        settings.Tabs.SelectedIndex = 1;
    }

    // Categories follow the selected language even where section titles are not translated yet.
    private static void VerifyCategoryLanguage(DesignPreviewWindow window)
    {
        var settings = window.Settings;
        (int Tab, string English, string Chinese)[] categories =
            [(3, "Sound", "声音"), (4, "Alarms", "报警"), (5, "Vital signs", "生命体征"), (6, "Advanced parameters", "高级参数")];
        settings.Language.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        foreach (var (tab, english, _) in categories)
        {
            settings.Tabs.SelectedIndex = tab;
            Layout(window);
            var sections = settings.SectionPages[tab];
            Require(sections.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.StartsWith(english + " / ", StringComparison.Ordinal) == true) &&
                AutomationProperties.GetName(sections.Sections) == english + " parameter groups",
                english + " heading and navigation name use the selected language");
        }
        Require(settings.Status.Text == "Apply continues the current waveform; Restart clears the sweep history.",
            "idle footer status follows the selected language");
        Capture(window, "ui-preview-alarms-en.png");
        settings.Tabs.SelectedIndex = 4;
        settings.Language.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Layout(window);
        Require(settings.SectionPages[4].GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "报警 / ECG 心率") &&
            AutomationProperties.GetName(settings.SectionPages[4].Sections) == "报警参数组" &&
            settings.SectionPages[4].DetailFor(0) == "关", "switching back restores Chinese headings without resetting section state");
    }

    private static ComboBox Selector(DesignPreviewWindow window, string name) =>
        window.Settings.GetVisualDescendants().OfType<ComboBox>().Single(combo => combo.IsEffectivelyVisible && AutomationProperties.GetName(combo) == name);

    private static void RequireCaption(ComboBox combo, params string[] expected)
    {
        Dispatcher.UIThread.RunJobs();
        string?[] texts = combo.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
        Require(expected.All(texts.Contains), "compact selector caption shows " + string.Join(" ", expected) + ": " + string.Join(" / ", texts));
    }

    private static void Press(Control target, Key key)
    {
        target.Focus(NavigationMethod.Tab);
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        Dispatcher.UIThread.RunJobs();
    }

    private static void Layout(Window window)
    {
        var root = (Control)window.Content!;
        DesktopViewportSmokeChecks.Layout(window,
            double.IsNaN(root.Width) ? 1440 : root.Width,
            double.IsNaN(root.Height) ? 940 : root.Height);
    }

    private static void Capture(Window window, string name)
    {
        Layout(window);
        Directory.CreateDirectory("artifacts");
        using var image = new RenderTargetBitmap(new PixelSize((int)((Control)window.Content!).Width, (int)((Control)window.Content!).Height), new Vector(96, 96));
        image.Render((Control)window.Content!);
        image.Save(Path.Combine("artifacts", name), PngBitmapEncoderOptions.Default);
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Settings navigation: " + message); } }
}
