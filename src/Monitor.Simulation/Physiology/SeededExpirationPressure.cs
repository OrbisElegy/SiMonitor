// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Finite authored breath-to-breath gas concentration, not metabolism feedback.
public sealed class SeededExpirationPressure
{
    public int TargetMmHg { get; }
    public int AmplitudeCentiMmHg { get; }
    public DeterministicStreamState PreparedState { get; }
    internal IReadOnlyList<int> Gains { get; }
    public SeededExpirationPressure(int targetMmHg, int amplitudeCentiMmHg, string seedHex)
    {
        if (targetMmHg is < 5 or > 80 || amplitudeCentiMmHg is < 0 or > 500 ||
            targetMmHg * 100 - amplitudeCentiMmHg < 500 || targetMmHg * 100 + amplitudeCentiMmHg > 8000)
        { throw new ArgumentException("SeededCo2.InvalidRange"); }
        using var factory = DeterministicStreamFactory.FromLowercaseHex(seedHex);
        var random = factory.CreateStream("physiology.respiration.co2");
        TargetMmHg = targetMmHg; AmplitudeCentiMmHg = amplitudeCentiMmHg;
        int[] gains = new int[64];
        gains[0] = 1000;
        for (int i = 1; i < gains.Length; i++)
        {
            int delta = (int)random.UniformBelow((ulong)(2 * amplitudeCentiMmHg + 1)) - amplitudeCentiMmHg;
            int nominal = targetMmHg * 100;
            int rounded = (int)FixedPointMath.RoundDivideTiesToEven((nominal + delta) * 1000L, nominal);
            gains[i] = Math.Clamp(rounded, ((nominal - amplitudeCentiMmHg) * 1000 + nominal - 1) / nominal,
                (nominal + amplitudeCentiMmHg) * 1000 / nominal);
        }
        Gains = Array.AsReadOnly(gains); PreparedState = random.CaptureState();
    }
}
