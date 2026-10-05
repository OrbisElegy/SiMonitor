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
    // envelopes from40 advances of200ms (30 completed blocks after delay).
    // SVT/VT/1:1 flutter and AAR/AJR/AIVR goldens reflect their documented
    // filling corrections; other fixtures retain the pre-refactor outputs.
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
            ("Default", PhysiologyDemoConfiguration.Default, 30, "aa5d72387421f94e426c51bb2bc9c19058277228bd1b5ef30302e008e59664d7"),
            ("SinusArrhythmiaPreset", PhysiologyDemoConfiguration.SinusArrhythmiaPreset, 30, "513f090ab577d9b42e2a703bb74596f214f8aef01dff493d590959cc10d1944e"),
            ("SinusArrestPreset", PhysiologyDemoConfiguration.SinusArrestPreset, 30, "6b2489946e42d3d13cb9740e33d86df8eecec9264c3f6303f35b1d8b42f0e5fe"),
            ("AtrialEscapePreset", PhysiologyDemoConfiguration.AtrialEscapePreset, 30, "578b151583a092154a554a29c08411bf499d1e2210e3ffd8442ac7d51e49641c"),
            ("AarPreset", PhysiologyDemoConfiguration.AarPreset, 30, "014dca22030d55235b0a34b906298eee3b55a06a189df049e6c1f8419755b03d"),
            ("AjrPreset", PhysiologyDemoConfiguration.AjrPreset, 30, "b78ec0c75e0ea0bf332c5e634dd403dacbc5b0fa3f8fbbd89c80f17d36be3785"),
            ("AivrPreset", PhysiologyDemoConfiguration.AivrPreset, 30, "d9e445e4a5464129d6048363ebef410da4dbbfe43f09b0e17cda65b07634a38f"),
            ("VtPreset", PhysiologyDemoConfiguration.VtPreset, 30, "7b14508fe4b765fe58317e202338c363f0aedb60ac3a5401a84385de52161494"),
            ("SvtPreset", PhysiologyDemoConfiguration.SvtPreset, 30, "51de3ce35de895e973e3016704cb02f92d6e89645e17b7587d76fbc1c7372783"),
            ("PrematureVentricular", PhysiologyDemoConfiguration.PrematureVentricular, 30, "c4ae9df61ddf8ec848e74e7b19e096566d620dbf6e4acdbad29a6dbfcd721565"),
            ("PrematureAtrial", PhysiologyDemoConfiguration.PrematureAtrial, 30, "345d15d40686708b415f206ca172a39f567952a98625e073a6be6cedec5f1d03"),
            ("PrematureJunctional", PhysiologyDemoConfiguration.PrematureJunctional, 30, "6bf12ba27510575cdda201b40e5ad6c79b77b714e5cca430b550ce245db7109a"),
            ("FlutterOneToOne", PhysiologyDemoConfiguration.Flutter(1), 30, "eab239912876382af1666f95bab3ccc946a9d2d6aef1cc28313554184e8efba6"),
            ("Fibrillation", PhysiologyDemoConfiguration.Fibrillation(), 30, "95d504ea87beab28b6ea9b0744cb95c7f14c9c2dd7e9a02d71cb05ab3a0b5471"),
        ];
        foreach (var item in cases)
        {
            var source = PhysiologyDemoSource.Create(item.Configuration);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int count = 0;
            for (long time = 200_000_000; time <= 8_000_000_000; time += 200_000_000)
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
