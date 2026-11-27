// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;

namespace Monitor.Network;

internal sealed record ClassroomDeviceState(string? LayoutId, string? Ecg12ThemeId, long? AlarmPauseUntilSimTimeNs,
    string DefibTimingMode, int DefibEnergyJoules, NibpMeasurementState Cuff);

internal sealed class ClassroomControlDevice : IClassroomActionReducer
{
    public string ProfileId => "ClassroomBedsideControls@1";
    public JsonElement InitialState
    {
        get
        {
            var cuff = new NibpMeasurement();
            cuff.Advance(0, new ManualNibp(120, 80, 93));
            return Encode(new ClassroomDeviceState("FiveRows", "MonitorDarkGreen", null, "Async", 150, cuff.CaptureState()));
        }
    }

    public bool Supports(string actionId) => actionId is "alarm.audio_pause.start" or "alarm.audio_pause.resume" or
        "presentation.layout.select" or "presentation.ecg12_theme.select" or "nibp.measurement.start" or
        "nibp.measurement.cancel" or "nibp.auto.configure" or "therapy.defib.timing_mode.set" or "therapy.defib.energy.set";

    public JsonElement Reduce(JsonElement currentState, ClassroomActionIntent action, long simTimeNs, long safetyTimeNs)
    {
        ClassroomDeviceState previous = Decode(currentState);
        ClassroomDeviceState next;
        JsonElement p = action.Parameters;
        switch (action.ActionId)
        {
            case "presentation.layout.select":
                string? layout = p.GetProperty("selected_id").GetString();
                if (layout is not ("ThreeRows" or "FourRows" or "FiveRows" or "SevenRows")) { throw Denied(); }
                next = previous with { LayoutId = layout };
                break;
            case "presentation.ecg12_theme.select":
                string? theme = p.GetProperty("selected_id").GetString();
                if (theme is not ("MonitorDarkGreen" or "PaperGridBlack")) { throw Denied(); }
                next = previous with { Ecg12ThemeId = theme };
                break;
            case "alarm.audio_pause.start":
                next = previous with { AlarmPauseUntilSimTimeNs = checked(simTimeNs + p.GetProperty("duration_s").GetInt32() * 1_000_000_000L) };
                break;
            case "alarm.audio_pause.resume":
                next = previous with { AlarmPauseUntilSimTimeNs = null };
                break;
            case "therapy.defib.timing_mode.set":
                next = previous with { DefibTimingMode = p.GetProperty("timing_mode").GetString()! };
                break;
            case "therapy.defib.energy.set":
                JsonElement energy = p.GetProperty("energy");
                decimal joules = decimal.Parse(energy.GetProperty("mantissa_i64").GetString()!, CultureInfo.InvariantCulture);
                int exponent = energy.GetProperty("exponent10").GetInt32();
                for (int digit = 0; digit < Math.Abs(exponent); digit++) { joules = exponent < 0 ? joules / 10 : checked(joules * 10); }
                if (joules is < 1 or > 1000 || decimal.Truncate(joules) != joules ||
                    !DefibrillatorConfiguration.Default.EnergyStepsJoules.Contains((int)joules)) { throw Denied(); }
                next = previous with { DefibEnergyJoules = (int)joules };
                break;
            default:
                var cuff = NibpMeasurement.Restore(previous.Cuff);
                cuff.Advance(simTimeNs, previous.Cuff.Target);
                switch (action.ActionId)
                {
                    case "nibp.measurement.start" when cuff.Start(): break;
                    case "nibp.measurement.cancel" when cuff.Measuring: cuff.Stop(); break;
                    case "nibp.auto.configure":
                        bool enabled = p.GetProperty("enabled").GetBoolean();
                        int seconds = enabled ? p.GetProperty("interval_s").GetInt32() : cuff.AutoIntervalMinutes * 60;
                        if (seconds % 60 != 0 || !NibpMeasurement.AutoIntervalsMinutes.Contains(seconds / 60)) { throw Denied(); }
                        cuff.ConfigureAuto(enabled, seconds / 60);
                        break;
                    default: throw Denied();
                }
                next = previous with { Cuff = cuff.CaptureState() };
                break;
        }
        return Encode(next);
    }

    internal static JsonElement ConfigureDisplay(JsonElement state, JsonElement settings) =>
        Encode(Decode(state) with
        {
            LayoutId = settings.GetProperty("layout_id").GetString(),
            Ecg12ThemeId = settings.GetProperty("ecg12_theme_id").GetString()
        });

    internal static JsonElement? Advance(JsonElement state, long simTimeNs)
    {
        ClassroomDeviceState value = Decode(state);
        NibpMeasurementState cuff = value.Cuff;
        bool due = cuff.StartedAtNs is { } started && simTimeNs - started >= NibpMeasurement.MeasurementDurationNs ||
            cuff.StartedAtNs is null && cuff.AutoEnabled && cuff.NextMeasurementAtNs <= simTimeNs ||
            value.AlarmPauseUntilSimTimeNs <= simTimeNs;
        if (!due) { return null; }
        var active = NibpMeasurement.Restore(cuff);
        active.Advance(simTimeNs, cuff.Target);
        return Encode(value with
        {
            AlarmPauseUntilSimTimeNs = value.AlarmPauseUntilSimTimeNs > simTimeNs ? value.AlarmPauseUntilSimTimeNs : null,
            Cuff = active.CaptureState()
        });
    }

    internal static JsonElement Project(JsonElement state, long simTimeNs)
    {
        ClassroomDeviceState value = Decode(state);
        var cuff = NibpMeasurement.Restore(value.Cuff);
        cuff.Advance(Math.Max(simTimeNs, value.Cuff.SimTimeNs), value.Cuff.Target);
        // The target becomes a learner-visible reading only when the cuff has finished its cycle.
        return JsonSerializer.SerializeToElement(new
        {
            value.LayoutId,
            value.Ecg12ThemeId,
            value.DefibTimingMode,
            value.DefibEnergyJoules,
            AlarmPauseUntilSimTimeNs = value.AlarmPauseUntilSimTimeNs > simTimeNs ? value.AlarmPauseUntilSimTimeNs : null,
            NibpPhase = cuff.Phase.ToString(),
            cuff.AutoEnabled,
            cuff.AutoIntervalMinutes,
            cuff.Reading,
            cuff.MeasuredAtNs
        }, ClassroomControlWire.Json);
    }

    private static ClassroomActionDeniedException Denied() => new("DEVICE_ACTION_UNAVAILABLE");
    private static JsonElement Encode(ClassroomDeviceState state) => JsonSerializer.SerializeToElement(state, ClassroomControlWire.Json);
    private static ClassroomDeviceState Decode(JsonElement state) => state.Deserialize<ClassroomDeviceState>(ClassroomControlWire.Json)!;
}
