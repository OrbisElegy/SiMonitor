// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Monitor.Desktop;

// Owns navigation and layout only; the window retains session and page lifetimes.
internal sealed class DesktopShell : Grid
{
    private const double ExpandedNavigationWidth = 1280;
    private static readonly string[] PageKeys = ["shell.monitor", "shell.ecg", "navigation.settings", "navigation.help", "navigation.about"];
    // Geometry avoids depending on platform-specific symbol fonts for compact navigation.
    private static readonly string[] PageIcons =
    [
        "M3,3 H21 V18 H3 Z M8,22 H16 M12,18 V22 M5,10 H8 L10,6 L13,15 L15,10 H19",
        "M5,2 H15 L20,7 V22 H5 Z M15,2 V7 H20 M7,14 H9 L11,10 L13,18 L15,14 H18",
        "M10,2 H14 L15,5 L17,6 L20,5 L22,9 L19.5,11 V13 L22,15 L20,19 L17,18 L15,19 L14,22 H10 L9,19 L7,18 L4,19 L2,15 L4.5,13 V11 L2,9 L4,5 L7,6 L9,5 Z M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12",
        "M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 M9,8 A3,3 0 1 1 13,11 L12,12 V14 M12,17 V18",
        "M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 M12,10 V18 M12,6 V7"
    ];
    private readonly SplitView _split = new()
    {
        DisplayMode = SplitViewDisplayMode.CompactOverlay,
        OpenPaneLength = 208,
        CompactPaneLength = 64,
        IsPaneOpen = false,
        PaneBackground = DesktopFluentStyle.Canvas
    };
    private readonly TextBlock[] _captions = new TextBlock[PageKeys.Length];
    private bool _wide;
    internal ListBox Navigation { get; } = new();
    internal Button PaneToggle { get; } = new()
    {
        Width = 44,
        Height = 44,
        Padding = new Thickness(12),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        HorizontalAlignment = HorizontalAlignment.Left
    };
    internal Button StatusButton { get; } = new()
    {
        Width = 44,
        Height = 44,
        Padding = new Thickness(12),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(10, 0, 0, 16)
    };
    internal bool IsPaneOpen => _split.IsPaneOpen;

    internal DesktopShell(Control workspace, DesktopLocalization localization, Action<int> selectPage,
        params Control[] notices)
    {
        Background = DesktopFluentStyle.Canvas;
        var sidebar = new Grid { RowDefinitions = new("Auto,*,Auto") };
        var heading = new Grid { ColumnDefinitions = new("44,*"), MinHeight = 44, Margin = new Thickness(10, 16, 8, 20) };
        PaneToggle.Content = Icon(PaneToggle, "M3,5 H21 M3,12 H21 M3,19 H21");
        heading.Children.Add(PaneToggle);
        var brand = new StackPanel { Spacing = 4, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new TextBlock
        {
            Text = ProductIdentity.Name,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        brand.Children.Add(new TextBlock
        {
            Text = ProductIdentity.Version + " · Standalone",
            FontSize = 11,
            Foreground = DesktopFluentStyle.SecondaryText,
            TextWrapping = TextWrapping.Wrap
        });
        SetColumn(brand, 1);
        heading.Children.Add(brand);
        sidebar.Children.Add(heading);

        SettingsSections.StyleNavigation(Navigation);
        Navigation.Padding = new Thickness(0);
        Navigation.Margin = new Thickness(4, 0);
        Navigation.ItemsSource = PageKeys.Select((key, index) => CreateItem(key, index, localization)).ToArray();
        localization.Bind(Navigation, AutomationProperties.NameProperty, "shell.navigation");
        Navigation.SelectionChanged += (_, args) =>
        {
            if (ReferenceEquals(args.Source, Navigation) && Navigation.SelectedIndex >= 0)
            {
                selectPage(Navigation.SelectedIndex);
                CloseOverlay();
            }
        };
        Navigation.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key is Key.Enter or Key.Space) { CloseOverlay(); }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        SetRow(Navigation, 1);
        sidebar.Children.Add(Navigation);

        var footer = new StackPanel { Spacing = 12, Margin = new Thickness(16, 12) };
        foreach (var notice in notices) { footer.Children.Add(notice); }
        var disclaimer = new TextBlock
        {
            FontSize = 12,
            Foreground = DesktopFluentStyle.SecondaryText,
            TextWrapping = TextWrapping.Wrap
        };
        localization.Bind(disclaimer, TextBlock.TextProperty, "shell.disclaimer");
        footer.Children.Add(disclaimer);
        var footerScroll = SettingsScroll.Create(footer);
        footerScroll.MaxHeight = 220;
        var footerHost = new ContentControl { Content = footerScroll, Margin = new Thickness(8, 0, 8, 12) };
        SetRow(footerHost, 2);
        sidebar.Children.Add(footerHost);
        var statusFlyout = new Flyout { Placement = PlacementMode.RightEdgeAlignedBottom };
        StatusButton.Content = Icon(StatusButton, "M12,2 A7,7 0 0 0 5,9 V15 L3,18 H21 L19,15 V9 A7,7 0 0 0 12,2 M9,21 H15");
        StatusButton.Flyout = statusFlyout;
        localization.Bind(StatusButton, AutomationProperties.NameProperty, "shell.status");
        localization.Bind(StatusButton, ToolTip.TipProperty, "shell.status");
        SetRow(StatusButton, 2);
        sidebar.Children.Add(StatusButton);

        void UpdatePane()
        {
            bool open = _split.IsPaneOpen;
            bool restoreFocus = _wide && PaneToggle.IsKeyboardFocusWithin ||
                (open ? StatusButton.IsKeyboardFocusWithin || statusFlyout.IsOpen : footerHost.IsKeyboardFocusWithin);
            statusFlyout.Hide();
            footerHost.Content = null;
            statusFlyout.Content = null;
            if (open)
            {
                footerScroll.Width = double.NaN;
                footerHost.Content = footerScroll;
            }
            else
            {
                footerScroll.Width = 260;
                statusFlyout.Content = footerScroll;
            }
            PaneToggle.IsVisible = !_wide;
            heading.ColumnDefinitions[0].Width = new GridLength(_wide ? 0 : 44);
            brand.IsVisible = open;
            footerHost.IsVisible = open;
            StatusButton.IsVisible = !open;
            foreach (var caption in _captions) { caption.IsVisible = open; }
            string toggleKey = open ? "shell.collapseNavigation" : "shell.expandNavigation";
            localization.Bind(PaneToggle, AutomationProperties.NameProperty, toggleKey);
            localization.Bind(PaneToggle, ToolTip.TipProperty, toggleKey);
            KeyboardNavigation.SetTabNavigation(sidebar, !_wide && open ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.Continue);
            if (restoreFocus)
            {
                if (_wide) { (Navigation.SelectedItem as Control ?? Navigation).Focus(); }
                else { PaneToggle.Focus(); }
            }
        }
        PaneToggle.Click += (_, _) =>
        {
            if (!_wide) { _split.IsPaneOpen = !_split.IsPaneOpen; }
        };
        _split.PropertyChanged += (_, change) =>
        {
            if (change.Property == SplitView.IsPaneOpenProperty) { UpdatePane(); }
        };
        SizeChanged += (_, _) =>
        {
            bool wide = Bounds.Width >= ExpandedNavigationWidth;
            if (_wide == wide) { return; }
            _wide = wide;
            _split.DisplayMode = wide ? SplitViewDisplayMode.Inline : SplitViewDisplayMode.CompactOverlay;
            // Crossing the breakpoint restores the default for the new viewport.
            if (_split.IsPaneOpen != wide) { _split.IsPaneOpen = wide; }
            else { UpdatePane(); }
        };
        UpdatePane();
        _split.Pane = sidebar;
        var card = DesktopFluentStyle.SurfaceCard(workspace);
        card.Margin = new Thickness(20, 20, 24, 20);
        card.ClipToBounds = true;
        _split.Content = card;
        Children.Add(_split);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Continue);
    }

    private ListBoxItem CreateItem(string key, int index, DesktopLocalization localization)
    {
        var item = new ListBoxItem
        {
            MinHeight = 44,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var row = new Grid { ColumnDefinitions = new("3,44,*"), MinHeight = 44 };
        var indicator = new Border
        {
            Width = 3,
            Height = 18,
            CornerRadius = new CornerRadius(2),
            Background = DesktopFluentStyle.Accent,
            IsVisible = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(indicator);
        var icon = Icon(item, PageIcons[index]);
        SetColumn(icon, 1);
        row.Children.Add(icon);
        var caption = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 8, 8, 8) };
        _captions[index] = caption;
        localization.Bind(caption, TextBlock.TextProperty, key);
        SetColumn(caption, 2);
        row.Children.Add(caption);
        item.Content = row;
        item.AddHandler(TappedEvent, (_, _) => CloseOverlay(), handledEventsToo: true);
        localization.Bind(item, AutomationProperties.NameProperty, key);
        localization.Bind(item, ToolTip.TipProperty, key);
        item.PropertyChanged += (_, change) =>
        {
            if (change.Property != ListBoxItem.IsSelectedProperty) { return; }
            indicator.IsVisible = item.IsSelected;
            caption.FontWeight = item.IsSelected ? FontWeight.SemiBold : FontWeight.Normal;
        };
        return item;
    }

    private void CloseOverlay()
    {
        if (!_wide) { _split.IsPaneOpen = false; }
    }

    private static Avalonia.Controls.Shapes.Path Icon(TemplatedControl owner, string data)
    {
        var icon = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Width = 20,
            Height = 20,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.6,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        icon.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, new Binding(nameof(TemplatedControl.Foreground)) { Source = owner });
        return icon;
    }

    internal void SelectPage(int page) => Navigation.SelectedIndex = page;
}
