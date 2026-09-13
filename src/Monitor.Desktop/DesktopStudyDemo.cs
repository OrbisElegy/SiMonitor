// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Desktop;

// Explicit development entry only. Never called by ordinary startup.
internal static class DesktopStudyDemo
{
    public static void Start(MainWindow window)
    {
        window.ShowDemoNotice();
        RecordStudyPresenter? presenter = null;
        NativeDragInput? input = null;
        CapturedRecordMeasurement? measurement = null;
        SystemViewCommandAssessmentPolicy policy = SystemViewCommandAssessmentPolicy.Enabled;
        bool auxiliary = false;
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 4, 1), SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        RecordStudyCommandContext Context() => new(false,
            DesktopStudySmokeFixture.Layout, new(100, 60, 400, 240),
            theme.Theme == Ecg12Theme.PaperGridBlack ? DesktopStudySmokeFixture.GridStyle : new("#16402a", "#296044", 500, 1000),
            DesktopStudySmokeFixture.CursorStyle, auxiliary);
        void BindInput()
        {
            input = new NativeDragInput(presenter!, Context, 8, gesture => gesture.Cancel(Context()));
            presenter!.Refresh(Context());
            window.DemoViews.Update(zoom.Selection.Numerator, theme.Theme);
            window.DemoMeasurement.Update(policy, auxiliary);
        }
        void ChangeView(Action select)
        {
            // Resolve preview under its original geometry before changing scale.
            input?.Dispose();
            if (presenter!.ActiveDrag is not null)
            {
                presenter.Withdraw();
                return;
            }
            try { select(); BindInput(); }
            catch (Exception exception) { presenter.WithdrawCommandFailure(exception); }
        }
        void Reset()
        {
            input?.Dispose();
            presenter?.ActiveDrag?.ReleaseWithoutRollback();
            presenter?.UnbindClearButton();
            presenter?.UnbindPointerQueries();
            presenter?.Withdraw();
            zoom.Select(new(Ecg12ZoomMode.ExplicitScale, 4, 1));
            theme.Select(Ecg12Theme.PaperGridBlack);
            policy = SystemViewCommandAssessmentPolicy.Enabled;
            auxiliary = false;
            presenter = new(window, DesktopStudySmokeFixture.CreatePresentation(theme: theme, zoom: zoom,
                captureMeasurement: value => measurement = value));
            presenter.BindClearButton(Context);
            presenter.BindPointerQueries(Context, 8);
            BindInput();
        }
        window.DemoViews.Bind(scale => ChangeView(() => zoom.Select(new(Ecg12ZoomMode.ExplicitScale, scale, 1))),
            selected => ChangeView(() => theme.Select(selected)));
        window.DemoMeasurement.Bind(selected => ChangeView(() => { measurement!.UpdatePolicy(selected); policy = selected; }),
            allowed => ChangeView(() => auxiliary = allowed));
        window.SetDemoReset(Reset);
        Reset();
    }
}
