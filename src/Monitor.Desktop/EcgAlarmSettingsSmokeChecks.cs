// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class EcgAlarmSettingsSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show(); window.Pause();
        try
        {
            var alerts = window.Settings.Alerts;
            var settings = alerts.NotificationSettings;
            var browser = settings.EcgBrowser!;
            window.SelectPage(2);
            window.Settings.Tabs.SelectedIndex = 4;
            window.Settings.SectionPages[4].SelectedSection = 0;
            alerts.HeartRateConfirmation.Groups.SelectedItem = alerts.EcgMonitoringPage;
            Require(browser.VisibleCount == EcgAlarmNotices.Descriptors.Count &&
                browser.Rows.Keys.Order(StringComparer.Ordinal).SequenceEqual(EcgAlarmNotices.Descriptors.Select(d => d.Id).Order(StringComparer.Ordinal)) &&
                !browser.GetVisualDescendants().OfType<ComboBox>().Any(),
                "all ECG events are directly browsable without a long selector");
            Capture("all", 1440, 940);
            Require(!browser.FilterTools.IsOpen && !browser.Search.GetVisualAncestors().Contains(browser) &&
                browser.Filter.Bounds.Width == 44 && browser.Filter.Bounds.Height == 44 &&
                browser.Filter.TranslatePoint(default, browser)!.Value.X + browser.Filter.Bounds.Width >= browser.Bounds.Width - 1,
                "only a square filter tool occupies the list's upper right");
            Click(browser.Filter);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Require(browser.FilterTools.IsOpen && browser.Search.IsFocused, "filter tool opens and focuses search");
            browser.Category.SelectedIndex = 5;
            Require(browser.VisibleCount == 4 && browser.Rows["ecg-st-high"].IsVisible && browser.Rows["ecg-qtc"].IsVisible,
                "repolarization category locates ST and QT events");
            browser.Search.Text = "qTc";
            Require(browser.VisibleCount == 2, "case-insensitive abbreviation filters within category");
            CaptureFilters();
            Click(browser.Done);
            Require(!browser.FilterTools.IsOpen && browser.HasFilters && browser.Filter.IsFocused &&
                (AutomationProperties.GetName(browser.Filter) ?? "").Contains("已启用", StringComparison.Ordinal),
                "closing keeps active filters visible through the tool state and restores focus");
            Click(browser.Filter);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Require(browser.Category.SelectedIndex == 5 && browser.Search.Text == "qTc", "reopening preserves the current filter tools");
            Click(browser.Done);
            Capture("qtc", 960, 640);
            Click(browser.Rows["ecg-qtc"]);
            DesktopViewportSmokeChecks.Layout(window, 960, 640);
            var editor = settings.Editors["ecg-qtc"];
            Require(browser.SelectedId == "ecg-qtc" && editor.GetVisualAncestors().Contains(browser) && browser.Back.IsFocused,
                "row opens the existing editor rather than copying its state");
            editor.RepeatSeconds.Value = null;
            Click(browser.Back);
            DesktopViewportSmokeChecks.Layout(window, 960, 640);
            Require(browser.Search.Text == "qTc" && browser.Category.SelectedIndex == 5 && browser.VisibleCount == 2 && browser.Rows["ecg-qtc"].IsFocused &&
                (AutomationProperties.GetName(browser.Rows["ecg-qtc"]) ?? "").Contains("待修正", StringComparison.Ordinal),
                "return retains filters and marks invalid drafts in the summary");
            Click(browser.Rows["ecg-qtc"]);
            Require(editor.RepeatSeconds.Value is null && editor.Advanced.IsExpanded, "reopening preserves invalid inputs for correction");
            editor.RepeatSeconds.Value = 2;
            editor.SelectedSoundMode = 0;
            editor.LatchUntilAcknowledged.IsChecked = true;
            settings.Mode.SelectedIndex = 1;
            Click(browser.Back);
            string summary = (AutomationProperties.GetName(browser.Rows["ecg-qtc"]) ?? "");
            Require(summary.Contains("短警报（默认）", StringComparison.Ordinal) && summary.Contains("恢复后保留", StringComparison.Ordinal) &&
                !summary.Contains("待修正", StringComparison.Ordinal), "summary updates sound source, retention and corrected input");
            settings.Mode.SelectedIndex = 0;
            Require((AutomationProperties.GetName(browser.Rows["ecg-qtc"]) ?? "").Contains("长警报（默认）", StringComparison.Ordinal),
                "global notification policy immediately refreshes inherited ECG summaries");
            window.Localization.Select("en");
            Require(browser.VisibleCount == 2 && (AutomationProperties.GetName(browser.Rows["ecg-qtc"]) ?? "").Contains("Retain after recovery", StringComparison.Ordinal),
                "language changes preserve filtering and refresh summary labels");
            Click(browser.Rows["ecg-qtc"]);
            Capture("detail-en", 960, 640);
            Click(browser.Back);
            DesktopViewportSmokeChecks.Layout(window, 960, 640);
            Click(browser.Filter);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            browser.Search.Text = "no-matching-event";
            Require(browser.VisibleCount == 0 && browser.Clear.IsEnabled, "empty results remain recoverable");
            Click(browser.Clear);
            Require(browser.VisibleCount == 25 && browser.Category.SelectedIndex == 0 && browser.Search.Text == "" && !browser.HasFilters,
                "clear restores all groups and search");
            browser.Search.Text = "PVC";
            Require(browser.VisibleCount > 0 && browser.Rows["ecg-pvc-rate"].IsVisible, "English acronym finds ventricular ectopy");
            window.Localization.Select("zh-CN");
            Click(browser.Clear);
            browser.Search.Text = "室速";
            Require(browser.Rows["ecg-vt"].IsVisible && browser.Rows["ecg-nsvt"].IsVisible, "common Chinese abbreviations find full event names");
            browser.Search.Text = "PVC";
            Require(browser.VisibleCount == 7 && browser.Rows["ecg-bigeminy"].IsVisible, "PVC search finds all ectopy labels in the Chinese UI");
            browser.Search.Text = "室早";
            Require(browser.Rows["ecg-pvc-rate"].IsVisible && !browser.Rows["ecg-qtc"].IsVisible, "Chinese name search finds the requested family");
            Click(browser.Done);
            Capture("pvc", 1000, 720);
            var scroll = browser.GetVisualAncestors().OfType<ScrollViewer>().First();
            Require(scroll.Extent.Width <= scroll.Viewport.Width + 1, "compact list needs no horizontal scrolling");

            void CaptureFilters()
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var popup = (Control)TopLevel.GetTopLevel(browser.Search)!;
                Require(popup.Bounds.Width > 0 && popup.Bounds.Width <= 400 && popup.Bounds.Height < 500,
                    "filter tools fit a compact floating panel");
                using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(popup.Bounds.Width), (int)Math.Ceiling(popup.Bounds.Height)), new Vector(96, 96));
                image.Render(popup);
                image.Save("artifacts/ecg-alarm-settings/filter-tools.png", PngBitmapEncoderOptions.Default);
            }

            void Capture(string name, int width, int height)
            {
                DesktopViewportSmokeChecks.Layout(window, width, height);
                using var image = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
                image.Render((Control)window.Content!);
                Directory.CreateDirectory("artifacts/ecg-alarm-settings");
                image.Save($"artifacts/ecg-alarm-settings/{name}.png", PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("ECG settings: " + message); }
    }
}
