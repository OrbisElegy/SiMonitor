// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Localization;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

// Shared alarm wording for limit editors and event sounds.
internal static class AlarmText
{
    // Critical low, warning low, warning high, critical high.
    internal static readonly string[] BoundaryKeys =
        ["alarm.boundaryCriticalLow", "alarm.boundaryWarningLow", "alarm.boundaryWarningHigh", "alarm.boundaryCriticalHigh"];

    internal static TextMessage ConditionLabel(MeasurementLimitDescriptor descriptor, bool low) =>
        new(low ? "alarm.lowLimitLabel" : "alarm.highLimitLabel", descriptor.LabelMessage);

    // Descriptor units are symbols except the Chinese per-minute unit.
    internal static string Unit(ITextLocalizer text, MeasurementLimitDescriptor descriptor) =>
        descriptor.Unit == "次/分" ? text.GetString("unit.perMinute") : descriptor.Unit;
}
