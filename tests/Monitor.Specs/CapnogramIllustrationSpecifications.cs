// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class CapnogramIllustrationSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static readonly Guid Resp = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Co2 = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static RegularPhysiologyPlan Timeline => PhysiologyIllustrationConfiguration.Default.ResolvePlan();
    private static CapnogramPlan Plan(CapnogramIllustration shape) => new(125_000_000, 250_000_000, 200_000_000, 0, 40) { Illustration = shape };
    public static Specification[] All =>
    [
        new(nameof(SelectedMorphologiesPreserveTerminalMeaning), SelectedMorphologiesPreserveTerminalMeaning),
        new(nameof(SpontaneousContourRoundsPhysicalPhaseJunctions), SpontaneousContourRoundsPhysicalPhaseJunctions),
        new(nameof(EsophagealWashoutDoesNotRepeatOrEraseChestMotion), EsophagealWashoutDoesNotRepeatOrEraseChestMotion),
        new(nameof(MandatoryBreathsHaveIndependentGasAndEffort), MandatoryBreathsHaveIndependentGasAndEffort),
        new(nameof(VentilationDepthGatingRetainsTheJointBreathPattern), VentilationDepthGatingRetainsTheJointBreathPattern),
        new(nameof(CurareEffortPrecedesDelayedGasCleft), CurareEffortPrecedesDelayedGasCleft),
        new(nameof(CardiogenicRippleUsesMechanicalEventsAndGasSupport), CardiogenicRippleUsesMechanicalEventsAndGasSupport),
        new(nameof(CapnogramCausesPreserveIndependentRespiration), CapnogramCausesPreserveIndependentRespiration),
        new(nameof(TransientActivationUsesCurrentExpirationOrdinal), TransientActivationUsesCurrentExpirationOrdinal),
        new(nameof(ActivatedWashoutPreservesGlobalVentilationDepth), ActivatedWashoutPreservesGlobalVentilationDepth),
        new(nameof(RespiratoryIllustrationsRestoreAndValidateOwnedState), RespiratoryIllustrationsRestoreAndValidateOwnedState),
    ];

    private static EventWaveformComposition Single(CapnogramIllustration shape, ulong cycle = 0) => EventWaveformComposition.Restore(new(
        Plan(shape).CreateChannel(Timeline, Co2, 0).Bands, [new(0, PhysiologyCycleEventKind.ExpirationStart, cycle)]));

    private static void SpontaneousContourRoundsPhysicalPhaseJunctions()
    {
        foreach (var plan in new[] { Plan(CapnogramIllustration.SpontaneousVentilation),
            Plan(CapnogramIllustration.SpontaneousVentilation) with { DeadSpaceNs = 200_000_000, RiseNs = 500_000_000 } })
        {
            var band = plan.CreateChannel(Timeline, Co2, 0).Bands.Single();
            var curve = EventWaveformComposition.Restore(new([band], [new(0, PhysiologyCycleEventKind.ExpirationStart, 0)]));
            long junctionNs = plan.DeadSpaceNs + plan.RiseNs;
            long at = curve.EvaluateAt(junctionNs);
            long before = curve.EvaluateAt(junctionNs - 4_000_000);
            long after = curve.EvaluateAt(junctionNs + 4_000_000);
            Check.That(before < at && at < after && Math.Abs(after - 2 * at + before) < 8 * Q,
                "physical rise/plateau junction has matching slopes rather than a visible corner");
            Check.That(band.TableQ32.All(value => value >= 0 && value <= 4000 * Q) &&
                band.TableQ32[32] == 0 && band.TableQ32[480] == 4000 * Q &&
                band.TableQ32[481] > band.TableQ32[500] && band.TableQ32[500] > band.TableQ32[511],
                "rounding retains dead space, terminal pressure and bounded inspiratory washout");
            long midpoint = (band.TableQ32[160] + band.TableQ32[320]) / 2;
            Check.That(Math.Abs(band.TableQ32[240] - midpoint) > Q,
                "the long spontaneous expiratory segment has actual source curvature");
        }
    }

    private static void SelectedMorphologiesPreserveTerminalMeaning()
    {
        var reversed = Single(CapnogramIllustration.Emphysema);
        Check.That(reversed.EvaluateAt(375_000_000) == 5000 * Q && reversed.EvaluateAt(1_875_000_000) == 4000 * Q &&
            reversed.EvaluateAt(900_000_000) > reversed.EvaluateAt(1_500_000_000),
            "reverse plateau keeps an earlier higher peak and the configured terminal concentration");
        var bifid = Single(CapnogramIllustration.RightMainstemIntubation);
        Check.That(bifid.EvaluateAt(800_000_000) > bifid.EvaluateAt(1_090_000_000) &&
            bifid.EvaluateAt(1_600_000_000) > bifid.EvaluateAt(800_000_000), "selected endobronchial example has sequential emptying components");
        var obstructed = Single(CapnogramIllustration.Obstructive);
        Check.That(obstructed.EvaluateAt(375_000_000) < 1600 * Q &&
            obstructed.EvaluateAt(900_000_000) < obstructed.EvaluateAt(1_600_000_000), "obstructive rise merges into an ascending plateau");
        var tail = Single(CapnogramIllustration.Pigtail);
        Check.That(tail.EvaluateAt(1_700_000_000) < 3000 * Q && tail.EvaluateAt(1_875_000_000) == 4000 * Q,
            "pigtail depicts terminal upswing without asserting a unique cause");
        var slowFall = Plan(CapnogramIllustration.MechanicalObstruction) with { InspiratoryFallNs = 600_000_000 };
        var mechanical = EventWaveformComposition.Restore(new(slowFall.CreateChannel(Timeline, Co2, 0).Bands,
            [new(0, PhysiologyCycleEventKind.ExpirationStart, 0)]));
        Check.That(mechanical.EvaluateAt(2_175_000_000) == 2000 * Q && obstructed.EvaluateAt(2_175_000_000) == 0,
            "partial mechanical obstruction has delayed inspiratory washout using explicit fall duration");
    }

    private static void EsophagealWashoutDoesNotRepeatOrEraseChestMotion()
    {
        long previous = long.MaxValue;
        for (ulong cycle = 0; cycle < 12; cycle++)
        {
            long value = Single(CapnogramIllustration.EsophagealIntubation, cycle).EvaluateAt(1_000_000_000);
            Check.That(cycle < 4 ? value > 0 && value < previous : value == 0,
                "residual CO2 washes out in four finite breaths and stays absent");
            previous = value;
        }
        Check.That(Single(CapnogramIllustration.EsophagealIntubation, ulong.MaxValue).EvaluateAt(1_000_000_000) == 0,
            "far-future ordinals cannot wrap into another residual breath");
        var config = PhysiologyIllustrationConfiguration.Default with { CapnogramIllustration = CapnogramIllustration.EsophagealIntubation };
        var curve = EventWaveformComposition.Restore(new(config.ResolveRespiration().CreateChannel(Timeline, Resp, 0).Bands,
            [new(30_000_000_000, PhysiologyCycleEventKind.InspirationStart, 8)]));
        Check.That(curve.EvaluateAt(31_875_000_000) == 1000 * Q, "absent sampled gas does not erase chest motion");
    }

    private static void MandatoryBreathsHaveIndependentGasAndEffort()
    {
        var config = PhysiologyIllustrationConfiguration.Default with
        {
            CapnogramIllustration = CapnogramIllustration.IntermittentMandatoryVentilation,
            RespirationIllustration = RespirationIllustration.IntermittentMandatoryVentilation
        };
        var events = RegularPhysiologyTimeline.Start(Timeline).AdvanceBefore(32_000_000_000, 300);
        var resp = EventWaveformComposition.Restore(new(config.ResolveRespiration().CreateChannel(Timeline, Resp, 0).Bands, events));
        var gas = EventWaveformComposition.Restore(new(config.ResolveCapnogram().CreateChannel(Timeline, Co2, 0).Bands, events));
        Check.That(resp.EvaluateAt(1_875_000_000) == 1000 * Q && resp.EvaluateAt(5_625_000_000) == 550 * Q &&
            gas.EvaluateAt(3_750_000_000) == 4000 * Q && gas.EvaluateAt(7_500_000_000) == 3680 * Q,
            "spontaneous effort depth does not scale gas concentration as if impedance were tidal volume");
        Check.That(resp.EvaluateAt(16_875_000_000) == resp.EvaluateAt(1_875_000_000) &&
            gas.EvaluateAt(18_750_000_000) == gas.EvaluateAt(3_750_000_000), "authored four-breath sequence repeats deterministically");
        var changed = config with { RespAmplitudeCounts = -2300 };
        Check.That(JsonSerializer.Serialize(changed.ResolveCapnogram().CreateChannel(Timeline, Co2, 0).Bands) ==
            JsonSerializer.Serialize(config.ResolveCapnogram().CreateChannel(Timeline, Co2, 0).Bands), "RESP display amplitude does not alter gas");
    }

    private static void VentilationDepthGatingRetainsTheJointBreathPattern()
    {
        var esophageal = PhysiologyIllustrationConfiguration.Default with { CapnogramIllustration = CapnogramIllustration.EsophagealIntubation };
        var blocked = PhysiologyIllustrationSource.Create(esophageal, ventilation: new(450000, 150000, 210000, AirwayOpen: false))
            .CaptureState().Channels.Single(channel => channel.ChannelId == PhysiologyIllustrationSource.ChannelId(4)).Generator;
        Check.That(EventWaveformComposition.Restore(new(blocked.Bands, [new(0, PhysiologyCycleEventKind.ExpirationStart, 0)]))
            .EvaluateAt(1_000_000_000) == 0, "physical absence of alveolar gas also admits a silent finite-washout template");
        foreach (var pattern in new[] { RespiratoryPattern.CheyneStokesIllustration, RespiratoryPattern.IntermittentIllustration })
        {
            var configuration = PhysiologyIllustrationConfiguration.Default with
            {
                RespiratoryPattern = pattern,
                CapnogramIllustration = CapnogramIllustration.IntermittentMandatoryVentilation
            };
            var normal = PhysiologyIllustrationSource.Create(configuration).CaptureState().Channels.Single(channel =>
                channel.ChannelId == PhysiologyIllustrationSource.ChannelId(4)).Generator;
            var coupled = PhysiologyIllustrationSource.Create(configuration, ventilation: new(450000, 150000, 210000))
                .CaptureState().Channels.Single(channel => channel.ChannelId == PhysiologyIllustrationSource.ChannelId(4)).Generator;
            for (ulong cycle = 0; cycle < 88; cycle++)
            {
                PhysiologyCycleEvent[] events = [new(0, PhysiologyCycleEventKind.ExpirationStart, cycle)];
                long reference = EventWaveformComposition.Restore(new(normal.Bands, events)).EvaluateAt(1_875_000_000);
                long actual = EventWaveformComposition.Restore(new(coupled.Bands, events)).EvaluateAt(1_875_000_000);
                bool alveolar = pattern == RespiratoryPattern.CheyneStokesIllustration
                    ? cycle % 11 is >= 1 and <= 7 : cycle % 10 is 0 or 1 or 2 or 5 or 6;
                Check.That(actual == (alveolar ? reference : 0),
                    "physical dead space gates each original breath without truncating the independent four-slot IMV sequence");
            }
        }
    }

    private static void CurareEffortPrecedesDelayedGasCleft()
    {
        var config = PhysiologyIllustrationConfiguration.Default with
        {
            CapnogramIllustration = CapnogramIllustration.CurareCleft,
            RespirationIllustration = RespirationIllustration.InspiratoryEffort,
            Co2TransportDelayMilliseconds = 300
        };
        var events = RegularPhysiologyTimeline.Start(Timeline).AdvanceBefore(4_000_000_000, 100);
        var plain = EventWaveformComposition.Restore(new(new RespirationPlan(1000).CreateChannel(Timeline, Resp, 0).Bands, events));
        var effort = EventWaveformComposition.Restore(new(config.ResolveRespiration().CreateChannel(Timeline, Resp, 0).Bands, events));
        var gas = EventWaveformComposition.Restore(new(config.ResolveCapnogram().CreateChannel(Timeline, Co2, 0).Bands, events));
        Check.That(effort.EvaluateAt(3_000_000_000) - plain.EvaluateAt(3_000_000_000) == 300 * Q &&
            effort.EvaluateAt(3_300_000_000) == plain.EvaluateAt(3_300_000_000), "chest effort is not delayed by the sampling tube");
        Check.That(gas.EvaluateAt(3_300_000_000) == 2240 * Q && gas.EvaluateAt(3_000_000_000) > 3700 * Q &&
            gas.EvaluateAt(3_600_000_000) > 3700 * Q, "CO2 cleft follows the same effort after transport delay");
    }

    private static void CardiogenicRippleUsesMechanicalEventsAndGasSupport()
    {
        var bands = Plan(CapnogramIllustration.CardiogenicOscillations).CreateChannel(Timeline, Co2, 0).Bands;
        PhysiologyCycleEvent[] events = [new(0, PhysiologyCycleEventKind.ExpirationStart, 0),
            new(700_000_000, PhysiologyCycleEventKind.VentricularMechanical, 0),
            new(1_200_000_000, PhysiologyCycleEventKind.VentricularMechanical, 1)];
        var actual = EventWaveformComposition.Restore(new(bands, events));
        var noHeart = EventWaveformComposition.Restore(new(bands, [events[0]]));
        Check.That(actual.EvaluateAt(800_000_000) < noHeart.EvaluateAt(800_000_000) &&
            actual.EvaluateAt(1_300_000_000) < noHeart.EvaluateAt(1_300_000_000) &&
            actual.EvaluateAt(1_050_000_000) == noHeart.EvaluateAt(1_050_000_000), "ripples follow actual mechanical heartbeat intervals");
        var noGas = EventWaveformComposition.Restore(new(bands, events.Skip(1).ToArray()));
        Check.That(noGas.EvaluateAt(800_000_000) == 0 && actual.EvaluateAt(2_500_000_000) == 0,
            "cardiac motion creates no gas during apnea and no inspired baseline");
        var delayedBands = (Plan(CapnogramIllustration.CardiogenicOscillations) with { TransportDelayNs = 300_000_000 }).CreateChannel(Timeline, Co2, 0).Bands;
        var delayed = EventWaveformComposition.Restore(new(delayedBands, events));
        Check.That(delayed.EvaluateAt(1_100_000_000) == actual.EvaluateAt(800_000_000), "sampling delay transports heart-induced gas motion");
    }

    private static void CapnogramCausesPreserveIndependentRespiration()
    {
        var reference = PhysiologyIllustrationConfiguration.Default;
        string expected = JsonSerializer.Serialize(reference.ResolveRespiration().CreateChannel(Timeline, Resp, 0));
        foreach (var shape in Enum.GetValues<CapnogramIllustration>())
        {
            var changed = reference with { CapnogramIllustration = shape };
            Check.That(JsonSerializer.Serialize(changed.ResolveRespiration().CreateChannel(Timeline, Resp, 0)) == expected,
                "capnogram selection alone cannot replace the independent RESP source");
        }
        var rebreathe = Plan(CapnogramIllustration.Rebreathing) with { BaselineMmHg = 5, EndExpiratoryMmHg = 45 };
        var absorber = rebreathe with { Illustration = CapnogramIllustration.ExhaustedAbsorber };
        Check.That(rebreathe.CreateChannel(Timeline, Co2, 0).Plane.OffsetNumerator == 5 &&
            JsonSerializer.Serialize(rebreathe.CreateChannel(Timeline, Co2, 0).Bands) == JsonSerializer.Serialize(absorber.CreateChannel(Timeline, Co2, 0).Bands),
            "absorber exhaustion can share the rebreathing contour and raised baseline");
    }

    private static void TransientActivationUsesCurrentExpirationOrdinal()
    {
        var group = PhysiologyIllustrationSource.Create();
        long cursor = 0;
        void Advance(long until)
        {
            while (cursor < until)
            {
                cursor = Math.Min(cursor + 50_000_000, until);
                group.AdvanceTo(cursor, 50, 1, 100);
            }
        }
        Advance(20_000_000_000);
        var config = PhysiologyIllustrationConfiguration.Default with { CapnogramIllustration = CapnogramIllustration.EsophagealIntubation };
        group.ContinueWith(PhysiologyIllustrationSource.Create(config));
        PhysiologySignalState GasState() => group.CaptureState().Channels.Single(channel => channel.ChannelId == PhysiologyIllustrationSource.ChannelId(4)).Generator;
        Check.That(GasState().Bands.All(band => band.ExpirationCycleOrigin == 5), "late activation starts at the next actual expiration ordinal");
        var active = GasState() with { ActiveFromEventTimeNs = null, History = [] };
        var samples = PhysiologySignalGenerator.Restore(active).GenerateBefore(37_500_000_000, 1800, 1000);
        long Peak(long from, long until) => samples.Where(sample => sample.Tick.SimTimeNs >= from && sample.Tick.SimTimeNs < until).Max(sample => sample.ValueQ32);
        Check.That(Peak(21_000_000_000, 22_500_000_000) > Peak(24_750_000_000, 26_250_000_000) &&
            Peak(24_750_000_000, 26_250_000_000) > Peak(28_500_000_000, 30_000_000_000) &&
            Peak(35_000_000_000, 37_500_000_000) == 0, "late activation shows declining breaths and persistent silence");
        Advance(24_000_000_000);
        group.ContinueWith(PhysiologyIllustrationSource.Create(config with { BundleBlock = EcgBundleBlockIllustration.CompleteRight }));
        Check.That(GasState().Bands.All(band => band.ExpirationCycleOrigin == 5), "an ECG-only change cannot restart washout");
        group.ContinueWith(PhysiologyIllustrationSource.Create(config with { CardiacActivity = CardiacActivity.Absent }));
        Check.That(GasState().Bands.All(band => band.ExpirationCycleOrigin == 5), "a changed cardiac event schedule also preserves unchanged gas washout");
        var restored = PhysiologyWaveformGroup.Restore(group.CaptureState());
        for (int index = 0; index < 20; index++)
        {
            cursor += 50_000_000;
            var first = group.AdvanceTo(cursor, 50, 1, 100);
            var second = restored.AdvanceTo(cursor, 50, 1, 100);
            Check.That(first.Count == second.Count && first.Zip(second).All(pair => pair.First.SequenceEqual(pair.Second)),
                "washout and outgoing tails restore during ECG change");
        }
        group.ContinueWith(PhysiologyIllustrationSource.Create());
        Advance(27_000_000_000);
        group.ContinueWith(PhysiologyIllustrationSource.Create(config));
        Check.That(GasState().Bands.All(band => band.ExpirationCycleOrigin == 7), "reselection restarts locally on the continuing global clock");
    }

    private static void ActivatedWashoutPreservesGlobalVentilationDepth()
    {
        var configuration = PhysiologyIllustrationConfiguration.Default with { RespiratoryPattern = RespiratoryPattern.CheyneStokesIllustration };
        var ventilation = new VentilationTransportPlan(450000, 150000, 210000);
        var source = PhysiologyIllustrationSource.Create(configuration, ventilation: ventilation);
        for (long time = 50_000_000; time <= 20_000_000_000; time += 50_000_000)
        { source.AdvanceTo(time, 50, 1, 100); }
        source.ContinueWith(PhysiologyIllustrationSource.Create(configuration with
        { CapnogramIllustration = CapnogramIllustration.EsophagealIntubation }, ventilation: ventilation));
        var bands = source.CaptureState().Channels.Single(channel => channel.ChannelId == PhysiologyIllustrationSource.ChannelId(4)).Generator.Bands;
        var withoutWashout = bands.Select(band => band with { ExpirationActivationGainsPermille = null, ExpirationCycleOrigin = 0 }).ToArray();
        foreach (ulong cycle in new ulong[] { 5, 6 })
        {
            PhysiologyCycleEvent[] events = [new(0, PhysiologyCycleEventKind.ExpirationStart, cycle)];
            long actual = EventWaveformComposition.Restore(new(bands, events)).EvaluateAt(1_000_000_000);
            long reference = EventWaveformComposition.Restore(new(withoutWashout, events)).EvaluateAt(1_000_000_000);
            int washout = cycle == 5 ? 1000 : 450;
            Check.That(actual > 0 && Math.Abs(actual * 1000 - reference * washout) <= 5000,
                "late washout retains the real deep Cheyne breath and its global CO2 response while applying local decay");
        }
        var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
        for (long time = 20_050_000_000; time <= 24_000_000_000; time += 50_000_000)
        {
            var expected = source.AdvanceTo(time, 50, 1, 100);
            var actual = restored.AdvanceTo(time, 50, 1, 100);
            Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
                "global depth gates and finite local decay restore together");
        }
    }

    private static void RespiratoryIllustrationsRestoreAndValidateOwnedState()
    {
        foreach (var shape in Enum.GetValues<CapnogramIllustration>())
        {
            var plan = Plan(shape) with { TransportDelayNs = 170_000_000, DispersionStepNs = 30_000_000 };
            var source = PhysiologySignalGenerator.Start(Timeline, "AcqCO2_100@1", 1, plan.CreateChannel(Timeline, Co2, 0).Bands);
            source.GenerateBefore(3_070_000_000, 400, 100);
            var replay = PhysiologySignalGenerator.Restore(JsonSerializer.Deserialize<PhysiologySignalState>(JsonSerializer.Serialize(source.CaptureState()))!);
            Check.That(source.GenerateBefore(8_000_000_000, 600, 100).SequenceEqual(replay.GenerateBefore(8_000_000_000, 600, 100)),
                "every shape and active transport tail survives serialized checkpoints");
        }
        int[] mutable = [1000, 550];
        var band = new RespirationPlan(1000).CreateChannel(Timeline, Resp, 0).Bands[0] with { InspirationCycleGainsPermille = mutable };
        var owned = EventWaveformComposition.Restore(new([band], [new(0, PhysiologyCycleEventKind.InspirationStart, 1)]));
        mutable[1] = 0;
        Check.That(owned.EvaluateAt(1_875_000_000) == 550 * Q, "inspiratory gains are owned after validation");
        int[] decay = [1000, 450];
        var finite = Plan(CapnogramIllustration.EsophagealIntubation).CreateChannel(Timeline, Co2, 0).Bands[0] with
        { ExpirationActivationGainsPermille = decay };
        var ownedFinite = EventWaveformComposition.Restore(new([finite], [new(0, PhysiologyCycleEventKind.ExpirationStart, 1)]));
        long before = ownedFinite.EvaluateAt(1_000_000_000);
        decay[1] = 0;
        Check.That(before > 0 && ownedFinite.EvaluateAt(1_000_000_000) == before, "activation gains are also copied before publication");
        foreach (var bad in new[] { band with { InspirationCycleGainsPermille = [] },
            band with { InspirationCycleGainsPermille = [-1] }, band with { Trigger = PhysiologyCycleEventKind.ExpirationStart },
            band with { ExpirationActivationGainsPermille = [1000] }, band with { CardiogenicRippleDurationNs = 100, CardiogenicRipplePermille = 5 },
            finite with { ExpirationActivationGainsPermille = [] }, finite with { ExpirationActivationGainsPermille = [-1] },
            finite with { ExpirationActivationGainsPermille = [1001] }, finite with { ExpirationActivationGainsPermille = null, ExpirationCycleOrigin = 3 } })
        {
            try { EventWaveformComposition.Restore(new([bad], [])); }
            catch (EventWaveformException) { continue; }
            throw new InvalidOperationException("Invalid respiratory band metadata accepted.");
        }
        foreach (var bad in new[] { Plan((CapnogramIllustration)99),
            Plan(CapnogramIllustration.Emphysema) with { EndExpiratoryMmHg = 327 },
            Plan(CapnogramIllustration.Emphysema) with { EndExpiratoryMmHg = 327, DispersionStepNs = 30_000_000 },
            Plan(CapnogramIllustration.CurareCleft) with { PlateauStartCentiMmHg = 3500 } })
        {
            try { bad.CreateChannel(Timeline, Co2, 0); }
            catch (EventWaveformException) { continue; }
            throw new InvalidOperationException("Invalid illustration or peak pressure accepted.");
        }
        try
        {
            new RespirationPlan(25206)
            {
                Illustration = RespirationIllustration.InspiratoryEffort,
                InspiratoryEffortDelayNs = Timeline.InspirationDurationNs,
                InspiratoryEffortDurationNs = 4
            }.CreateChannel(Timeline, Resp, 0);
        }
        catch (EventWaveformException) { return; }
        throw new InvalidOperationException("Fractional effort peak overflow accepted.");
    }
}
