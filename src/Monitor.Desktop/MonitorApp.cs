// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Monitor.Desktop;

public sealed class MonitorApp : Avalonia.Application
{
    public override void Initialize() => DesktopFluentStyle.Install(this);

    // Default product entry and the retained preview alias share one runtime.
    internal static Window CreateLaunchWindow(string[]? arguments) => arguments switch
    {
        null or [] or ["--ui-preview"] => new DesignPreviewWindow(),
        ["--waveform-demo"] => new WaveformDemoWindow(),
        ["--physiology-demo"] => new WaveformDemoWindow(physiology: true),
        ["--electrode-demo"] => new WaveformDemoWindow(projected: true),
        ["--study-demo"] or ["--smoke-test"] => new MainWindow(),
        _ => throw new ArgumentException("Desktop.UnsupportedLaunch", nameof(arguments))
    };

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateLaunchWindow(desktop.Args);
            if (desktop.MainWindow is not MainWindow window)
            {
                base.OnFrameworkInitializationCompleted();
                return;
            }
            if (desktop.Args is ["--study-demo"]) { DesktopStudyDemo.Start(window); }
            if (desktop.Args is ["--smoke-test"])
            {
                window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    window.UpdateLayout();
                    bool valid = window.IsVisible && window.HasUnloadedRecordState;
                    if (valid) { NativeSmokePartition.Run(() => DesktopSmokeChecks.VerifyPublicationStates(window)); }
                    if (valid) { NativeSmokePartition.Run(() => DesktopPresenterSmokeChecks.Verify(window)); }
                    if (valid) { NativeSmokePartition.Run(() => DesktopPresenterSmokeChecks.VerifyClear(window)); }
                    if (valid) { NativeSmokePartition.Run(() => DesktopButtonSmokeChecks.Verify(window)); }
                    if (valid) { NativeSmokePartition.Run(() => DesktopPointerSmokeChecks.Verify(window)); }
                    if (valid) { NativeSmokePartition.Run(() => DesktopHoverSmokeChecks.Verify(window)); }
                    if (valid) { NativeSmokePartition.Run(() => DesktopDragSmokeChecks.Verify(window)); }
                    if (valid) { NativeSmokePartition.Run(() => DesktopCaptureSmokeChecks.Verify(window)); }
                    if (valid) { NativeSmokePartition.Run(() => DesktopDemoSmokeChecks.Verify()); }
                    if (valid) { NativeSmokePartition.Run(() => DesktopMeasurementSmokeChecks.Verify()); }
                    if (valid) { DesignPreviewSmokeChecks.Verify(); }
                    if (valid) { WaveformDemoSmokeChecks.Verify(); }
                    if (valid) { NativeSmokePartition.Run(() => ProjectedEcgDemoSmokeChecks.Verify()); }
                    if (valid && NativeSmokePartition.Index == 0)
                    {
                        Directory.CreateDirectory("artifacts");
                        using RenderTargetBitmap image = new(new PixelSize(1280, 720), new Vector(96, 96));
                        image.Render(window);
                        image.Save(Path.Combine("artifacts", "desktop-startup.png"), PngBitmapEncoderOptions.Default);
                    }
                    Console.WriteLine(valid ? "ok: desktop unloaded-state window opened" : "failed: desktop unloaded state");
                    NativeSmokePartition.Complete();
                    desktop.Shutdown(valid ? 0 : 1);
                }, DispatcherPriority.Background);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
