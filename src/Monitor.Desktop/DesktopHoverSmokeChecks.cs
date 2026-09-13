// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Desktop;

internal static class DesktopHoverSmokeChecks
{
    public static void Verify(MainWindow window)
    {
        CapturedRecordSvgPresentation source = DesktopStudySmokeFixture.CreatePresentation(activeInstance: true);
        RecordStudyPresenter presenter = new(window, source);
        bool overlay = true;
        int queries = 0;
        presenter.BindPointerQueries(() => { queries++; return Context(overlay); }, 4);
        Refresh(presenter);
        RecordStudyControl control = window.RecordControl!;
        control.Margin = new Thickness(7.25, 11.5, 0, 0);
        window.UpdateLayout();
        CapturedRecordSvgPublication before = source.Publication;
        Move(window, control, new(50, 50));
        RequireHover(control, RecordCursorHits.First, "卡尺起点");
        Move(window, control, new(150, 70));
        RequireHover(control, RecordCursorHits.Second, "卡尺终点");
        presenter.BindPointerQueries(() => Context(overlay), 60);
        Move(window, control, new(100, 60));
        RequireHover(control, RecordCursorHits.First | RecordCursorHits.Second, "两条卡尺同时命中");
        presenter.BindPointerQueries(() => { queries++; return Context(overlay); }, 4);
        if (!ReferenceEquals(before, source.Publication)) { throw new InvalidOperationException("Hover mutated publication."); }
        Move(window, control, new(-10, -10));
        RequireHover(control, RecordCursorHits.None, null);
        if (window.HasNoRecordContent) { throw new InvalidOperationException("Out-of-plot hover withdrew record."); }
        Move(window, control, new(50, 50));
        control.RaiseEvent(Event(window, control, new(50, 50), InputElement.PointerExitedEvent));
        RequireHover(control, RecordCursorHits.None, null);
        presenter.UnbindPointerQueries();
        int count = queries;
        Move(window, control, new(50, 50));
        if (queries != count || control.IsHitTestVisible) { throw new InvalidOperationException("Unbound hover remained active."); }

        presenter.BindPointerQueries(() => { queries++; return Context(overlay); }, 4);
        Move(window, control, new(50, 50));
        Refresh(presenter);
        RequireHover(control, RecordCursorHits.None, null);
        count = queries;
        // A retained detached control may still receive an already queued event.
        control.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, control,
            new Pointer(1, PointerType.Mouse, true), control, new(50, 50), 0, new PointerPointProperties(), KeyModifiers.None));
        if (queries != count) { throw new InvalidOperationException("Detached control queried old input."); }
        control = window.RecordControl!;
        window.UpdateLayout();
        overlay = false;
        Move(window, control, new(50, 50));
        if (source.Current is not null || window.CurrentPublication?.Status != CapturedRecordSvgStatus.Denied)
        { throw new InvalidOperationException("Hover reused old safety capability."); }
        RequireHover(control, RecordCursorHits.None, null);
        overlay = true;
        Refresh(presenter);
        control = window.RecordControl!;
        presenter.BindPointerQueries(() => { Refresh(presenter); return Context(true); }, 4);
        window.UpdateLayout();
        Move(window, control, new(50, 50));
        RequireHover(control, RecordCursorHits.None, null);
        if (window.HasNoRecordContent || ReferenceEquals(control, window.RecordControl))
        { throw new InvalidOperationException("Superseded hover did not preserve newer content."); }
        presenter.BindPointerQueries(() => throw new InvalidOperationException("Synthetic hover provider failure"), 4);
        window.UpdateLayout();
        Move(window, window.RecordControl!, new(50, 50));
        if (source.Current is not null || !window.HasNoRecordContent)
        { throw new InvalidOperationException("Hover provider failure retained input."); }
        presenter.UnbindPointerQueries();
        presenter.Withdraw();
        source = DesktopStudySmokeFixture.CreatePresentation(hideMeasurement: true);
        presenter = new(window, source);
        presenter.BindPointerQueries(() => throw new InvalidOperationException("Disabled hover queried context"), 4);
        Refresh(presenter);
        window.UpdateLayout();
        control = window.RecordControl!;
        Move(window, control, new(50, 50));
        if (control.IsHitTestVisible || window.HasNoRecordContent)
        { throw new InvalidOperationException("Disabled measurement enabled hover query."); }
        RequireHover(control, RecordCursorHits.None, null);
        presenter.UnbindPointerQueries();
        presenter.Withdraw();
        window.ApplyPublication(new(CapturedRecordSvgStatus.NotRendered, "SvgPresentation.NotRendered", null));
        Console.WriteLine("ok: native pointer hover, translation, exit, detach and current safety gates");
    }

    private static PointerEventArgs Event(MainWindow window, RecordStudyControl control, Point local,
        Avalonia.Interactivity.RoutedEvent routedEvent) => new(routedEvent, control,
            new Pointer(1, PointerType.Mouse, true), window, control.TranslatePoint(local, window)!.Value,
            0, new PointerPointProperties(), KeyModifiers.None);

    private static void Move(MainWindow window, RecordStudyControl control, Point local) =>
        control.RaiseEvent(Event(window, control, local, InputElement.PointerMovedEvent));

    private static void RequireHover(RecordStudyControl control, RecordCursorHits expected, string? tip)
    {
        if (control.HoveredCursors != expected || !Equals(ToolTip.GetTip(control), tip))
        { throw new InvalidOperationException("Native hover result or tooltip did not match current input."); }
    }

    private static RecordStudyCommandContext Context(bool overlay) => new(overlay,
        DesktopStudySmokeFixture.Layout, DesktopStudySmokeFixture.Screen,
        DesktopStudySmokeFixture.GridStyle, DesktopStudySmokeFixture.CursorStyle, false);

    private static void Refresh(RecordStudyPresenter presenter) => presenter.Refresh(true,
        DesktopStudySmokeFixture.Layout, DesktopStudySmokeFixture.Screen,
        DesktopStudySmokeFixture.GridStyle, DesktopStudySmokeFixture.CursorStyle, false);
}
