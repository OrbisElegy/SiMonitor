// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Infrastructure.Localization;

namespace Monitor.Desktop;

internal sealed partial class DesignPreviewSettings
{
    internal DesktopLocalization Localization { get; }
    internal ComboBox Language { get; } = new()
    {
        ItemsSource = BuiltInLocalizations.Supported.Select(locale => locale.NativeName).ToArray(),
        MinHeight = 44,
        MinWidth = 220,
        HorizontalAlignment = HorizontalAlignment.Left
    };
    internal event Action<string>? LanguageChanged;

    private void InitializeLocalization()
    {
        Language.SelectedIndex = BuiltInLocalizations.Supported.ToList().FindIndex(locale => locale.Code == Localization.Locale);
        Language.SelectionChanged += (_, args) =>
        {
            if (ReferenceEquals(args.Source, Language) && Language.SelectedIndex >= 0)
            { LanguageChanged?.Invoke(BuiltInLocalizations.Supported[Language.SelectedIndex].Code); }
        };
        Localization.Bind(Language, AutomationProperties.NameProperty, "settings.language");
        Localization.Bind(Apply, ContentControl.ContentProperty, "common.apply");
        Localization.Bind(Restart, ContentControl.ContentProperty, "settings.restart");
        Localization.Bind(ResetAll, ContentControl.ContentProperty, "settings.resetAction");
        Localization.SetChoices(Skin, "display.skinThree", "display.skinFive", "display.skinSeven");
        Localization.SetChoices(PaperLayout, "display.layoutThree", "display.layoutSix");
        Localization.Bind(Skin, AutomationProperties.NameProperty, "display.skin");
        Localization.Bind(PaperLayout, AutomationProperties.NameProperty, "display.paperLayout");
    }

    private ScrollViewer BuildGeneralPage()
    {
        var heading = LocalizedText("settings.general");
        heading.FontSize = 20;
        heading.FontWeight = FontWeight.SemiBold;
        var label = new Label { Target = Language, Padding = new Thickness(0) };
        Localization.Bind(label, ContentControl.ContentProperty, "settings.language");
        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(heading);
        content.Children.Add(label);
        content.Children.Add(Language);
        content.Children.Add(LocalizedText("settings.languageImmediate"));
        return SettingsScroll.Create(new Border
        {
            Margin = new Thickness(16),
            Padding = new Thickness(20),
            Background = DesktopFluentStyle.Surface,
            BorderBrush = DesktopFluentStyle.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = content
        });
    }

    private TextBlock LocalizedText(string key)
    {
        var text = Text("");
        Localization.Bind(text, TextBlock.TextProperty, key);
        return text;
    }
}
