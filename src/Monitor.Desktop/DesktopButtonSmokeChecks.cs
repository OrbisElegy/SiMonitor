// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Desktop;

internal static class DesktopButtonSmokeChecks
{
    public static void Verify(MainWindow window)
    {
        CapturedRecordSvgPresentation source = DesktopStudySmokeFixture.CreatePresentation(activeInstance: true);
        RecordStudyPresenter presenter = new(window, source);
        int resolutions = 0;
        bool overlay = true;
        presenter.BindClearButton(() =>
        {
            resolutions++;
            return Context(overlay);
        });
        if (window.ClearCursorButton.IsVisible) { throw new InvalidOperationException("Empty screen exposed clear."); }
        Refresh(presenter);
        if (!window.ClearCursorButton.IsVisible || !window.ClearCursorButton.IsEnabled || resolutions != 0)
        { throw new InvalidOperationException("Ready button or deferred context resolution failed."); }
        window.UpdateLayout();
        using (RenderTargetBitmap image = new(new PixelSize(1280, 720), new Vector(96, 96)))
        {
            image.Render(window);
            image.Save(Path.Combine("artifacts", "desktop-study-actions.png"), PngBitmapEncoderOptions.Default);
        }
        Click(window);
        if (resolutions != 1 || !window.ClearCursorButton.IsVisible || window.ClearCursorButton.IsEnabled || source.Current?.Display.CursorOverlaySvg is not null)
        { throw new InvalidOperationException("Native clear activation did not redraw without cursors."); }
        Click(window);
        if (resolutions != 1) { throw new InvalidOperationException("Disabled button invoked the provider."); }
        presenter.UnbindClearButton();
        if (window.ClearCursorButton.IsVisible || window.HasNoRecordContent)
        { throw new InvalidOperationException("Unbind did not preserve the displayed record."); }
        presenter.Withdraw();

        source = DesktopStudySmokeFixture.CreatePresentation(activeInstance: true);
        presenter = new(window, source);
        presenter.BindClearButton(() => Context(overlay));
        Refresh(presenter);
        overlay = false;
        Click(window);
        if (window.CurrentPublication?.Status != CapturedRecordSvgStatus.Denied ||
            source.Current is not null || window.ClearCursorButton.IsVisible)
        { throw new InvalidOperationException("Button reused old overlay capability."); }
        overlay = true;
        Refresh(presenter);
        presenter.BindClearButton(() => throw new InvalidOperationException("Synthetic provider failure"));
        Click(window);
        if (window.CurrentPublication?.ReasonCode != "DesktopStudy.CommandContextFailed" || source.Current is not null)
        { throw new InvalidOperationException("Provider failure retained input or escaped the native event."); }
        Refresh(presenter);
        presenter.BindClearButton(() =>
        {
            Refresh(presenter);
            return Context(true);
        });
        Click(window);
        if (window.HasNoRecordContent || source.Current?.Display.CursorOverlaySvg is null || !window.ClearCursorButton.IsEnabled)
        { throw new InvalidOperationException("Superseded click changed the newer picture."); }
        presenter.Withdraw();
        presenter.UnbindClearButton();

        source = DesktopStudySmokeFixture.CreatePresentation(hideMeasurement: true);
        presenter = new(window, source);
        presenter.BindClearButton(() => throw new InvalidOperationException("Disabled button invoked provider"));
        Refresh(presenter);
        if (window.ClearCursorButton.IsVisible || window.ClearCursorButton.IsEnabled)
        { throw new InvalidOperationException("Disabled measurement exposed clear."); }
        Click(window);
        if (window.HasNoRecordContent) { throw new InvalidOperationException("Disabled click changed the record."); }
        presenter.UnbindClearButton();
        presenter.Withdraw();
        source = DesktopStudySmokeFixture.CreatePresentation();
        presenter = new(window, source);
        presenter.BindClearButton(() => Context(true));
        Refresh(presenter);
        if (!window.ClearCursorButton.IsVisible || !window.ClearCursorButton.IsEnabled)
        { throw new InvalidOperationException("Enabled publication did not restore clear after disabled measurement."); }
        Click(window);
        if (source.Current?.Display.CursorOverlaySvg is not null || window.ClearCursorButton.IsEnabled)
        { throw new InvalidOperationException("Restored clear action did not execute."); }
        presenter.UnbindClearButton();
        presenter.Withdraw();
        window.ApplyPublication(new(CapturedRecordSvgStatus.NotRendered, "SvgPresentation.NotRendered", null));
        Console.WriteLine("ok: native clear button activation, current context, disabled state and failure containment");
    }

    private static void Click(MainWindow window) => window.ClearCursorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static RecordStudyCommandContext Context(bool overlay) => new(overlay,
        DesktopStudySmokeFixture.Layout, DesktopStudySmokeFixture.Screen,
        DesktopStudySmokeFixture.GridStyle, DesktopStudySmokeFixture.CursorStyle, false);

    private static void Refresh(RecordStudyPresenter presenter) => presenter.Refresh(true,
        DesktopStudySmokeFixture.Layout, DesktopStudySmokeFixture.Screen,
        DesktopStudySmokeFixture.GridStyle, DesktopStudySmokeFixture.CursorStyle, false);
}
