// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class MeasuredLimitSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(MeasuredLimitsKeepSourcesAndUnitsSeparate), MeasuredLimitsKeepSourcesAndUnitsSeparate),
        new(nameof(MeasuredLimitsRejectInvalidInputs), MeasuredLimitsRejectInvalidInputs),
        new(nameof(MeasuredLimitsFollowAcquisitionAndQuality), MeasuredLimitsFollowAcquisitionAndQuality)
    ];
    private static LiveMeasurementSnapshot Empty() => LiveWaveformMeasurements.CreateIllustration().Read(0);
    private static LiveMeasurementSnapshot Set(LiveMeasurementSnapshot s, MonitorNumeric numeric, int? value, WaveformMeasurementStatus status = WaveformMeasurementStatus.Valid) => numeric switch
    {
        MonitorNumeric.RespirationRate => s with { ImpedanceRespiration = new(status, value, 0) },
        MonitorNumeric.PulseRate => s with { PulseRate = new(status, value, 0) },
        MonitorNumeric.EtCo2 => s with { Capnography = s.Capnography with { EndTidalCentiMmHg = new(status, value, 0) } },
        MonitorNumeric.Co2RespirationRate => s with { Capnography = s.Capnography with { RespirationsMilliPerMinute = new(status, value, 0) } },
        MonitorNumeric.AbpMean => s with { AbpMean = new(status, value, 0, 0) },
        MonitorNumeric.PaMean => s with { PaMean = new(status, value, 0, 0) },
        MonitorNumeric.CvpMean => s with { CvpMean = new(status, value, 0, 0) },
        _ => throw new ArgumentOutOfRangeException(nameof(numeric))
    };
    private static void MeasuredLimitsKeepSourcesAndUnitsSeparate()
    {
        foreach (var d in MeasuredLimitNotice.Descriptors)
        {
            var limits = d.TeachingDefaults with { Enabled = true };
            int cl = limits.CriticalLow!.Value, wl = limits.WarningLow!.Value, wh = limits.WarningHigh!.Value, ch = limits.CriticalHigh!.Value;
            foreach (var (value, level) in new (int, MonitorNoticeLevel?)[]
            { (cl - 1, MonitorNoticeLevel.Critical), (cl, MonitorNoticeLevel.Warning), (wl - 1, MonitorNoticeLevel.Warning),
                (wl, null), (wh, null), (wh + 1, MonitorNoticeLevel.Warning), (ch, MonitorNoticeLevel.Warning), (ch + 1, MonitorNoticeLevel.Critical) })
            {
                var snapshot = Set(Empty(), d.Numeric, value);
                var notice = MeasuredLimitNotice.Evaluate(d.Numeric, limits, snapshot);
                Check.That(notice?.Level == level && (notice is null || notice.Numeric == d.Numeric), "strict native-unit boundary and target for " + d.Label);
                Check.That(MeasuredLimitNotice.Evaluate(d.Numeric, d.TeachingDefaults, snapshot) is null, "every default is disabled");
                foreach (var other in MeasuredLimitNotice.Descriptors.Where(x => x.Numeric != d.Numeric))
                { Check.That(MeasuredLimitNotice.Evaluate(other.Numeric, other.TeachingDefaults with { Enabled = true }, snapshot) is null, "a valid source never supplies another source's rate or pressure"); }
            }
        }
        var cvp = MeasuredLimitNotice.Describe(MonitorNumeric.CvpMean);
        Check.That(MeasuredLimitNotice.Evaluate(cvp.Numeric, cvp.TeachingDefaults with { Enabled = true }, Set(Empty(), cvp.Numeric, -1))?.Level == MonitorNoticeLevel.Warning,
            "valid negative venous pressure is measurable and compared, not treated as missing");
    }
    private static void MeasuredLimitsRejectInvalidInputs()
    {
        foreach (var d in MeasuredLimitNotice.Descriptors)
        {
            var limits = d.TeachingDefaults with { Enabled = true };
            foreach (var status in Enum.GetValues<WaveformMeasurementStatus>().Where(s => s != WaveformMeasurementStatus.Valid))
            { Check.That(MeasuredLimitNotice.Evaluate(d.Numeric, limits, Set(Empty(), d.Numeric, d.Maximum, status)) is null, "nonvalid readings never retain high/low condition"); }
            foreach (int? value in new int?[] { null, d.Minimum - 1, d.Maximum + 1 })
            { Check.That(MeasuredLimitNotice.Evaluate(d.Numeric, limits, Set(Empty(), d.Numeric, value)) is null, "bad value cannot alarm"); }
            foreach (var bad in new[] { limits with { CriticalLow = null }, limits with { WarningLow = null }, limits with { WarningHigh = null }, limits with { CriticalHigh = null },
                limits with { CriticalLow = limits.WarningLow }, limits with { WarningLow = limits.WarningHigh }, limits with { WarningHigh = limits.CriticalHigh },
                limits with { CriticalLow = d.Minimum - 1 }, limits with { CriticalHigh = d.Maximum + 1 } })
            {
                var notice = MeasuredLimitNotice.Evaluate(d.Numeric, bad, Set(Empty(), d.Numeric, d.Maximum));
                Check.That(notice?.Level == MonitorNoticeLevel.Info && notice.Numeric is null && notice.Id == d.Id + "-settings", "settings fault stays source-specific Info");
            }
        }
        foreach (var unsupported in new[] { MonitorNumeric.HeartRate, MonitorNumeric.SpO2, (MonitorNumeric)999 })
        {
            bool rejected = false;
            try { MeasuredLimitNotice.Describe(unsupported); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "existing HR/SpO2 policies are not silently rebound");
        }
    }
    private static void MeasuredLimitsFollowAcquisitionAndQuality()
    {
        var source = PhysiologyIllustrationSource.Create();
        var measurement = LiveWaveformMeasurements.CreateIllustration();
        var wires = new List<byte[]>();
        for (int i = 1; i <= 110; i++) { wires.AddRange(source.AdvanceTo(i * 200_000_000L, 50, 1, 100)); }
        LiveMeasurementSnapshot snapshot = Empty();
        foreach (byte[]? wire in wires.Take(80)) { snapshot = measurement.Consume(wire); }
        // Deliberately narrow teaching limits so every actual acquired channel
        // exercises the high path, without substituting any configured target.
        var narrow = new MeasurementLimits(true, -1000, -500, 0, 1);
        foreach (var d in MeasuredLimitNotice.Descriptors)
        {
            var limits = d.Minimum < 0 ? narrow : new(true, 0, 1, 2, 3);
            Check.That(MeasuredLimitNotice.Evaluate(d.Numeric, limits, snapshot)?.Level == MonitorNoticeLevel.Critical, "real acquired measurement triggers own condition: " + d.Label);
        }
        var block = WaveformEnvelopeCodec.Decode(wires[80]);
        var damaged = block with
        {
            Planes = block.Planes.Select(p => p.ChannelId == PhysiologyIllustrationSource.ChannelId(4) ? p with
            { QualityEncoding = WaveformQualityEncoding.Ranges, QualityRanges = [new(0, (uint)p.Samples.Count, 1)] } : p).ToArray()
        };
        var badSnapshot = measurement.Consume(WaveformEnvelopeCodec.EncodeRaw(damaged));
        var rateLimits = new MeasurementLimits(true, 0, 1, 2, 3);
        Check.That(MeasuredLimitNotice.Evaluate(MonitorNumeric.EtCo2, rateLimits, badSnapshot) is null &&
            MeasuredLimitNotice.Evaluate(MonitorNumeric.Co2RespirationRate, rateLimits, badSnapshot) is null &&
            MeasuredLimitNotice.Evaluate(MonitorNumeric.RespirationRate, rateLimits, badSnapshot)?.Level == MonitorNoticeLevel.Critical,
            "CO2 quality clears only CO2 conditions; independent impedance rate remains active");
        var expired = measurement.Read(badSnapshot.SampleTimeNs + 600_000_000);
        foreach (var d in MeasuredLimitNotice.Descriptors)
        { Check.That(MeasuredLimitNotice.Evaluate(d.Numeric, d.Minimum < 0 ? narrow : rateLimits, expired) is null, "sample loss clears every old condition"); }
    }
}
