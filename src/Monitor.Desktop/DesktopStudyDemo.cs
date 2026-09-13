// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Desktop;

// Explicit development entry only. Never called by ordinary startup.
internal static class DesktopStudyDemo
{
    public static void Start(MainWindow window)
    {
        window.ShowDemoNotice();
        RecordStudyPresenter? presenter = null;
        NativeDragInput? input = null;
        void Reset()
        {
            input?.Dispose();
            presenter?.ActiveDrag?.ReleaseWithoutRollback();
            presenter?.UnbindClearButton();
            presenter?.UnbindPointerQueries();
            presenter?.Withdraw();
            presenter = new(window, DesktopStudySmokeFixture.CreatePresentation(zoomNumerator: 4));
            presenter.BindClearButton(Context);
            presenter.BindPointerQueries(Context, 8);
            // Reset replaces synthetic test data only; never a production record.
            input = new NativeDragInput(presenter, Context, 8, gesture => gesture.Cancel(Context()));
            presenter.Refresh(Context());
        }
        window.SetDemoReset(Reset);
        Reset();
    }

    private static RecordStudyCommandContext Context() => new(false,
        DesktopStudySmokeFixture.Layout, new(100, 60, 400, 240),
        DesktopStudySmokeFixture.GridStyle, DesktopStudySmokeFixture.CursorStyle, false);
}
