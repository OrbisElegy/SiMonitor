// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Monitor.Desktop;

internal static class InformationPagesSmokeChecks
{
    internal static void Verify()
    {
        var localization = new DesktopLocalization("zh-CN");
        Control help = DesktopInformationPages.CreateHelp(localization);
        var host = new Border { Child = help, Background = DesktopFluentStyle.Surface };
        var window = new Window { Width = 880, Height = 700, Content = host };
        window.Show();
        try
        {
            DesktopViewportSmokeChecks.Layout(window, 880, 700);
            var search = Find<TextBox>(help, "搜索帮助");
            var category = Find<ComboBox>(help, "帮助分类");
            var count = Find<TextBlock>(help, "帮助搜索结果");
            search.Text = "呼气末 随机";
            Dispatcher.UIThread.RunJobs();
            Require(help.GetVisualDescendants().OfType<SelectableTextBlock>().Count() == 1,
                "multiple search words still intersect across the help text");
            category.SelectedIndex = 2;
            search.Text = "missing-topic-xyz";
            Dispatcher.UIThread.RunJobs();
            Require(count.Text == localization.Get("help.noMatches"), "empty results explain how to recover");
            var clearFilters = help.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, localization.Get("help.clearFilters")));
            clearFilters.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(category.SelectedIndex == 0 && search.Text == "" && help.GetVisualDescendants().OfType<SelectableTextBlock>().Any(),
                "the empty state action clears both filters and restores topics");

            category.SelectedIndex = 2;
            search.Text = "实时氧合";
            var find = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Control };
            window.RaiseEvent(find);
            Require(find.Handled && search.SelectionStart == 0 && search.SelectionEnd == search.Text.Length,
                "Control+F focuses the search and selects its current query");
            var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
            search.RaiseEvent(escape);
            Require(escape.Handled && search.Text == "" && category.SelectedIndex == 2,
                "Escape clears the focused search while preserving its category");

            DesktopViewportSmokeChecks.Layout(window, 420, 700);
            Point categoryOrigin = category.TranslatePoint(default, host)!.Value;
            Point searchOrigin = search.TranslatePoint(default, host)!.Value;
            Require(searchOrigin.Y >= categoryOrigin.Y + category.Bounds.Height && search.Bounds.Width >= 240 &&
                searchOrigin.X + search.Bounds.Width <= host.Bounds.Width,
                "compact help stacks its filters and keeps the search usable without horizontal scrolling");
            Capture(host, "ui-information-help-compact.png");

            host.Child = new Border();
            localization.Select("en");
            host.Child = help;
            DesktopViewportSmokeChecks.Layout(window, 880, 700);
            Require(category.SelectedIndex == 2 && category.Items[0]?.ToString() == "All categories",
                "a detached help page refreshes categories when reattached after a language change");
            search.Text = "realtime oxygenation";
            Dispatcher.UIThread.RunJobs();
            Require(count.Text == "Found 6 help topics" && help.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Vital signs"),
                "reattached help searches the selected language and retains its category");
            Capture(host, "ui-information-help-en.png");
            localization.Select("zh-TW");
            search.Text = "";
            category.SelectedIndex = 0;
            Require(help.GetVisualDescendants().OfType<SelectableTextBlock>().Select(text => text.Text).Order(StringComparer.Ordinal)
                .SequenceEqual(DesktopInformationPages.Topics("zh-TW").Select(topic => topic.Text).Order(StringComparer.Ordinal)),
                "visible help updates every article to Traditional Chinese");

            Control about = DesktopInformationPages.CreateAbout(localization);
            host.Child = about;
            DesktopViewportSmokeChecks.Layout(window, 880, 700);
            var document = Find<TextBox>(about, localization.Get("about.documentText"));
            var documents = Find<ComboBox>(about, localization.Get("about.documentsName"));
            string license = document.Text!;
            Require(document.IsReadOnly && license.Contains("GNU AFFERO GENERAL PUBLIC LICENSE", StringComparison.Ordinal) &&
                document.Bounds.Height is >= 240 and <= 560,
                "the offline license has a bounded, selectable reading viewport");
            documents.SelectedIndex = 1;
            Require(document.Text != license && document.CaretIndex == 0, "switching license documents starts at the new document's beginning");
            DesktopViewportSmokeChecks.Layout(window, 420, 700);
            Capture(host, "ui-information-about-compact.png");
        }
        finally { window.Close(); }
        Console.WriteLine("ok: information pages adapt filters, restore empty searches, follow hidden language changes and expose readable licenses");
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetName(control) == name);

    private static void Capture(Control root, string name)
    {
        // Flush deferred text/template updates before rendering the isolated fixture.
        Dispatcher.UIThread.RunJobs();
        root.UpdateLayout();
        Directory.CreateDirectory("artifacts");
        using var image = new RenderTargetBitmap(new PixelSize((int)root.Bounds.Width, (int)root.Bounds.Height), new Vector(96, 96));
        image.Render(root);
        image.Save(Path.Combine("artifacts", name), PngBitmapEncoderOptions.Default);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Information pages: " + message); }
    }
}
