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
            MainWindow window = new();
            desktop.MainWindow = window;
            if (desktop.Args is ["--smoke-test"])
            {
                window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    window.UpdateLayout();
                    bool valid = window.IsVisible && window.HasUnloadedRecordState;
                    if (valid) { DesktopSmokeChecks.VerifyPublicationStates(window); }
                    if (valid) { DesktopPresenterSmokeChecks.Verify(window); }
                    if (valid) { DesktopPresenterSmokeChecks.VerifyClear(window); }
                    if (valid) { DesktopButtonSmokeChecks.Verify(window); }
                    if (valid) { DesktopPointerSmokeChecks.Verify(window); }
                    if (valid)
                    {
                        Directory.CreateDirectory("artifacts");
                        using RenderTargetBitmap image = new(new PixelSize(1280, 720), new Vector(96, 96));
                        image.Render(window);
                        image.Save(Path.Combine("artifacts", "desktop-startup.png"), PngBitmapEncoderOptions.Default);
                    }
                    Console.WriteLine(valid ? "ok: desktop unloaded-state window opened" : "failed: desktop unloaded state");
                    desktop.Shutdown(valid ? 0 : 1);
                }, DispatcherPriority.Background);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
