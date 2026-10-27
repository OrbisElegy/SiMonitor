// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Threading;

namespace Monitor.Desktop;

internal static class SettingsNavigation
{
    // Resizing must not leave keyboard focus inside the navigation being hidden.
    internal static void Adapt(ListBox list, ComboBox selector, bool compact)
    {
        if (list.IsVisible == !compact && selector.IsVisible == compact) { return; }
        bool restoreFocus = compact ? list.IsKeyboardFocusWithin : selector.IsKeyboardFocusWithin;
        list.IsVisible = !compact;
        selector.IsVisible = compact;
        if (!restoreFocus) { return; }
        Dispatcher.UIThread.Post(() =>
        {
            Control target = compact ? selector : list.ContainerFromIndex(list.SelectedIndex) ?? list;
            if (target.IsEffectivelyVisible)
            {
                target.Focus();
                target.BringIntoView();
            }
        }, DispatcherPriority.Loaded);
    }
}
