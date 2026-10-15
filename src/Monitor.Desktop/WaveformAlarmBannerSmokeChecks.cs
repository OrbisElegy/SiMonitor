// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Monitor.Application.Localization;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class WaveformAlarmBannerSmokeChecks
{
    private static readonly int[] BedsideChannels = [0, 2, 1];
    private static readonly int[] ReorderedChannels = [1, 0, 2];
    private static readonly int[] DuplicateChannels = [0, 0, 2, 1];
    private static readonly int[] WithoutEcgChannels = [2, 1];
    private static readonly int[] AllChannels = [0, 1, 2, 3, 4, 5, 6];
    private static readonly Color NeutralColor = Color.Parse("#34373B");
    private static readonly Color CriticalColor = Color.Parse("#B51F2C");

    internal static void Verify()
    {
        VerifyIndependentChannels();
        VerifyNumericSources();
        VerifyRotationAndLayoutChanges();
        VerifyAttentionAndLocalization();
        VerifyProjectedRecovery();
        VerifyLongText();
    }

    private static void VerifyIndependentChannels()
    {
        var fixture = new Fixture(BedsideChannels);
        MonitorNotice[] notices =
        [
            Notice("hr-high", MonitorNumeric.HeartRate, MonitorNoticeLevel.Warning),
            Notice("spo2-low", MonitorNumeric.SpO2, MonitorNoticeLevel.Critical),
            Notice("resp-sensor", MonitorNumeric.RespirationRate, MonitorNoticeLevel.Info),
            new("global", MonitorNoticeLevel.Critical, "Global device status")
        ];
        fixture.Banners.Refresh(notices, 0, null, true);
        fixture.Layout();
        var ecg = fixture.Row(0);
        var pleth = fixture.Row(2);
        var respiration = fixture.Row(1);
        Require(fixture.Banners.Rows.All(row => row.Region.IsVisible) && ecg.Notice?.Id == "hr-high" &&
            pleth.Notice?.Id == "spo2-low" && respiration.Notice?.Id == "resp-sensor",
            "three waveform alarms remain visible simultaneously despite an unrelated global Critical");
        Require(ColorOf(ecg.Region.Background) == ColorOf(fixture.Skin.WarningBrush) && ColorOf(ecg.Text.Foreground) == Colors.Black &&
            ColorOf(pleth.Region.Background) == CriticalColor && ColorOf(pleth.Text.Foreground) == Colors.White &&
            ColorOf(respiration.Region.Background) == NeutralColor && ColorOf(respiration.Text.Foreground) == Colors.White,
            "each channel uses its own severity and the selected skin's warning color");
        fixture.Banners.Refresh(notices, 750_000_000, null, true);
        Require(fixture.Banners.Rows.All(row => row.Region.IsVisible && ColorOf(row.Region.Background) == NeutralColor) &&
            ecg.Text.Text == "hr-high" && respiration.Text.Text == "resp-sensor",
            "the off phase keeps every channel's text readable, including technical information");
        fixture.Banners.Refresh(notices, 1_000_000_000, null, true);
        Require(ColorOf(ecg.Region.Background) == ColorOf(fixture.Skin.WarningBrush) &&
            ColorOf(pleth.Region.Background) == CriticalColor && pleth.Text.Text!.Contains("00:01", StringComparison.Ordinal),
            "the simulation clock drives both the repeating blink and each channel's elapsed time");
        fixture.Banners.Refresh(notices, 1_000_000_000, null, true);
        Require(pleth.Text.Text!.Contains("00:01", StringComparison.Ordinal), "refreshing a paused simulation never advances elapsed time");
        fixture.Banners.Refresh([notices[3]], 2_000_000_000, null, true);
        Require(fixture.Banners.Rows.All(row => !row.Region.IsVisible && row.Notice is null && row.Text.Text == ""),
            "a global notice without a numeric source cannot occupy a cleared waveform banner");
    }

    private static void VerifyNumericSources()
    {
        var fixture = new Fixture(AllChannels);
        MonitorNotice[] notices =
        [
            Notice("ecg-rhythm", MonitorNumeric.HeartRate, MonitorNoticeLevel.Warning),
            Notice("resp", MonitorNumeric.RespirationRate, MonitorNoticeLevel.Warning),
            Notice("spo2", MonitorNumeric.SpO2, MonitorNoticeLevel.Warning),
            Notice("abp", MonitorNumeric.AbpMean, MonitorNoticeLevel.Warning),
            Notice("etco2", MonitorNumeric.EtCo2, MonitorNoticeLevel.Warning),
            Notice("pa", MonitorNumeric.PaMean, MonitorNoticeLevel.Warning),
            Notice("cvp", MonitorNumeric.CvpMean, MonitorNoticeLevel.Warning)
        ];
        fixture.Banners.Refresh(notices, 0, null, true);
        Require(fixture.Row(0).Notice?.Id == "ecg-rhythm" && fixture.Row(1).Notice?.Id == "resp" &&
            fixture.Row(2).Notice?.Id == "spo2" && fixture.Row(3).Notice?.Id == "abp" &&
            fixture.Row(4).Notice?.Id == "etco2" && fixture.Row(5).Notice?.Id == "pa" && fixture.Row(6).Notice?.Id == "cvp",
            "numeric source metadata routes all supported waveform channels");
        MonitorNotice[] secondary =
        [
            Notice("pulse-rate", MonitorNumeric.PulseRate, MonitorNoticeLevel.Notice),
            Notice("co2-rate", MonitorNumeric.Co2RespirationRate, MonitorNoticeLevel.Warning)
        ];
        fixture.Banners.Refresh(secondary, 1_000_000_000, null, true);
        Require(fixture.Row(2).Notice?.Id == "pulse-rate" && fixture.Row(4).Notice?.Id == "co2-rate" &&
            fixture.Banners.Rows.Count(row => row.Region.IsVisible) == 2,
            "pulse rate belongs to PLETH and CO2 respiration rate belongs to CO2, without leaking into RESP");
        fixture.Banners.Refresh(secondary, 1_000_000_000, null, false);
        Require(fixture.Row(2).Region.IsVisible && ColorOf(fixture.Row(2).Region.Background) == NeutralColor &&
            ColorOf(fixture.Row(4).Region.Background) == ColorOf(fixture.Skin.WarningBrush),
            "the Notice color preference keeps the indication while preserving unrelated Warning highlighting");
    }

    private static void VerifyRotationAndLayoutChanges()
    {
        var fixture = new Fixture(BedsideChannels);
        MonitorNotice[] notices =
        [
            Notice("hr-a", MonitorNumeric.HeartRate, MonitorNoticeLevel.Warning),
            Notice("hr-b", MonitorNumeric.HeartRate, MonitorNoticeLevel.Warning),
            Notice("spo2", MonitorNumeric.SpO2, MonitorNoticeLevel.Critical),
            Notice("resp", MonitorNumeric.RespirationRate, MonitorNoticeLevel.Info)
        ];
        fixture.Banners.Refresh(notices, 0, null, true);
        fixture.Layout();
        Require(fixture.Row(0).Notice?.Id == "hr-a", "the first ECG warning has its own initial turn");
        fixture.SetChannels(ReorderedChannels);
        fixture.Banners.Refresh(notices, 3_000_000_000, null, true);
        fixture.Layout();
        Require(fixture.Banners.Rows.Select(row => row.Channel).SequenceEqual(ReorderedChannels) &&
            fixture.Row(0).Notice?.Id == "hr-a" && fixture.Row(2).Text.Text!.Contains("00:03", StringComparison.Ordinal),
            "reordering moves alarm rows without resetting the independent rotation or Critical clock");
        VerifyHeaderGeometry(fixture);
        fixture.SetChannels(DuplicateChannels);
        fixture.Banners.Refresh(notices, 6_000_000_000, null, true);
        fixture.Layout();
        Require(fixture.Banners.Rows.Where(row => row.Channel == 0).All(row => row.Notice?.Id == "hr-b") &&
            fixture.Row(2).Text.Text!.Contains("00:06", StringComparison.Ordinal) && fixture.Row(1).Notice?.Id == "resp",
            "both duplicate ECG rows share the next turn while PLETH and RESP remain independently visible");
        VerifyHeaderGeometry(fixture);
        fixture.SetChannels(WithoutEcgChannels);
        fixture.Banners.Refresh(notices, 7_000_000_000, null, true);
        Require(fixture.Banners.Rows.All(row => row.Channel != 0), "removing a waveform removes its banner controls");
        fixture.SetChannels(BedsideChannels);
        fixture.Banners.Refresh(notices, 8_000_000_000, null, true);
        fixture.Layout();
        Require(fixture.Row(0).Notice?.Id == "hr-a" && fixture.Row(2).Text.Text!.Contains("00:08", StringComparison.Ordinal),
            "a removed channel starts fresh when readded while retained channels preserve their clocks");
    }

    private static void VerifyAttentionAndLocalization()
    {
        var fixture = new Fixture(BedsideChannels);
        var original = Notice("ecg-asystole", MonitorNumeric.HeartRate, MonitorNoticeLevel.Critical) with
        { Message = new TextMessage("alarm.ecg.asystole") };
        MonitorNotice[] active = [original];
        var state = new AlarmAttentionSnapshot(original.Id, 1, new(original.Id, 1), original.Level,
            AlarmAttentionState.ActiveUnacknowledged, AlarmLatchingMode.UntilAcknowledged);
        AlarmAttentionSnapshot? AttentionFor(string id) => id == original.Id ? state : null;
        fixture.Banners.Refresh(active, 0, AttentionFor, true);
        fixture.Banners.Refresh(active, 3_000_000_000, AttentionFor, true);
        string english = fixture.Row(0).Text.Text!;
        fixture.Localization.Select("zh-CN");
        fixture.Banners.Refresh(active, 3_000_000_000, AttentionFor, true);
        Require(fixture.Row(0).Text.Text != english &&
            fixture.Row(0).Text.Text!.Contains(fixture.Localization.Get("alarm.ecg.asystole"), StringComparison.Ordinal) &&
            fixture.Row(0).Text.Text!.Contains("00:03", StringComparison.Ordinal) && fixture.Row(0).Notice?.Id == original.Id,
            "language changes rerender the same active episode without restarting its elapsed time");
        state = state with { State = AlarmAttentionState.ActiveAcknowledged, Revision = 2 };
        var acknowledged = original with { Message = new TextMessage("alarm.acknowledged", original.Message!) };
        MonitorNotice[] acknowledgedSet = [acknowledged];
        fixture.Banners.Refresh(acknowledgedSet, 4_000_000_000, AttentionFor, true);
        string acknowledgedText = fixture.Row(0).Text.Text!;
        Require(acknowledgedText.Contains(acknowledged.Message!.Render(fixture.Localization.Current), StringComparison.Ordinal) &&
            ColorOf(fixture.Row(0).Region.Background) == NeutralColor, "acknowledged wording survives projection and its banner stops highlighting");
        fixture.Banners.Refresh(acknowledgedSet, 4_750_000_000, AttentionFor, true);
        Require(fixture.Row(0).Text.Text == acknowledgedText && ColorOf(fixture.Row(0).Region.Background) == NeutralColor,
            "acknowledged text and neutral color remain stable through the off phase");
        state = state with { State = AlarmAttentionState.RecoveredUnacknowledged, Revision = 3 };
        var recovered = original with { Message = new TextMessage("alarm.recoveredUnacknowledged", original.Message!) };
        MonitorNotice[] recoveredSet = [recovered];
        fixture.Banners.Refresh(recoveredSet, 5_000_000_000, AttentionFor, true);
        Require(fixture.Row(0).Region.IsVisible && fixture.Row(0).Text.Text == recovered.Message!.Render(fixture.Localization.Current) &&
            ColorOf(fixture.Row(0).Region.Background) == NeutralColor,
            "a retained recovered indication keeps its wording without an active Critical timer or blinking color");
        fixture.Banners.Refresh(recoveredSet, 5_750_000_000, AttentionFor, true);
        Require(ColorOf(fixture.Row(0).Region.Background) == NeutralColor, "recovered indications remain neutral in both blink phases");
    }

    private static void VerifyProjectedRecovery()
    {
        const long activeTime = 20_000_000_000;
        const long recoveredTime = activeTime + 200_000_000;
        var empty = LiveWaveformMeasurements.CreateIllustration().Read(0);
        var active = empty with
        {
            SampleTimeNs = activeTime,
            HeartRate = new(WaveformMeasurementStatus.Valid, 190000, activeTime),
            EcgMonitoring = empty.EcgMonitoring with
            { Status = WaveformMeasurementStatus.Valid, Learning = false, ActiveConditions = EcgMonitoringConditions.Asystole },
            Capnography = empty.Capnography with { Activity = new(WaveformMeasurementStatus.Valid, 0, null, activeTime) }
        };
        var recovered = active with
        {
            SampleTimeNs = recoveredTime,
            HeartRate = new(WaveformMeasurementStatus.Valid, 100000, recoveredTime),
            EcgMonitoring = active.EcgMonitoring with { ActiveConditions = EcgMonitoringConditions.None },
            Capnography = active.Capnography with { Activity = new(WaveformMeasurementStatus.Valid, 0, recoveredTime, recoveredTime) }
        };
        foreach (var (condition, numeric, channel) in new[]
        {
            ("hr-high", MonitorNumeric.HeartRate, 0),
            ("ecg-asystole", MonitorNumeric.HeartRate, 0),
            ("co2-no-expiration", MonitorNumeric.Co2RespirationRate, 4)
        })
        {
            var fixture = new Fixture(AllChannels);
            var alerts = new MonitorAlertSettings(fixture.Localization);
            alerts.NotificationSettings.Editors[condition].LatchUntilAcknowledged.IsChecked = true;
            var journal = alerts.AlarmLifecycles.Single(item => item.Conditions.Any(state => state.ConditionId == condition));
            AlarmAttentionSnapshot? AttentionFor(string id)
            {
                string conditionId = id.StartsWith(MonitorAlertSettings.RetainedNoticePrefix, StringComparison.Ordinal)
                    ? id[MonitorAlertSettings.RetainedNoticePrefix.Length..] : id;
                return alerts.AlarmLifecycles.SelectMany(item => item.Attention.Conditions)
                    .SingleOrDefault(state => state.ConditionId == conditionId);
            }
            var activeNotices = alerts.Notices(active).ToArray();
            fixture.Banners.Refresh(alerts.ProjectAttention(activeNotices), activeTime, AttentionFor, true);
            var recoveryNotices = alerts.Notices(recovered).ToArray();
            var projected = alerts.ProjectAttention(recoveryNotices).ToArray();
            var retained = projected.Single(notice => notice.Id == MonitorAlertSettings.RetainedNoticePrefix + condition);
            Require(retained.Numeric == numeric && !retained.Audible,
                "real recovery projection preserves the numeric source without making the retained indication audible: " + condition);
            fixture.Banners.Refresh(projected, recoveredTime, AttentionFor, true);
            var row = fixture.Row(channel);
            Require(row.Region.IsVisible && row.Notice?.Id == retained.Id &&
                row.Text.Text == retained.Message!.Render(fixture.Localization.Current) && ColorOf(row.Region.Background) == NeutralColor,
                "real recovered attention remains on its waveform with neutral color and no active elapsed timer: " + condition);
            var attention = journal.Attention.Conditions.Single(item => item.ConditionId == condition);
            Require(journal.Attention.Acknowledge(attention.Episode!, attention.Revision), "retained recovery accepts its current acknowledgement");
            fixture.Banners.Refresh(alerts.ProjectAttention(recoveryNotices), recoveredTime, AttentionFor, true);
            Require(!row.Region.IsVisible && row.Notice is null,
                "acknowledging actual recovered attention clears its waveform indication: " + condition);
        }
    }

    private static void VerifyLongText()
    {
        var fixture = new Fixture(BedsideChannels);
        string fullText = new('W', 400);
        MonitorNotice[] notices = [Notice("long-technical", MonitorNumeric.SpO2, MonitorNoticeLevel.Info) with { Text = fullText }];
        fixture.Banners.Refresh(notices, 0, null, true);
        fixture.Layout(420, 300);
        var row = fixture.Row(2);
        Require(row.Text.Text == fullText && Equals(ToolTip.GetTip(row.Region), fullText) &&
            AutomationProperties.GetName(row.Text) == fullText && AutomationProperties.GetName(row.Region) == fullText,
            "truncated technical text remains complete in tooltips and accessibility names");
        Require(row.Text.TextTrimming == TextTrimming.CharacterEllipsis && row.Text.TextWrapping == TextWrapping.NoWrap &&
            row.Region.Bounds.Width < 245 && row.Text.Bounds.Width <= row.Region.Bounds.Width &&
            row.Region.Bounds.Right <= 420 && row.Region.Bounds.Bottom <= 130,
            "long text fits a compact header without growing into the waveform trace or beyond the right edge");
    }

    private static void VerifyHeaderGeometry(Fixture fixture)
    {
        for (int index = 0; index < fixture.Banners.Rows.Count; index++)
        {
            var bounds = fixture.Banners.Rows[index].Region.Bounds;
            Require(bounds.Width > 0 && bounds.Height > 0 && bounds.Top >= index * 138 &&
                bounds.Bottom <= index * 138 + 30 && bounds.Right <= 670 && bounds.Left > 160,
                "each slot keeps its alarm beside its own labels and entirely above the waveform trace");
        }
    }

    private static MonitorNotice Notice(string id, MonitorNumeric numeric, MonitorNoticeLevel level) =>
        new(id, level, id) { Numeric = numeric };

    private static Color ColorOf(IBrush? brush) => (brush as ISolidColorBrush)?.Color ?? default;

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Waveform alarm banner: " + message); } }

    private sealed class Fixture
    {
        private MonitorDisplaySlot[] _slots = [];
        internal DesktopLocalization Localization { get; } = new("en");
        internal TestSkin Skin { get; } = new();
        internal WaveformAlarmBanners Banners { get; }

        internal Fixture(IReadOnlyList<int> channels)
        {
            SetChannels(channels);
            Banners = new(() => _slots, Skin, Localization);
        }

        internal void SetChannels(IReadOnlyList<int> channels) => _slots = channels.Select(channel =>
            new MonitorDisplaySlot(channel, true, MonitorDisplayConfiguration.ReferenceRange(channel))).ToArray();

        internal WaveformAlarmBannerRow Row(int channel) => Banners.Rows.Single(row => row.Channel == channel);

        internal void Layout(double width = 670, double? height = null)
        {
            var size = new Size(width, height ?? _slots.Length * 138);
            Banners.Measure(size);
            Banners.Arrange(new Rect(size));
        }
    }

    private sealed class TestSkin : IMonitorSkin
    {
        public MonitorChannels Channels => MonitorChannels.All;
        public string Id => "waveform-banner-check";
        public IBrush Background => Brushes.Black;
        public IBrush WarningBrush { get; } = Brush.Parse("#DDF12E");
        public IBrush ChannelBrush(int channel) => Brushes.Lime;
        public Control Compose(MonitorSkinContent content) => content.Waveforms;
    }
}
