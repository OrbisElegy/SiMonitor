// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;

using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using PhysiologyDemoConfiguration = Monitor.Simulation.Authoring.PhysiologyIllustrationConfiguration;
using PhysiologyDemoSource = Monitor.Simulation.Authoring.PhysiologyIllustrationSource;

namespace Monitor.Specs;

internal static class PhysiologyIllustrationSpecifications
{
    // Captured from632acca before the refactor: concatenate returned raw wire
    // envelopes for the first six seconds of source time (30 complete blocks).
    // These same source windows are now available without the former acquisition wait.
    // Goldens include the documented filling corrections and authored smooth
    // ABP/PA/CVP/CO2 contours, including the ejection-bounded AIVR baseline.
    // ECG, RESP and PLETH retain their previous samples.
    // CVP now encodes absolute pressure; normalize only that representation back
    // to the recorded baseline/increment pair before hashing. Frozen hashes still
    // guard every physical sample, other channel and acquisition metadata.
    public static Specification[] All =>
    [
        new(nameof(PreservesFourteenRecordedPhysicalOutputs), PreservesFourteenRecordedPhysicalOutputs),
        new(nameof(HeadlessSelectionAndValidationRetainBoundaries), HeadlessSelectionAndValidationRetainBoundaries),
    ];
    private static void HeadlessSelectionAndValidationRetainBoundaries()
    {
        for (int id = 0; id <= 42; id++)
        {
            var ratio = AuthoredConductionSelection.Resolve(id);
            var pattern = AuthoredConductionSelection.Pattern(id);
            Check.That(AuthoredConductionSelection.Index(ratio.Atrial, ratio.Conducted, pattern) == id,
                "existing illustration selection retains its canonical ID");
        }
        foreach (Action action in new Action[] {
            () => AuthoredConductionSelection.Resolve(43),
            () => PhysiologyDemoSource.Create(PhysiologyDemoConfiguration.Default with { BreathPeriodMilliseconds = 0 }),
            () => PhysiologyDemoSource.Create(PhysiologyDemoConfiguration.SinusArrestPreset with { UseVascularReservoir = false }),
            () => PhysiologyDemoSource.Create(abpZeroOffsetCentiMmHg: 1001),
            () => IllustrationVentricularTiming.ResolveOffset(null, 200, 160_000_000) })
        {
            bool rejected = false;
            try { action(); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "headless construction preserves authored validation boundaries");
        }
    }
    private static void PreservesFourteenRecordedPhysicalOutputs()
    {
        (string Name, PhysiologyDemoConfiguration Configuration, int Envelopes, string Sha256)[] cases =
        [
            ("Default", PhysiologyDemoConfiguration.Default, 30, "f00461d9eb7c70fcf0e2b409403033491af48096f5f41869df633ef063c27f5a"),
            ("SinusArrhythmiaPreset", PhysiologyDemoConfiguration.SinusArrhythmiaPreset, 30, "9b05867893a1fc835d6fa30a04f032f98e935ca62780727dc4af8a165e11b137"),
            ("SinusArrestPreset", PhysiologyDemoConfiguration.SinusArrestPreset, 30, "0947bc0f9f42783334e5b60f43a762ab67377a2e65b6d385735c5dcaccd77f77"),
            ("AtrialEscapePreset", PhysiologyDemoConfiguration.AtrialEscapePreset, 30, "5504d44aea5b9e97665e840409abc56e91834f37e7d4ead5e6dc2c569393f280"),
            ("AarPreset", PhysiologyDemoConfiguration.AarPreset, 30, "b014ab6eab4fa9df98c4ca8d2ea4a207335eda249f93c0dd3025c3dd482436da"),
            ("AjrPreset", PhysiologyDemoConfiguration.AjrPreset, 30, "10aa268f63b2de4337aa7b5340b53700767756b93d93559465b64003137db5d3"),
            ("AivrPreset", PhysiologyDemoConfiguration.AivrPreset, 30, "05181dac836c290d9ccf5fe4a0353126d240471116edba0cdef8e31e25c9083e"),
            ("VtPreset", PhysiologyDemoConfiguration.VtPreset, 30, "c2c478f9a04f045565a0434ca1dd7c8ff5f5896a9c155b3d66f25ca4a4051cfc"),
            ("SvtPreset", PhysiologyDemoConfiguration.SvtPreset, 30, "ff02e9a949a3903ae71640e7fa505a3b4f67a55248e4ae2151f6a472f1e02e1d"),
            ("PrematureVentricular", PhysiologyDemoConfiguration.PrematureVentricular, 30, "805a1b3a3a48a2d3f200568bec468e06b729669b098f84ad03d1b7da8fac35a0"),
            ("PrematureAtrial", PhysiologyDemoConfiguration.PrematureAtrial, 30, "946edcd401f9264677ab6e2eb9f5f7a884fa738bbe47951f12ad0e1dd968e1f9"),
            ("PrematureJunctional", PhysiologyDemoConfiguration.PrematureJunctional, 30, "a44db801a84f2433020371325b9c975cf54209c4679e2328f1695be528e64da9"),
            ("FlutterOneToOne", PhysiologyDemoConfiguration.Flutter(1), 30, "afc158a0f68bfcca11c3b10faf254ebaf140e1aa48c659d4243ea0423a8fdfa2"),
            ("Fibrillation", PhysiologyDemoConfiguration.Fibrillation(), 30, "126b7e03c139295b058ec9b7faf90b2982e62fa7fa58d4b8e609311ea5460e7d"),
        ];
        foreach (var item in cases)
        {
            var source = PhysiologyDemoSource.Create(item.Configuration);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int count = 0;
            for (long time = 200_000_000; time <= 6_000_000_000; time += 200_000_000)
                foreach (byte[] wire in source.AdvanceTo(time, 50, 1, 100))
                {
                    var envelope = WaveformEnvelopeCodec.Decode(wire);
                    var cvp = envelope.Planes.Single(plane => plane.ChannelId == PhysiologyDemoSource.ChannelId(6));
                    Check.That(cvp.ScaleNumerator == 1 && cvp.ScaleDenominator == 100 &&
                        cvp.OffsetNumerator == 0 && cvp.OffsetDenominator == 1,
                        "CVP uses absolute centi-mmHg samples with fixed zero-offset encoding");
                    var legacy = cvp with
                    {
                        OffsetNumerator = item.Configuration.CvpBaselineCentiMmHg,
                        OffsetDenominator = 100,
                        Samples = cvp.Samples.Select(value => checked((short)(value - item.Configuration.CvpBaselineCentiMmHg))).ToArray()
                    };
                    hash.AppendData(WaveformEnvelopeCodec.EncodeRaw(envelope with
                    { Planes = envelope.Planes.Select(plane => plane.ChannelId == cvp.ChannelId ? legacy : plane).ToArray() }));
                    count++;
                }
            string actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (count != item.Envelopes || actual != item.Sha256)
            { throw new InvalidOperationException(item.Name + " changed the recorded seven-channel physical output: " + actual); }
        }
    }
}
