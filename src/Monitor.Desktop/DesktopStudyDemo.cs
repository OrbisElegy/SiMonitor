// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Desktop;

// Explicit development entry only. Never called by ordinary startup.
internal static class DesktopStudyDemo
{
    public static void Start(MainWindow window)
    {
        window.ShowDemoNotice();
        RecordStudyPresenter presenter = new(window, DesktopStudySmokeFixture.CreatePresentation(zoomNumerator: 4));
        presenter.BindClearButton(Context);
        presenter.BindPointerQueries(Context, 8);
        // The demo explicitly chooses current-policy rollback on interruption.
        // NativeDragInput owns its subscriptions until the window closes.
        _ = new NativeDragInput(presenter, Context, 8, gesture => gesture.Cancel(Context()));
        presenter.Refresh(Context());
    }

    private static RecordStudyCommandContext Context() => new(false,
        DesktopStudySmokeFixture.Layout, new(100, 60, 400, 240),
        DesktopStudySmokeFixture.GridStyle, DesktopStudySmokeFixture.CursorStyle, false);
}
