// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Input;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Desktop;

internal static class DesktopCaptureSmokeChecks
{
    public static void Verify(MainWindow window)
    {
        CapturedRecordSvgPresentation source = DesktopStudySmokeFixture.CreatePresentation(activeInstance: true);
        RecordStudyPresenter presenter = new(window, source);
        RecordStudyCommandContext context = new(true, DesktopStudySmokeFixture.Layout, DesktopStudySmokeFixture.Screen,
            DesktopStudySmokeFixture.GridStyle, DesktopStudySmokeFixture.CursorStyle, false);
        int interruptions = 0;
        bool providerFails = false;
        using NativeDragInput route = new(presenter,
            () => providerFails ? throw new InvalidOperationException("Synthetic drag context failure") : context,
            4, gesture => { interruptions++; gesture.Cancel(context); });
        presenter.Refresh(context);
        Pointer pointer = new(91, PointerType.Mouse, true);
        RecordStudyControl original = window.RecordControl!;
        Press(window, pointer, new(52, 50));
        if (!ReferenceEquals(pointer.Captured, window) || presenter.ActiveDrag is null)
        { throw new InvalidOperationException("Native press did not capture the window and start a gesture."); }
        Move(window, pointer, new(62, 50));
        if (ReferenceEquals(original, window.RecordControl) || !ReferenceEquals(pointer.Captured, window))
        { throw new InvalidOperationException("Preview replacement lost stable pointer capture."); }
        RequireX(source, 30);
        Move(window, new Pointer(92, PointerType.Mouse, false), new(82, 50));
        RequireX(source, 30);
        Release(window, pointer, new(72, 50));
        RequireX(source, 35);
        if (pointer.Captured is not null || presenter.ActiveDrag is not null || interruptions != 0)
        { throw new InvalidOperationException("Release did not commit and clear capture."); }

        Press(window, pointer, new(70, 50));
        Move(window, pointer, new(80, 50));
        pointer.Capture(null);
        RequireX(source, 35);
        if (presenter.ActiveDrag is not null || interruptions != 1)
        { throw new InvalidOperationException("Capture loss did not invoke explicit rollback policy."); }
        Press(window, pointer, new(70, 50));
        Move(window, pointer, new(80, 50));
        presenter.Withdraw();
        if (pointer.Captured is not null || presenter.ActiveDrag is not null || source.Current is not null || interruptions != 2)
        { throw new InvalidOperationException("External withdrawal retained capture or reopened the study."); }
        presenter.Refresh(context);
        RequireX(source, 35);
        Press(window, pointer, new(70, 50));
        providerFails = true;
        Move(window, pointer, new(80, 50));
        if (pointer.Captured is not null || source.Current is not null || presenter.ActiveDrag is not null || interruptions != 3)
        { throw new InvalidOperationException("Context failure escaped capture cleanup."); }
        providerFails = false;
        presenter.Refresh(context);
        RequireX(source, 35);
        Press(window, pointer, new(70, 50));
        route.Dispose();
        if (pointer.Captured is not null || presenter.ActiveDrag is not null || window.RecordControl!.IsHitTestVisible || interruptions != 4)
        { throw new InvalidOperationException("Route disposal did not remove capture and handlers."); }
        int missingReleases = 0;
        int moveContexts = 0;
        using (NativeDragInput recovery = new(presenter, () => { moveContexts++; return context; },
            4, gesture => { missingReleases++; gesture.Cancel(context); }))
        {
            Press(window, pointer, new(70, 50));
            Move(window, pointer, new(80, 50));
            RequireX(source, 40);
            int resolvedBeforeLoss = moveContexts;
            MoveWithoutButton(window, new Pointer(93, PointerType.Mouse, false), new(90, 50));
            if (!ReferenceEquals(pointer.Captured, window) || missingReleases != 0 || moveContexts != resolvedBeforeLoss)
            { throw new InvalidOperationException("Foreign button state interrupted the captured mouse."); }
            MoveWithoutButton(window, pointer, new(90, 50));
            RequireX(source, 35);
            if (pointer.Captured is not null || presenter.ActiveDrag is not null || missingReleases != 1 || moveContexts != resolvedBeforeLoss)
            { throw new InvalidOperationException("Missing release previewed data or retained capture."); }
            CapturedRecordSvgPublication recovered = source.Publication;
            MoveWithoutButton(window, pointer, new(100, 50));
            Release(window, pointer, new(100, 50));
            if (!ReferenceEquals(recovered, source.Publication) || missingReleases != 1)
            { throw new InvalidOperationException("Late release changed the recovered measurement."); }
            Press(window, pointer, new(70, 50));
            Release(window, pointer, new(70, 50));
            if (presenter.ActiveDrag is not null || pointer.Captured is not null)
            { throw new InvalidOperationException("Missing-release recovery blocked the next gesture."); }
        }
        Action deactivate = () => NotifyActivation(window, false);
        int deactivations = 0;
        using (NativeDragInput activationRoute = new(presenter, () => context, 4,
            gesture => { deactivations++; gesture.Cancel(context); }))
        {
            Press(window, pointer, new(70, 50));
            Move(window, pointer, new(80, 50));
            deactivate();
            RequireX(source, 35);
            if (pointer.Captured is not null || presenter.ActiveDrag is not null || deactivations != 1)
            { throw new InvalidOperationException("Window deactivation retained drag capture."); }
            CapturedRecordSvgPublication restored = source.Publication;
            deactivate();
            Release(window, pointer, new(100, 50));
            if (deactivations != 1 || !ReferenceEquals(restored, source.Publication))
            { throw new InvalidOperationException("Repeated deactivation or late release changed recovered data."); }
            NotifyActivation(window, true);
            Press(window, pointer, new(70, 50));
            Release(window, pointer, new(70, 50));
        }
        using (NativeDragInput duringContext = new(presenter, () => { deactivate(); return context; },
            4, gesture => gesture.Cancel(context)))
        {
            Press(window, pointer, new(70, 50));
            if (pointer.Captured is not null || presenter.ActiveDrag is not null)
            { throw new InvalidOperationException("Deactivation during context resolution started capture afterwards."); }
        }
        NotifyActivation(window, true);
        NativeDragInput? disposedDuringContext = null;
        disposedDuringContext = new(presenter, () => { disposedDuringContext!.Dispose(); return context; },
            4, gesture => gesture.Cancel(context));
        using (disposedDuringContext)
        {
            Press(window, pointer, new(70, 50));
            if (pointer.Captured is not null || presenter.ActiveDrag is not null || window.DragInput is not null)
            { throw new InvalidOperationException("Disposed context callback resurrected native capture."); }
        }
        using (NativeDragInput failingPolicy = new(presenter, () => context, 4,
            _ => throw new InvalidOperationException("Synthetic interruption policy failure")))
        {
            Press(window, pointer, new(70, 50));
            pointer.Capture(null);
            if (pointer.Captured is not null || source.Current is not null || presenter.ActiveDrag is null)
            { throw new InvalidOperationException("Failed interruption silently discarded the pending gesture."); }
            presenter.ActiveDrag.ReleaseWithoutRollback();
        }
        presenter.Withdraw();
        window.ApplyPublication(new(CapturedRecordSvgStatus.NotRendered, "SvgPresentation.NotRendered", null));
        MainWindow closing = new();
        closing.Show();
        CapturedRecordSvgPresentation closingSource = DesktopStudySmokeFixture.CreatePresentation();
        RecordStudyPresenter closingPresenter = new(closing, closingSource);
        using NativeDragInput closingRoute = new(closingPresenter, () => context, 4, gesture => gesture.Cancel(context));
        closingPresenter.Refresh(context);
        Press(closing, pointer, new(50, 50));
        closing.Close();
        if (pointer.Captured is not null || closingPresenter.ActiveDrag is not null || closingSource.Current is not null || closing.DragInput is not null)
        { throw new InvalidOperationException("Window close retained drag capture or native input."); }
        Console.WriteLine("ok: native press/move/release capture, redraw continuity, interruption and cleanup");
    }

    // Smoke-only invocation of the locked framework's notification handler.
    // No OS focus event is synthesized and production code uses public events.
    private static void NotifyActivation(MainWindow window, bool activated)
    {
        System.Reflection.MethodInfo handler = typeof(Avalonia.Controls.WindowBase).GetMethod(
            activated ? "HandleActivated" : "HandleDeactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Locked framework activation handler is unavailable.");
        handler.Invoke(window, null);
    }

    private static Point Position(MainWindow window, Point local)
    {
        window.UpdateLayout();
        return window.RecordControl!.TranslatePoint(local, window)!.Value;
    }

    private static void Press(MainWindow window, Pointer pointer, Point local)
    {
        Point point = Position(window, local);
        RecordStudyControl control = window.RecordControl!;
        control.RaiseEvent(new PointerPressedEventArgs(control, pointer, window, point, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
    }

    private static void Move(MainWindow window, Pointer pointer, Point local) =>
        window.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, window, pointer, window,
            Position(window, local), 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));

    private static void Release(MainWindow window, Pointer pointer, Point local) =>
        window.RaiseEvent(new PointerReleasedEventArgs(window, pointer, window, Position(window, local), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));

    private static void MoveWithoutButton(MainWindow window, Pointer pointer, Point local) =>
        window.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, window, pointer, window,
            Position(window, local), 0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None));

    private static void RequireX(CapturedRecordSvgPresentation source, int x)
    {
        if (source.Current?.Display.Content.Content.Display.Content.Content.Study.Measurement?.First?.X.WholePixels != x)
        { throw new InvalidOperationException("Captured native pointer produced an unexpected measurement."); }
    }
}
