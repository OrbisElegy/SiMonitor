// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Monitor.Desktop;

internal static class DemoFrameTimingSmokeChecks
{
    internal static void Verify()
    {
        if (DemoFrameTiming.ResolveElapsed(TimeSpan.FromMilliseconds(50)) != 50_000_000 ||
            DemoFrameTiming.ResolveElapsed(TimeSpan.FromMilliseconds(250)) != 250_000_000 ||
            DemoFrameTiming.ResolveElapsed(TimeSpan.FromMilliseconds(251)) != 50_000_000 ||
            DemoFrameTiming.ResolveElapsed(TimeSpan.MaxValue) != 50_000_000 ||
            DemoFrameTiming.ResolveElapsed(TimeSpan.MinValue) != 1 || DemoFrameTiming.ResolveElapsed(TimeSpan.Zero) != 1)
        { throw new InvalidOperationException("Demo frame jitter and suspension boundaries changed."); }
        foreach (bool projected in new[] { false, true })
        {
            WaveformDemoWindow window = new(physiology: !projected, projected: projected);
            window.Show();
            void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            try
            {
                if (projected)
                {
                    window.TPeakInput.Text = "50";
                    window.TScaleInputs[2].Text = "-1.25";
                    window.UAmplitudeInputs[2].Text = "60";
                    window.ChestJInput.Text = "200";
                    window.ChestStEndInput.Text = "-100";
                    Click(window.ApplyEcgButton);
                }
                Click(window.RunButton);
                var timer = window.ActiveTimer;
                long expected = 0;
                for (int cycle = 0; cycle < 100; cycle++)
                {
                    foreach (int ms in new[] { 16, 60, 90, 34 })
                    {
                        window.Pulse(timer, DemoFrameTiming.ResolveElapsed(TimeSpan.FromMilliseconds(ms)));
                        expected += ms * 1_000_000L;
                        if (window.SimulationTimeNs != expected || !ReferenceEquals(timer, window.ActiveTimer))
                        { throw new InvalidOperationException("Ordinary dispatcher delay lost simulation time or failed bounded generation."); }
                    }
                }
                if (window.SimulationTimeNs != 20_000_000_000 || window.LiveFrontierNs != (projected ? 19_760_000_000 : 17_800_000_000))
                { throw new InvalidOperationException("20s of jittered callbacks drifted beyond the declared presentation buffer."); }
                Click(window.HoldButton);
                var held = window.Trace;
                window.Pulse(timer, DemoFrameTiming.ResolveElapsed(TimeSpan.FromMilliseconds(250)));
                if (window.SimulationTimeNs != 20_250_000_000 || !ReferenceEquals(held, window.Trace))
                { throw new InvalidOperationException("Bounded multi-step generation changed held pixels or lost time."); }
                Click(window.RunButton);
                window.Pulse(timer, 90_000_000);
                if (window.SimulationTimeNs != 20_250_000_000)
                { throw new InvalidOperationException("Stale timer bypassed the pause fence."); }
            }
            finally { window.Close(); }
        }
        Console.WriteLine("ok: physiology and12-lead20s jittered50–250ms callbacks retain source time, declared buffering, held views and stale timer fencing");
    }
}
