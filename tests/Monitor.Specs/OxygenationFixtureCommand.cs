// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

// Reproducible interop fixture, kept outside the monitor's deterministic runtime.
internal static class OxygenationFixtureCommand
{
    private const long StepNs = 8_000_000;
    private static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true };

    internal static int Execute(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length != 2)
        { error.WriteLine("Usage: --oxygen[-deep]-transport-fixture OUTPUT.json | --oxygenation[-deep]-replay-check RESULT.json | --oxygenation-realtime-check DEEP_RESULT.json"); return 2; }
        try
        {
            bool realtime = args[0] == "--oxygenation-realtime-check";
            bool deep = realtime || args[0] is "--oxygen-deep-transport-fixture" or "--oxygenation-deep-replay-check";
            long durationNs = deep ? 300_000_000_000 : 120_000_000_000;
            var configuration = PhysiologyIllustrationConfiguration.Default with
            { RespiratoryActivity = RespiratoryActivity.Absent, ActivityAfterBreaths = 8, ActivityDurationBreaths = deep ? 40 : 8 };
            var transport = PhysiologyIllustrationSource.CreateTransport(configuration, new(450_000, 150_000, 210_000), 66_667);
            if (args[0] is "--oxygen-transport-fixture" or "--oxygen-deep-transport-fixture")
            {
                var intervals = new List<PhysiologyTransportInterval>();
                for (long from = 0; from < durationNs; from += StepNs)
                { intervals.Add(transport.Integrate(from, from + StepNs, cancellationToken: cancellationToken)); }
                var fixture = new { Format = "OxygenTransportReplayInput@1", Transport = transport.CaptureState(), SampleStepNs = StepNs, Intervals = intervals };
                string path = Path.GetFullPath(args[1]);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(fixture, JsonOptions) + "\n", new UTF8Encoding(false));
                output.WriteLine($"PASS: {intervals.Count} physical input intervals; {path}");
                return 0;
            }
            using var document = JsonDocument.Parse(File.ReadAllText(args[1], Encoding.UTF8));
            var root = document.RootElement;
            if (root.GetProperty("Format").GetString() != "OxygenTransportReplayResult@1" ||
                root.GetProperty("Transport").Deserialize<PhysiologyTransportState>() != transport.CaptureState())
            { throw new ArgumentException("OxygenationFixture.TransportMismatch"); }
            var oxygen = new SampledArterialOxygenation(root.GetProperty("Oxygenation").Deserialize<SampledArterialOxygenationState>()!);
            if (oxygen.StartSimTimeNs != 0 || oxygen.EndSimTimeNs != durationNs || oxygen.SampleStepNs != StepNs)
            { throw new ArgumentException("OxygenationFixture.ClockMismatch"); }
            int maximumDifferenceMilliPercent = 0;
            decimal maximumBalanceResidualMl = 0;
            if (realtime)
            {
                var solver = new RealtimeOxygenationSource(transport, new());
                for (long time = 0; time <= durationNs; time += StepNs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    solver.AdvanceTo(time);
                    maximumDifferenceMilliPercent = Math.Max(maximumDifferenceMilliPercent,
                        Math.Abs(solver.ReadAt(time).SaturationMilliPercent - oxygen.ReadAt(time).SaturationMilliPercent));
                    maximumBalanceResidualMl = Math.Max(maximumBalanceResidualMl, Math.Abs(solver.Snapshot.OxygenBalanceResidualMl));
                }
                Check.That(maximumDifferenceMilliPercent <= 2 && maximumBalanceResidualMl < .000000000000000001m,
                    $"decimal runtime matches independent Python RK4: difference={maximumDifferenceMilliPercent}, residual={maximumBalanceResidualMl}");
            }
            var session = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), true,
                oxygenation: realtime ? null : oxygen,
                realtimeOxygenation: realtime ? RealtimeOxygenationConfiguration.ReferenceAdult : null);
            int initial = 0, minimum = 100000, final = 0;
            int below70 = 0, invalidAfterWarmup = 0;
            bool warning = false, critical = false;
            var transitions = new List<object>();
            MonitorNoticeLevel? previousLevel = null;
            while (session.SimulationTimeNs < durationNs + 2_000_000_000)
            {
                cancellationToken.ThrowIfCancellationRequested();
                session.Advance(250_000_000);
                var reading = session.Measurements!.SpO2;
                var notice = SpO2LimitNotice.Evaluate(true, 92000, 85000, reading);
                if (notice?.Level != previousLevel)
                {
                    transitions.Add(new { session.SimulationTimeNs, reading.MeasuredAtNs, reading.SaturationMilliPercent, Level = notice?.Level.ToString() ?? "Clear" });
                    previousLevel = notice?.Level;
                }
                warning |= notice?.Level == MonitorNoticeLevel.Warning;
                critical |= notice?.Level == MonitorNoticeLevel.Critical;
                if (reading.Status != WaveformMeasurementStatus.Valid)
                {
                    if (session.SimulationTimeNs >= 8_000_000_000) { invalidAfterWarmup++; }
                    continue;
                }
                final = reading.SaturationMilliPercent!.Value;
                minimum = Math.Min(minimum, final);
                if (final < 70000)
                {
                    below70++;
                    Check.That(notice?.Level == MonitorNoticeLevel.Critical, "valid deep oxygenation retains its critical alarm");
                }
                if (session.SimulationTimeNs == 12_000_000_000) { initial = final; }
            }
            Check.That(initial > 96000 && minimum < initial - 1500 && final > minimum + 1500,
                $"actual red/IR estimator follows apnea and recovery: initial={initial}, minimum={minimum}, final={final}");
            if (deep)
            {
                Check.That(below70 > 0 && warning && critical && invalidAfterWarmup == 0 &&
                    final > 96000 && SpO2LimitNotice.Evaluate(true, 92000, 85000, session.Measurements!.SpO2) is null,
                    $"deep physical replay must retain numeric measurement and recover without false loss: below70={below70}, invalid={invalidAfterWarmup}, minimum={minimum}, final={final}");
            }
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Passed = true,
                RealtimeModelId = realtime ? OxygenReservoirModel.ModelId : null,
                MaximumDifferenceMilliPercent = maximumDifferenceMilliPercent,
                MaximumBalanceResidualMl = maximumBalanceResidualMl,
                OpticalModelId = PulseOximeterIllustrationSource.ModelId,
                InitialSpo2MilliPercent = initial,
                MinimumSpo2MilliPercent = minimum,
                FinalSpo2MilliPercent = final,
                MinimumModelSao2MilliPercent = oxygen.CaptureState().SaturationMilliPercent.Min(),
                Below70Readings = below70,
                InvalidAfterWarmupReadings = invalidAfterWarmup,
                AlarmTransitions = transitions,
                session.SimulationTimeNs,
                SampleTimeNs = session.Measurements!.SampleTimeNs,
            }, JsonOptions));
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or JsonException or InvalidOperationException or OperationCanceledException)
        { error.WriteLine(exception.Message); return 1; }
    }
}
