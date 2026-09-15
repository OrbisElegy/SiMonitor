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
        WaveformDemoWindow window = new(physiology: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
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
            if (window.SimulationTimeNs != 20_000_000_000 || window.LiveFrontierNs != 17_800_000_000)
            { throw new InvalidOperationException("20s of jittered callbacks drifted beyond the declared2.2s presentation buffer."); }
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
        Console.WriteLine("ok:20s jittered50–250ms callbacks retain source time, declared buffering, held views and stale timer fencing");
    }
}
