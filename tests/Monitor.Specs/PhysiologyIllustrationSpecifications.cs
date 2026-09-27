// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;

using Monitor.Simulation.Authoring;
using PhysiologyDemoConfiguration = Monitor.Simulation.Authoring.PhysiologyIllustrationConfiguration;
using PhysiologyDemoSource = Monitor.Simulation.Authoring.PhysiologyIllustrationSource;

namespace Monitor.Specs;

internal static class PhysiologyIllustrationSpecifications
{
    // Captured from632acca before the refactor: concatenate returned raw wire
    // envelopes from40 advances of200ms (30 completed blocks after delay).
    // SVT/VT/1:1 flutter goldens reflect the documented filling-limited corrections;
    // the other eleven remain the original pre-refactor outputs.
    // Independent expected bytes guard against shared test/source drift.
    public static Specification[] All =>
    [
        new(nameof(PreservesFourteenRecordedWireOutputs), PreservesFourteenRecordedWireOutputs),
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
    private static void PreservesFourteenRecordedWireOutputs()
    {
        (string Name, PhysiologyDemoConfiguration Configuration, int Envelopes, string Sha256)[] cases =
        [
            ("Default", PhysiologyDemoConfiguration.Default, 30, "aa5d72387421f94e426c51bb2bc9c19058277228bd1b5ef30302e008e59664d7"),
            ("SinusArrhythmiaPreset", PhysiologyDemoConfiguration.SinusArrhythmiaPreset, 30, "06e0da37878dbef0d34268a0c0ad5b9a3298381fb86b084dd8e1c5f42758fdc7"),
            ("SinusArrestPreset", PhysiologyDemoConfiguration.SinusArrestPreset, 30, "6b2489946e42d3d13cb9740e33d86df8eecec9264c3f6303f35b1d8b42f0e5fe"),
            ("AtrialEscapePreset", PhysiologyDemoConfiguration.AtrialEscapePreset, 30, "578b151583a092154a554a29c08411bf499d1e2210e3ffd8442ac7d51e49641c"),
            ("AarPreset", PhysiologyDemoConfiguration.AarPreset, 30, "240e79245bf4736c11b6d782982812c5b50c447592d0d94cee0f3267f9c64dce"),
            ("AjrPreset", PhysiologyDemoConfiguration.AjrPreset, 30, "01a473cad6b807d769519a45dcc04c7341d8617d6b9c310f4eba24f69f64f294"),
            ("AivrPreset", PhysiologyDemoConfiguration.AivrPreset, 30, "227cc44d118c39fb186017ab19918f74da2bb9b8229a00f598081643a033357b"),
            ("VtPreset", PhysiologyDemoConfiguration.VtPreset, 30, "24cdef032803730b1862bc8604cf4b509c3a806621bad261f813ecc6a66489e1"),
            ("SvtPreset", PhysiologyDemoConfiguration.SvtPreset, 30, "1e3020ff3b1ffc6ffa93937eabab421a425c514feec76140aed370b9f2316630"),
            ("PrematureVentricular", PhysiologyDemoConfiguration.PrematureVentricular, 30, "c4ae9df61ddf8ec848e74e7b19e096566d620dbf6e4acdbad29a6dbfcd721565"),
            ("PrematureAtrial", PhysiologyDemoConfiguration.PrematureAtrial, 30, "345d15d40686708b415f206ca172a39f567952a98625e073a6be6cedec5f1d03"),
            ("PrematureJunctional", PhysiologyDemoConfiguration.PrematureJunctional, 30, "6bf12ba27510575cdda201b40e5ad6c79b77b714e5cca430b550ce245db7109a"),
            ("FlutterOneToOne", PhysiologyDemoConfiguration.Flutter(1), 30, "bd406394bea15d7452312e9e50b20ce73b2a9c834b35ae8fe5e9453501c06bfa"),
            ("Fibrillation", PhysiologyDemoConfiguration.Fibrillation(), 30, "6db60b01aa40074cb96402a1e913daa4b0cd1f7d03d05f4679d30ad41394d52f"),
        ];
        foreach (var item in cases)
        {
            var source = PhysiologyDemoSource.Create(item.Configuration);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int count = 0;
            for (long time = 200_000_000; time <= 8_000_000_000; time += 200_000_000)
                foreach (byte[] wire in source.AdvanceTo(time, 50, 1, 100))
                { hash.AppendData(wire); count++; }
            string actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (count != item.Envelopes || actual != item.Sha256)
            { throw new InvalidOperationException(item.Name + " changed the recorded seven-channel wire output: " + actual); }
        }
    }
}
