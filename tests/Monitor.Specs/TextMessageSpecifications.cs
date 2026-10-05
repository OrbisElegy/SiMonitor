// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Localization;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Localization;

namespace Monitor.Specs;

internal static class TextMessageSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(MessagesRenderNestedKeysAndCompareStructurally), MessagesRenderNestedKeysAndCompareStructurally),
        new(nameof(MeasurementNoticesRenderTheirDefaultTextInChinese), MeasurementNoticesRenderTheirDefaultTextInChinese),
        new(nameof(LimitNoticesRenderTheirDefaultTextInChinese), LimitNoticesRenderTheirDefaultTextInChinese),
    ];

    private static readonly ITextLocalizer Chinese = BuiltInLocalizations.Create("zh-CN");
    private static readonly ITextLocalizer English = BuiltInLocalizations.Create("en");

    private static void MessagesRenderNestedKeysAndCompareStructurally()
    {
        var message = new TextMessage("alarm.limitCriticalLow", new TextMessage("numeric.abpMean"));
        Check.That(message.Render(Chinese) == "ABP 平均压 极低" && message.Render(English) == "ABP mean extremely low",
            "nested keys render in the reader's language");
        Check.That(message.Equals(new TextMessage("alarm.limitCriticalLow", new TextMessage("numeric.abpMean"))) &&
            message.GetHashCode() == new TextMessage("alarm.limitCriticalLow", new TextMessage("numeric.abpMean")).GetHashCode() &&
            !message.Equals(new TextMessage("alarm.limitCriticalLow", "numeric.abpMean")),
            "equality compares keys and arguments, distinguishing literals from nested keys");
        var notice = new MonitorNotice("abp-mean-low", MonitorNoticeLevel.Critical, "ABP 平均压 极低") { Message = message };
        Check.That(notice == notice with { Message = new("alarm.limitCriticalLow", new TextMessage("numeric.abpMean")) },
            "notices carrying equal messages stay equal records");
        bool rejected = false;
        try { _ = new TextMessage("measurement.noData", 3); } catch (ArgumentException) { rejected = true; }
        Check.That(rejected, "arguments are limited to text and nested messages");
    }

    private static void MeasurementNoticesRenderTheirDefaultTextInChinese()
    {
        foreach (var source in Enum.GetValues<MeasurementSource>())
            foreach (var status in Enum.GetValues<WaveformMeasurementStatus>().Where(status => status != WaveformMeasurementStatus.Valid))
                foreach (var fault in Enum.GetValues<MeasurementTechnicalFault>())
                {
                    var display = MeasurementDisplay.Resolve(source, status, "75", fault);
                    Check.That(display.TopNoticeMessage is { } message && message.Render(Chinese) == display.TopNotice &&
                        !message.Render(English).Contains("[[", StringComparison.Ordinal),
                        $"measurement notice message matches its default text: {source} {status} {fault}");
                }
        Check.That(MeasurementDisplay.Resolve(MeasurementSource.Pressure, WaveformMeasurementStatus.NoData, null).TopNoticeMessage!.Render(English) == "Pressure: no data",
            "the pressure source label is translated");
    }

    private static void LimitNoticesRenderTheirDefaultTextInChinese()
    {
        foreach (var descriptor in MeasuredLimitNotice.Descriptors.Prepend(MeasuredLimitNotice.HeartRateDescriptor).Prepend(MeasuredLimitNotice.SpO2Descriptor))
        {
            Check.That(descriptor.LabelMessage.Render(Chinese) == descriptor.Label, $"descriptor label key matches: {descriptor.Id}");
            foreach (bool low in new[] { false, true })
                foreach (bool critical in new[] { false, true })
                {
                    var notice = MeasuredLimitNotice.CreateNotice(descriptor, low, critical);
                    Check.That(notice.Message!.Render(Chinese) == notice.Text && !notice.Message.Render(English).Contains("[[", StringComparison.Ordinal),
                        $"limit notice message matches its default text: {notice.Id}");
                }
        }
        var invalid = new MeasurementLimits(true, 5, 4, 3, 2);
        var snapshot = LiveWaveformMeasurements.CreateIllustration().Read(0);
        foreach (var numeric in MeasuredLimitNotice.Descriptors.Select(d => d.Numeric))
        {
            var notice = MeasuredLimitNotice.Evaluate(numeric, invalid, snapshot)!;
            Check.That(notice.Message!.Render(Chinese) == notice.Text, $"invalid settings message matches: {notice.Id}");
        }
        var spo2 = SpO2LimitNotice.Evaluate(true, 1, 2, snapshot.SpO2)!;
        var absence = NoExpirationNotice.Evaluate(true, 1, 0, null)!;
        Check.That(spo2.Message!.Render(Chinese) == spo2.Text && absence.Message!.Render(Chinese) == absence.Text,
            "SpO₂ and CO₂ settings messages match their default text");
    }
}
