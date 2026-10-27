// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Runtime.CompilerServices;
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
    internal static void VerifyPausedMonitorLanguage()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            for (int i = 0; i < 100; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            window.Pause();
            var session = window.Session;
            long time = session.SimulationTimeNs;
            var snapshot = session.Measurements;
            var view = window.MonitorView;
            string[] readings = view.NumericTexts.ToArray();
            var transitions = window.Settings.Alerts.AlarmLifecycles.SelectMany(journal => journal.Transitions).ToArray();
            var rate = view.NumericBlocks.Single(block => block.Text?.StartsWith("RR  ", StringComparison.Ordinal) == true);
            string chinese = rate.Text!;
            Require(chinese.EndsWith("次/分", StringComparison.Ordinal), "monitor starts with a Chinese respiratory-rate unit");
            window.Settings.Language.SelectedIndex = 0;
            Require(rate.Text == chinese.Replace("次/分", "/min", StringComparison.Ordinal),
                "paused respiratory rate switches to English without another measurement");
            Require(view.Clock.Text!.Contains("Heartbeat source", StringComparison.Ordinal), "visible monitor clock changes language");
            for (int i = 0; i < 3; i++)
            {
                window.SelectPage(2);
                window.Settings.Language.SelectedIndex = 1;
                window.SelectPage(0);
                Require(rate.Text == chinese && view.Clock.Text!.Contains("心搏音源", StringComparison.Ordinal),
                    "reattached monitor renders the language selected while hidden");
                window.Settings.Language.SelectedIndex = 0;
                Require(rate.Text == chinese.Replace("次/分", "/min", StringComparison.Ordinal),
                    "reattached monitor continues following language changes");
            }
            Require(ReferenceEquals(session, window.Session) && ReferenceEquals(view, window.MonitorView) &&
                Equals(snapshot, session.Measurements) && time == session.SimulationTimeNs && window.ActiveTimer is null &&
                readings.SequenceEqual(view.NumericTexts) && transitions.SequenceEqual(window.Settings.Alerts.AlarmLifecycles.SelectMany(journal => journal.Transitions)),
                "translation preserves paused measurements, monitor identity and alarm lifecycle");
            VerifyTranslatedChoices(window);
        }
        finally { window.Close(); }
    }

    private static void VerifyTranslatedChoices(DesignPreviewWindow window)
    {
        var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
        skin.ShowTherapy.IsChecked = true;
        foreach (int width in new[] { 960, 1440 })
        {
            for (int index = 0; index < skin.PacingType.ItemCount; index++)
            {
                skin.PacingType.SelectedIndex = index;
                DesktopViewportSmokeChecks.Layout(window, width, 940);
                RequireChoiceFits(skin.PacingType);
            }
            using var image = new RenderTargetBitmap(new PixelSize(width, 940), new Vector(96, 96));
            image.Render((Control)window.Content!);
            image.Save($"artifacts/i18n-pacing-en-{width}.png", PngBitmapEncoderOptions.Default);
        }
        window.SelectPage(2);
        window.Settings.Tabs.SelectedIndex = 3;
        window.Settings.SectionPages[3].SelectedSection = 1;
        window.Settings.Sound.BeatSource.SelectedIndex = 2;
        DesktopViewportSmokeChecks.Layout(window, 960, 720);
        RequireChoiceFits(window.Settings.Sound.BeatSource);
        RequireChoiceFits(window.Settings.Sound.PitchSource);
    }

    private static void RequireChoiceFits(ComboBox combo)
    {
        var caption = combo.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text));
        Require(caption.TextLayout.Width <= caption.Bounds.Width + 1 && caption.TextLayout.Height <= caption.Bounds.Height + 1,
            "translated selector displays its full caption: " + caption.Text);
        Require(caption.Bounds.Width <= combo.Bounds.Width - combo.Padding.Left - combo.Padding.Right,
            "selector reserves room for its drop-down arrow");
    }

    internal static void VerifyRetiredMonitorCollection()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        window.Pause();
        try
        {
            var retired = new List<WeakReference>();
            for (int i = 0; i < 3; i++) { retired.AddRange(ReplaceMonitor(window)); }
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Require(retired.All(reference => !reference.IsAlive),
                "applying and restarting release old monitor views, traces and sessions while the window remains alive");
            window.Settings.Language.SelectedIndex = 0;
            Require(window.MonitorView.Clock.Text!.Contains("Heartbeat source", StringComparison.Ordinal),
                "the current monitor still follows language changes after replacements");
        }
        finally { window.Close(); }
    }

    // Avoid test-stack roots keeping the retired controls alive across collection.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] ReplaceMonitor(DesignPreviewWindow window)
    {
        window.UpdateLayout();
        var view = window.MonitorView;
        var trace = view.GetVisualDescendants().OfType<LiveMonitorTrace>().Single();
        var session = window.Session;
        window.ApplySettings();
        Require(!ReferenceEquals(view, window.MonitorView), "apply replaces the monitor before checking collection");
        var applied = window.MonitorView;
        window.RestartSettings();
        window.Pause();
        Require(!ReferenceEquals(session, window.Session), "restart replaces the session before checking collection");
        return [new(view), new(trace), new(session), new(applied)];
    }

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
                RequireCaption(settings.Skin, "4 fixed rows");
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
                RequireCaption(settings.Skin, "固定 4 行");
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
                RequireCaption(settings.Skin, "固定 4 行");
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
                Require(reopened.Localization.Locale == "zh-CN" && reopened.Settings.Language.SelectedIndex == 3 &&
                    new LanguagePreferenceStore(Path.ChangeExtension(path, ".language.json")).Load(out bool rejected) == BuiltInLocalizations.SystemLocale && !rejected,
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
        var traditional = BuiltInLocalizations.Create("zh-TW");
        var templates = DesignPreviewSettings.EcgTemplateIdentities.Select((name, index) => (Key: DesignPreviewSettings.EcgTemplateKey(index), Name: name))
            .Concat(DesignPreviewSettings.RespirationTemplateIdentities.Select((name, index) => (Key: DesignPreviewSettings.RespirationTemplateKey(index), Name: name)))
            .Concat(DesignPreviewSettings.EjectionTemplateIdentities.Select((name, index) => (Key: DesignPreviewSettings.EjectionTemplateKey(index), Name: name)))
            .Concat(DesignPreviewSettings.PlethTemplateIdentities.Select((name, index) => (Key: DesignPreviewSettings.PlethTemplateKey(index), Name: name)))
            .Concat(DesignPreviewSettings.CapnogramTemplateIdentities.Select((name, index) => (Key: DesignPreviewSettings.CapnogramTemplateKey(index), Name: name)))
            .Concat(EcgChooserGroups.Ordered.Select(name => (Key: EcgChooserGroups.Key(name), Name: name)))
            .Concat(DesignPreviewSettings.TemplateGroupIdentities.Select(name => (Key: DesignPreviewSettings.TemplateGroupKey(name), Name: name)));
        foreach (var (key, name) in templates)
        {
            Require(chinese.GetString(key) == name && !english.Resolve(key).IsMissing && !english.Resolve(key).IsFallback &&
                !traditional.Resolve(key).IsMissing && !traditional.Resolve(key).IsFallback,
                $"template catalog key {key} matches its identity and has English and Traditional Chinese names");
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

    internal static void VerifySystemLanguage()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-system-language-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "display.json");
        var store = new LanguagePreferenceStore(Path.ChangeExtension(path, ".language.json"));
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            var first = new DesignPreviewWindow(path);
            first.Show();
            try
            {
                Require(first.Localization.Preference == BuiltInLocalizations.SystemLocale && first.Localization.Locale == "en" &&
                    first.Settings.Language.SelectedIndex == 3, "first launch follows unsupported system languages with English");
                first.SelectPage(2);
                first.Settings.Tabs.SelectedIndex = 0;
                DesktopViewportSmokeChecks.Layout(first, 960, 720);
                RequireCaption(first.Settings.Language, "Follow system language");
                RequireChoiceFits(first.Settings.Language);
                first.Settings.Language.SelectedIndex = 0;
                Require(store.Load(out bool rejected) == "en" && !rejected,
                    "selecting the already displayed language still persists an explicit preference");
            }
            finally { first.Close(); }

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-HK");
            var explicitChoice = new DesignPreviewWindow(path);
            explicitChoice.Show();
            try
            {
                Require(explicitChoice.Localization.Locale == "en" && explicitChoice.Settings.Language.SelectedIndex == 0,
                    "existing explicit language survives a different system language");
                var session = explicitChoice.Session;
                var timer = explicitChoice.ActiveTimer;
                explicitChoice.Settings.HeartRate.Value = 81;
                explicitChoice.Settings.Language.SelectedIndex = 3;
                Require(explicitChoice.Localization.Locale == "zh-TW" &&
                    explicitChoice.Localization.Preference == BuiltInLocalizations.SystemLocale &&
                    store.Load(out bool rejected) == BuiltInLocalizations.SystemLocale && !rejected &&
                    ReferenceEquals(session, explicitChoice.Session) && ReferenceEquals(timer, explicitChoice.ActiveTimer) &&
                    explicitChoice.Settings.HeartRate.Value == 81,
                    "selecting system language resolves Traditional Chinese and preserves drafts and the running session");
                explicitChoice.SelectPage(2);
                explicitChoice.Settings.Tabs.SelectedIndex = 0;
                DesktopViewportSmokeChecks.Layout(explicitChoice, 960, 720);
                RequireCaption(explicitChoice.Settings.Language, "跟隨目前系統語言");
                RequireChoiceFits(explicitChoice.Settings.Language);
                Capture(explicitChoice, "i18n-system-language-tw.png");
            }
            finally { explicitChoice.Close(); }

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-SG");
            var following = new DesignPreviewWindow(path);
            following.Show();
            try
            {
                Require(following.Localization.Locale == "zh-CN" && following.Settings.Language.SelectedIndex == 3,
                    "persisted system preference resolves the current language again after restart");
                following.Settings.Language.SelectedIndex = 0;
                following.ResetAllSettings();
                Require(following.Localization.Locale == "zh-CN" && following.Settings.Language.SelectedIndex == 3 &&
                    store.Load(out bool rejected) == BuiltInLocalizations.SystemLocale && !rejected,
                    "reset restores and saves system following instead of the resolved language");
            }
            finally { following.Close(); }

            File.WriteAllText(Path.ChangeExtension(path, ".language.json"), "invalid", System.Text.Encoding.UTF8);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            var recovery = new DesignPreviewWindow(path);
            recovery.Show();
            try
            {
                Require(recovery.Localization.Locale == "en" && recovery.Settings.Language.SelectedIndex == 3 &&
                    recovery.LanguageNotice.IsVisible && recovery.LanguageNotice.Text!.Contains("system language", StringComparison.Ordinal),
                    "invalid language settings recover to system following with an accurate notice");
            }
            finally { recovery.Close(); }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static void VerifyTraditionalChinese()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-traditional-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "display.json");
        try
        {
            var window = new DesignPreviewWindow(path);
            window.Show();
            try
            {
                var session = window.Session;
                var timer = window.ActiveTimer;
                window.Settings.HeartRate.Value = 81;
                window.Settings.Language.SelectedIndex = 2;
                Require(window.Localization.Locale == "zh-TW" && window.Settings.Apply.Content?.ToString() == "套用" &&
                    window.Settings.HeartRate.Value == 81 && ReferenceEquals(session, window.Session) && ReferenceEquals(timer, window.ActiveTimer),
                    "Traditional Chinese switches the running interface without applying drafts or replacing the session");
                Require(window.Settings.Sound.PublishedTherapy?.Locale == "zh-CN",
                    "Traditional Chinese uses the existing Mandarin therapy recordings");
                window.SelectPage(3);
                Dispatcher.UIThread.RunJobs();
                var search = window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "搜尋說明");
                search.Text = "即時氧合";
                Dispatcher.UIThread.RunJobs();
                Require(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "找到 7 條說明") &&
                    window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "設定患者變化事件"),
                    "Traditional Chinese help searches its independent translated topics, including oxygenation timeline events");
                window.SelectPage(2);
                window.Settings.Tabs.SelectedIndex = 0;
                Capture(window, "i18n-general-tw.png");
            }
            finally { window.Close(); }
            var reopened = new DesignPreviewWindow(path);
            reopened.Show();
            try
            {
                Require(reopened.Localization.Locale == "zh-TW" && reopened.Settings.Language.SelectedIndex == 2,
                    "Traditional Chinese selection survives restart");
            }
            finally { reopened.Close(); }
        }
        finally { Directory.Delete(directory, recursive: true); }
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
