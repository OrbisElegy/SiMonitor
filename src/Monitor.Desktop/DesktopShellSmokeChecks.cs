// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Monitor.Desktop;

internal static class DesktopShellSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow(startAudioOutput: false);
        window.Show();
        window.Pause();
        try
        {
            foreach (string locale in new[] { "zh-CN", "zh-TW", "en" })
            {
                window.Localization.Select(locale);
                foreach (int width in new[] { 960, 1440 })
                {
                    DesktopViewportSmokeChecks.Layout(window, width, 640);
                    var navigation = window.Shell.Navigation;
                    var items = navigation.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
                    Require(items.Length == 5 && items.All(item => item.Bounds.Height >= 44),
                        "all main pages retain a full navigation hit target at the minimum height");
                    Require(items.All(item => !string.IsNullOrEmpty(AutomationProperties.GetName(item))),
                        "navigation items retain localized accessible names");
                    Require(window.MonitorTrace.Bounds.Width > 200 && window.MonitorTrace.Bounds.Height > 200,
                        "main shell reserves usable waveform space at each supported viewport");
                }
            }

            window.Shell.Navigation.SelectedIndex = 2;
            Require(window.Page == 2, "main navigation changes the active page");
            window.Settings.Slots[0].Minimum.Text = "unfinished draft";
            window.SelectPage(3);
            DesktopViewportSmokeChecks.Layout(window);
            var search = window.Shell.GetVisualDescendants().OfType<TextBox>()
                .Single(control => AutomationProperties.GetName(control) == window.Localization.Get("help.searchName"));
            search.Text = "oxygen";
            window.SelectPage(2);
            Require(window.Settings.Slots[0].Minimum.Text == "unfinished draft",
                "page navigation preserves an unfinished settings draft");
            window.SelectPage(3);
            DesktopViewportSmokeChecks.Layout(window);
            Require(ReferenceEquals(search, window.Shell.GetVisualDescendants().OfType<TextBox>()
                .Single(control => AutomationProperties.GetName(control) == window.Localization.Get("help.searchName"))) && search.Text == "oxygen",
                "returning to help preserves its search control and query");

            window.SelectPage(4);
            DesktopViewportSmokeChecks.Layout(window);
            var about = window.Shell.GetVisualDescendants().OfType<SettingsSections>().Single();
            about.SelectedSection = 1;
            window.SelectPage(0);
            window.SelectPage(4);
            DesktopViewportSmokeChecks.Layout(window);
            Require(ReferenceEquals(about, window.Shell.GetVisualDescendants().OfType<SettingsSections>().Single()) && about.SelectedSection == 1,
                "returning to about preserves the selected legal section");

            VerifyNavigationModes(window);
            Require(window.Settings.Slots[0].Minimum.Text == "unfinished draft",
                "compact and overlay page navigation preserve the existing settings draft");
        }
        finally { window.Close(); }
    }

    private static void VerifyNavigationModes(DesignPreviewWindow window)
    {
        window.SelectPage(0);
        var shell = window.Shell;
        var split = shell.GetVisualDescendants().OfType<SplitView>().Single();
        var pane = split.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "PART_PaneRoot");
        var workspace = (Control)split.Content!;
        var session = window.Session;
        var monitor = window.MonitorView;
        var items = shell.Navigation.Items.Cast<ListBoxItem>().ToArray();
        Require(!shell.GetVisualDescendants().OfType<GridSplitter>().Any(),
            "main navigation has no draggable resize divider");
        Require(shell.PaneToggle is not ToggleButton && !shell.PaneToggle.Classes.Contains("accent") &&
            shell.PaneToggle.Background is ISolidColorBrush { Color: { A: 0 } },
            "the menu is a neutral action button without a blue checked or accent state");
        Directory.CreateDirectory("artifacts");

        void Layout(int width)
        {
            DesktopViewportSmokeChecks.Layout(window, width, 640);
            double paneWidth = width >= 1280 ? split.OpenPaneLength : split.CompactPaneLength;
            double expected = width - paneWidth - workspace.Margin.Left - workspace.Margin.Right;
            WaitFor(() => Math.Abs(workspace.Bounds.Width - expected) < 1);
        }
        Rect WorkspaceBounds() => new(workspace.TranslatePoint(default, shell)!.Value, workspace.Bounds.Size);
        void VerifyWorkspace(Rect before) => Require(WorkspaceBounds() == before,
            "opening or closing compact overlay navigation does not move or resize the workspace");
        void VerifyPane(bool open)
        {
            Point origin = shell.Navigation.TranslatePoint(default, shell)!.Value;
            var point = new Point(100, origin.Y + 22);
            double expectedWidth = open ? split.OpenPaneLength : split.CompactPaneLength;
            WaitFor(() => shell.IsPaneOpen == open && Math.Abs(pane.Bounds.Width - expectedWidth) < 1 &&
                NavigationHit(shell, point) == open);
        }

        foreach (string locale in new[] { "zh-CN", "zh-TW", "en" })
        {
            window.Localization.Select(locale);
            Layout(1440);
            Layout(960);
            Require(split.DisplayMode == SplitViewDisplayMode.CompactOverlay && !shell.IsPaneOpen &&
                shell.PaneToggle.IsEffectivelyVisible && shell.StatusButton.IsEffectivelyVisible,
                "a narrow viewport starts with the icon rail and an available menu action");
            Require(shell.PaneToggle.Focus(NavigationMethod.Tab), "the narrow menu is keyboard accessible");
            Rect compactBounds = WorkspaceBounds();
            foreach (var item in items)
            {
                var icon = item.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
                Point position = icon.TranslatePoint(default, shell)!.Value;
                Require(icon.IsEffectivelyVisible && position.X >= 0 && position.X + icon.Bounds.Width <= split.CompactPaneLength &&
                    item.Bounds.Height >= 44 && !item.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible),
                    "each compact entry has a visible icon and a full hit target without clipped captions");
                Require(ToolTip.GetTip(item)?.ToString() == AutomationProperties.GetName(item),
                    "compact entries expose the same localized tooltip and accessible name");
            }
            VerifyPane(false);
            Require(AutomationProperties.GetName(shell.PaneToggle) == window.Localization.Get("shell.expandNavigation"),
                "the compact menu names its expand action in the selected language");
            Capture(shell, "ui-shell-narrow-compact-" + locale + ".png");

            var flyout = (Flyout)shell.StatusButton.Flyout!;
            shell.StatusButton.Focus(NavigationMethod.Tab);
            flyout.ShowAt(shell.StatusButton);
            Dispatcher.UIThread.RunJobs();
            Require(flyout.IsOpen && ((Control)flyout.Content!).GetVisualDescendants().OfType<TextBlock>()
                .Any(text => text.Text == window.Localization.Get("shell.disclaimer")),
                "compact status access retains notices and simulator identification");
            Click(shell.PaneToggle);
            Layout(960);
            VerifyPane(true);
            VerifyWorkspace(compactBounds);
            Require(!flyout.IsOpen && !shell.StatusButton.IsEffectivelyVisible && shell.PaneToggle.IsKeyboardFocusWithin &&
                AutomationProperties.GetName(shell.PaneToggle) == window.Localization.Get("shell.collapseNavigation") &&
                items.All(item => item.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible)),
                "opening the overlay reparents status content, preserves focus and reveals localized captions");
            Capture(shell, "ui-shell-narrow-overlay-" + locale + ".png");
            Click(shell.PaneToggle);
            Layout(960);
            VerifyPane(false);
            VerifyWorkspace(compactBounds);
            Require(shell.PaneToggle.IsKeyboardFocusWithin,
                "clicking the menu again closes the overlay without losing menu focus");

            Click(shell.PaneToggle);
            Layout(960);
            VerifyPane(true);
            items[0].Focus(NavigationMethod.Tab);
            items[0].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Layout(960);
            VerifyPane(false);
            VerifyWorkspace(compactBounds);
            Require(window.FocusManager!.GetFocusedElement() is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } focused &&
                (ReferenceEquals(focused, shell.PaneToggle) || ReferenceEquals(focused, shell.Navigation) ||
                    focused.GetVisualAncestors().Contains(shell.Navigation)),
                "Escape closes the overlay and retains focus on a visible menu action or navigation icon");

            Click(shell.PaneToggle);
            Layout(960);
            VerifyPane(true);
            ClickAt(window, shell, new Point(split.OpenPaneLength + 48, 140));
            Layout(960);
            VerifyPane(false);
            VerifyWorkspace(compactBounds);

            for (int page = 1; page <= 5; page++)
            {
                Click(shell.PaneToggle);
                Layout(960);
                VerifyPane(true);
                shell.Navigation.SelectedIndex = page % 5;
                Layout(960);
                VerifyPane(false);
                VerifyWorkspace(compactBounds);
                Require(window.Page == page % 5, "choosing a page closes overlay navigation onto the selected page");
            }
            Require(items.SequenceEqual(shell.Navigation.Items.Cast<ListBoxItem>()) &&
                ReferenceEquals(session, window.Session) && ReferenceEquals(monitor, window.MonitorView),
                "responsive navigation preserves navigation items, monitor and acquisition session");
        }

        Layout(960);
        shell.Navigation.SelectedIndex = 0;
        foreach (var key in new[] { Key.Enter, Key.Space })
        {
            Click(shell.PaneToggle);
            Layout(960);
            VerifyPane(true);
            items[0].Focus(NavigationMethod.Tab);
            items[0].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
            items[0].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key });
            Layout(960);
            VerifyPane(false);
            Require(window.Page == 0, "activating the already selected page closes the overlay without changing pages");
        }
        Require(items[0].Focus(NavigationMethod.Tab), "compact items accept keyboard navigation");
        items[0].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
        Require(window.Page == 1 && shell.Navigation.SelectedIndex == 1 && !shell.IsPaneOpen,
            "arrow keys navigate between compact page icons without opening the overlay");
        window.SelectPage(0);
        Layout(960);
        Require(shell.PaneToggle.Focus(NavigationMethod.Tab), "the narrow menu can own focus before resizing");
        Click(shell.PaneToggle);
        Layout(960);
        VerifyPane(true);
        Layout(1280);
        Require(split.DisplayMode == SplitViewDisplayMode.Inline && shell.IsPaneOpen &&
            !shell.PaneToggle.IsEffectivelyVisible && !shell.StatusButton.IsEffectivelyVisible,
            "the wide breakpoint fixes the expanded pane and hides both compact-only actions");
        Require(window.FocusManager!.GetFocusedElement() is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } &&
            !shell.PaneToggle.IsKeyboardFocusWithin,
            "crossing to wide mode transfers focus away from the hidden menu button");
        items[0].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        shell.Navigation.SelectedIndex = 2;
        Layout(1280);
        Require(shell.IsPaneOpen && window.Page == 2,
            "Escape and page selection keep wide navigation fixed open");
        window.SelectPage(0);
        Layout(1440);
        WaitFor(() => Math.Abs(pane.Bounds.Width - split.OpenPaneLength) < 1);
        Capture(shell, "ui-shell-wide-en.png");
        window.Localization.Select("zh-CN");
        Layout(1440);
        Capture(shell, "ui-shell-wide-zh-CN.png");

        Layout(1279);
        Require(split.DisplayMode == SplitViewDisplayMode.CompactOverlay && !shell.IsPaneOpen && shell.PaneToggle.IsEffectivelyVisible,
            "crossing below the wide breakpoint resets to compact navigation");
        var status = (Flyout)shell.StatusButton.Flyout!;
        shell.StatusButton.Focus(NavigationMethod.Tab);
        status.ShowAt(shell.StatusButton);
        Dispatcher.UIThread.RunJobs();
        Layout(1280);
        Require(!status.IsOpen && !shell.StatusButton.IsEffectivelyVisible &&
            window.FocusManager!.GetFocusedElement() is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true },
            "resizing an open compact status flyout to wide restores its content and leaves focus visible");
        Layout(1279);
        status.ShowAt(shell.StatusButton);
        Dispatcher.UIThread.RunJobs();
        Require(status.IsOpen && ((Control)status.Content!).GetVisualDescendants().OfType<TextBlock>()
            .Any(text => text.Text == window.Localization.Get("shell.disclaimer")),
            "status content remains reusable after overlay and wide-mode transitions");
        status.Hide();
    }

    private static bool NavigationHit(DesktopShell shell, Point point)
    {
        var hit = shell.InputHitTest(point) as Control;
        return hit is not null && (ReferenceEquals(hit, shell.Navigation) || hit.GetVisualAncestors().Contains(shell.Navigation));
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void ClickAt(Window window, Control root, Point point)
    {
        var target = root.InputHitTest(point) as Control ?? throw new InvalidOperationException("Desktop shell: outside click has no input target");
        var pointer = new Pointer(284, PointerType.Mouse, true);
        Point position = root.TranslatePoint(point, window)!.Value;
        target.RaiseEvent(new PointerPressedEventArgs(target, pointer, window, position, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        target.RaiseEvent(new PointerReleasedEventArgs(target, pointer, window, position, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        pointer.Capture(null);
    }

    private static void WaitFor(Func<bool> completed)
    {
        if (completed()) { return; }
        // Fluent pane transitions and compositor hit testing advance on native frames.
        var elapsed = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        poll.Tick += (_, _) =>
        {
            if (completed() || elapsed.Elapsed > TimeSpan.FromSeconds(3)) { frame.Continue = false; }
        };
        poll.Start();
        try { Dispatcher.UIThread.PushFrame(frame); }
        finally { poll.Stop(); }
        Require(completed(), "the navigation pane finishes its native layout transition");
    }

    private static void Capture(Control root, string name)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        root.UpdateLayout();
        using var image = new RenderTargetBitmap(new PixelSize((int)root.Bounds.Width, (int)root.Bounds.Height), new Vector(96, 96));
        image.Render(root);
        image.Save(Path.Combine("artifacts", name), PngBitmapEncoderOptions.Default);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Desktop shell: " + message); }
    }
}
