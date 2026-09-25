// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace Monitor.Desktop;

public sealed class MonitorApp : Avalonia.Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.Args is ["--ui-preview"])
            {
                desktop.MainWindow = new DesignPreviewWindow();
                base.OnFrameworkInitializationCompleted();
                return;
            }
            if (desktop.Args is ["--waveform-demo" or "--physiology-demo" or "--electrode-demo"])
            {
                desktop.MainWindow = new WaveformDemoWindow(desktop.Args[0] == "--physiology-demo", desktop.Args[0] == "--electrode-demo");
                base.OnFrameworkInitializationCompleted();
                return;
            }
            MainWindow window = new();
            desktop.MainWindow = window;
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
                    if (valid) { NativeSmokePartition.Run(() => DesignPreviewSmokeChecks.Verify()); }
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
