// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Monitor.Infrastructure.Localization;
using Monitor.Infrastructure.Preferences;

namespace Monitor.Desktop;

internal static class LocalizationSmokeChecks
{
    internal static void Verify()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-language-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "display.json");
        try
        {
            var window = new DesignPreviewWindow(path);
            window.Show();
            try
            {
                window.RestartSettings();
                window.Pause();
                window.ApplySettings();
                window.SelectPage(2);
                window.Settings.Tabs.SelectedIndex = 2;
                var settings = window.Settings;
                var slot = settings.Slots[0];
                var session = window.Session;
                long time = session.SimulationTimeNs;
                long? pending = session.PendingSourceTimeNs;
                slot.Minimum.Text = "unfinished draft";
                settings.HeartRate.Value = 81;
                int selectionChanges = 0;
                settings.Skin.SelectionChanged += (_, _) => selectionChanges++;
                settings.PaperLayout.SelectionChanged += (_, _) => selectionChanges++;
                foreach (var row in settings.Slots) { row.Channel.SelectionChanged += (_, _) => selectionChanges++; }
                string original = File.ReadAllText(path, System.Text.Encoding.UTF8);
                Capture(window, "i18n-display-before-switch.png");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                settings.Language.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs();
                RequireCaption(settings.Skin, "Standard · 5 fixed rows");
                RequireCaption(settings.Slots[1].Channel, "PLETH / relative");
                Require(window.Localization.Locale == "en" && settings.Apply.Content?.ToString() == "Apply" &&
                    settings.Run.Content?.ToString() == "Resume sweep" && slot.Auto.Content?.ToString() == "Auto",
                    $"visible actions and existing row controls change language: {window.Localization.Locale}, {settings.Apply.Content}, {settings.Run.Content}, {slot.Auto.Content}");
                Require(ReferenceEquals(settings, window.Settings) && ReferenceEquals(slot, settings.Slots[0]) &&
                    ReferenceEquals(session, window.Session) && session.SimulationTimeNs == time && session.PendingSourceTimeNs == pending &&
                    window.ActiveTimer is null && settings.HeartRate.Value == 81 && slot.Minimum.Text == "unfinished draft" &&
                    settings.Tabs.SelectedIndex == 2 && File.ReadAllText(path, System.Text.Encoding.UTF8) == original,
                    "language switch preserves drafts, selection, pending source, session, paused state and saved generator preferences");
                Require(settings.Status.Text!.StartsWith("Scheduled", StringComparison.Ordinal) &&
                    AutomationProperties.GetName(settings.Language) == "Language" &&
                    window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Settings"),
                    "messages, accessibility names and navigation refresh through bindings");
                Capture(window, "i18n-settings-en-wide.png");
                window.Width = 1000;
                Capture(window, "i18n-settings-en-compact.png");
                var category = window.GetVisualDescendants().OfType<ComboBox>().Single(combo =>
                    AutomationProperties.GetName(combo) == "Settings categories");
                RequireCaption(category, "Display");
                Require(!window.GetVisualDescendants().Contains(settings.Language), "language is not part of monitor display settings");
                settings.Tabs.SelectedIndex = 0;
                Capture(window, "i18n-general-en-compact.png");
                Require(window.GetVisualDescendants().Contains(settings.Language) && !settings.Apply.IsEffectivelyVisible,
                    "general preferences have a dedicated language control without simulation apply actions");
                RequireCaption(category, "General");
                settings.Tabs.SelectedIndex = 2;
                settings.ApplyDelaySeconds.Value = 61;
                window.ApplySettings();
                Require(settings.Status.Text!.StartsWith("Not applied", StringComparison.Ordinal), "validation is translated without applying drafts");
                settings.Language.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                RequireCaption(settings.Skin, "标准 · 固定 5 行");
                RequireCaption(settings.Slots[1].Channel, "PLETH / 相对量");
                RequireCaption(category, "显示");
                RequireCaption(window.GetVisualDescendants().OfType<ComboBox>().Single(combo =>
                    AutomationProperties.GetName(combo) == "显示参数组"), "监护波形");
                Require(settings.Apply.Content?.ToString() == "应用" && slot.Auto.Content?.ToString() == "自动" &&
                    settings.Status.Text!.StartsWith("未应用", StringComparison.Ordinal), "switching back also refreshes the last validation message");
                Capture(window, "i18n-display-cn-after-switch.png");
                settings.SectionPages[2].Sections.SelectedIndex = 1;
                Capture(window, "i18n-paper-cn-before-switch.png");
                RequireCaption(settings.PaperLayout, "3 × 4 ＋ 长Ⅱ");
                settings.Language.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs();
                RequireCaption(settings.PaperLayout, "3 × 4 + long II");
                Require(selectionChanges == 0, "translation updates never trigger parameter-selection changes");
                settings.Tabs.SelectedIndex = 0;
                settings.Language.SelectedIndex = 1;
                Capture(window, "i18n-general-cn-compact.png");
                RequireCaption(category, "通用");
                window.Width = 1440;
                Capture(window, "i18n-general-cn-wide.png");
                settings.Tabs.SelectedIndex = 2;
                settings.SectionPages[2].Sections.SelectedIndex = 0;
                Capture(window, "i18n-display-cn-returned.png");
                RequireCaption(settings.Skin, "标准 · 固定 5 行");
                RequireCaption(settings.Slots[1].Channel, "PLETH / 相对量");
                window.Start();
                var timer = window.ActiveTimer;
                settings.Language.SelectedIndex = 0;
                Require(ReferenceEquals(timer, window.ActiveTimer) && ReferenceEquals(session, window.Session),
                    "switching language while running preserves the active timer and session");
                window.Pause();
            }
            finally { window.Close(); }

            var reopened = new DesignPreviewWindow(path);
            reopened.Show();
            try
            {
                Require(reopened.Localization.Locale == "en" && reopened.Settings.Language.SelectedIndex == 0 &&
                    reopened.Settings.Apply.Content?.ToString() == "Apply", "saved language is used during construction after restart");
                reopened.ResetAllSettings();
                Require(reopened.Localization.Locale == "zh-CN" && reopened.Settings.Language.SelectedIndex == 1 &&
                    new LanguagePreferenceStore(Path.ChangeExtension(path, ".language.json")).Load(out bool rejected) == "zh-CN" && !rejected,
                    "explicit reset restores and saves the default language");
            }
            finally { reopened.Close(); }

            string blocked = Path.Combine(directory, "blocked.json");
            Directory.CreateDirectory(Path.ChangeExtension(blocked, ".language.json"));
            var failing = new DesignPreviewWindow(blocked);
            failing.Show();
            try
            {
                failing.Settings.Language.SelectedIndex = 0;
                Require(failing.Localization.Locale == "en" && failing.LanguageNotice.IsVisible &&
                    failing.LanguageNotice.Text!.Contains("could not be saved", StringComparison.Ordinal),
                    "failed language save keeps selected language and exposes an actionable warning");
            }
            finally { failing.Close(); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    // Template catalog keys are built from stable identities at run time, so the
    // static key check cannot see them: Chinese must equal each identity, and every
    // key must resolve in English.
    internal static void VerifyTemplateCatalog()
    {
        var chinese = BuiltInLocalizations.Create("zh-CN");
        var english = BuiltInLocalizations.Create("en");
        var templates = DesignPreviewSettings.EcgTemplateIdentities.Select((name, index) => (Key: DesignPreviewSettings.EcgTemplateKey(index), Name: name))
            .Concat(DesignPreviewSettings.RespirationTemplateIdentities.Select((name, index) => (Key: DesignPreviewSettings.RespirationTemplateKey(index), Name: name)))
            .Concat(DesignPreviewSettings.EjectionTemplateIdentities.Select((name, index) => (Key: DesignPreviewSettings.EjectionTemplateKey(index), Name: name)))
            .Concat(EcgChooserGroups.Ordered.Select(name => (Key: EcgChooserGroups.Key(name), Name: name)))
            .Concat(DesignPreviewSettings.TemplateGroupIdentities.Select(name => (Key: DesignPreviewSettings.TemplateGroupKey(name), Name: name)));
        foreach (var (key, name) in templates)
        {
            Require(chinese.GetString(key) == name && !english.Resolve(key).IsMissing && !english.Resolve(key).IsFallback,
                $"template catalog key {key} matches its identity and has an English name");
        }
    }

    // Help topics are per-language resources: every language lists the Chinese topic ids in
    // order, and categories map one to one so the category filter keeps its selection.
    internal static void VerifyHelpCatalog()
    {
        var reference = DesktopInformationPages.Topics("zh-CN");
        foreach (var locale in BuiltInLocalizations.Supported)
        {
            var topics = DesktopInformationPages.Topics(locale.Code);
            Require(topics.Select(t => t.Id).SequenceEqual(reference.Select(t => t.Id)), $"{locale.Code} help lists the reference topics in order");
            Require(topics.All(t => !string.IsNullOrWhiteSpace(t.Title) && !string.IsNullOrWhiteSpace(t.Text)), $"{locale.Code} help topics are complete");
            var pairs = topics.Zip(reference, (topic, chinese) => (topic.Category, Chinese: chinese.Category)).Distinct().ToArray();
            Require(pairs.Select(p => p.Category).Distinct().Count() == pairs.Length && pairs.Select(p => p.Chinese).Distinct().Count() == pairs.Length,
                $"{locale.Code} help categories map one to one");
        }
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            window.SelectPage(3);
            window.Settings.Language.SelectedIndex = BuiltInLocalizations.Supported.ToList().FindIndex(locale => locale.Code == "en");
            Dispatcher.UIThread.RunJobs();
            var search = window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Search help");
            var category = window.GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Help category");
            category.SelectedIndex = 2;
            search.Text = "realtime oxygenation";
            Dispatcher.UIThread.RunJobs();
            var results = window.GetVisualDescendants().OfType<TextBlock>().Single(t => AutomationProperties.GetName(t) == "Help search results");
            string Results() => results.Text ?? "";
            Require(Results() == "Found 6 help topics" && window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Vital signs"),
                "help shown before a language switch follows it and searches the English topics: " + Results());
            window.Settings.Language.SelectedIndex = BuiltInLocalizations.Supported.ToList().FindIndex(locale => locale.Code == "zh-CN");
            Dispatcher.UIThread.RunJobs();
            Require(category.SelectedIndex == 2 && Results() == "没有匹配说明，请更换关键词或分类。",
                "switching back keeps the category filter and search text");
            search.Text = "实时氧合";
            Dispatcher.UIThread.RunJobs();
            Require(Results() == "找到 6 条说明", "Chinese help search finds the same topics: " + Results());
        }
        finally { window.Close(); }
    }

    private static void Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        Directory.CreateDirectory("artifacts");
        using var image = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        var root = (Control)window.Content!;
        root.InvalidateMeasure();
        root.Measure(new Size(window.Width, window.Height));
        root.Arrange(new Rect(0, 0, window.Width, window.Height));
        image.Render(root);
        image.Save(Path.Combine("artifacts", name), PngBitmapEncoderOptions.Default);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Localization: " + message); }
    }

    private static void RequireCaption(ComboBox combo, string expected)
    {
        Require(!combo.IsDropDownOpen && combo.GetVisualDescendants().OfType<TextBlock>()
            .Any(text => text.IsEffectivelyVisible && text.Text == expected),
            "closed selector renders the current translation: " + expected);
    }
}
