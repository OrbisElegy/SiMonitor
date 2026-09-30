// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Monitor.Desktop;

internal static class SettingsScroll
{
    internal static ScrollViewer Create(Control content)
    {
        var viewer = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            BringIntoViewOnFocusChange = false
        };
        // Window activation restores focus programmatically. Only explicit keyboard
        // navigation should scroll to the focused control; pointer scrolling stays put.
        viewer.AddHandler(InputElement.GotFocusEvent, (_, args) =>
        {
            if (args.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional && args.Source is Control target)
            { target.BringIntoView(); }
        });
        return viewer;
    }
}
