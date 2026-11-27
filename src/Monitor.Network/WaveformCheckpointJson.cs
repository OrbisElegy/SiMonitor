// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;
using Monitor.Simulation.Physiology;

namespace Monitor.Network;

internal static class WaveformCheckpointJson
{
    internal static readonly JsonSerializerOptions Options = new() { Converters = { new RateAdjustmentConverter() } };
    private sealed record ArchivedRate(int HeartRateBpm, int? AtrialRateBpm, string SeedHex, int VariationPermille);
    private sealed class RateAdjustmentConverter : JsonConverter<CardiacRateAdjustment>
    {
        public override CardiacRateAdjustment Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var rate = JsonSerializer.Deserialize<ArchivedRate>(ref reader, options) ?? throw new JsonException("Missing cardiac rate.");
            return new CardiacRateAdjustment(rate.HeartRateBpm, rate.AtrialRateBpm, rate.SeedHex, rate.VariationPermille);
        }
        public override void Write(Utf8JsonWriter writer, CardiacRateAdjustment value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new ArchivedRate(value.Rate.HeartRateBpm, value.AtrialRate?.HeartRateBpm,
                value.Rate.SeedHex, value.Rate.VariationPermille), options);
    }

    internal static PhysiologyWaveformGroupState Normalize(PhysiologyWaveformGroupState patient)
    {
        var channels = patient.Channels.ToArray();
        if (channels.Length == 0) { throw new InvalidDataException("Empty waveform checkpoint."); }
        // The group has one clinical plan. Record equality in the live engine also
        // uses identity of prepared rate schedules; JSON does not retain those aliases.
        var common = channels[0].Generator.Timeline.Plan;
        JsonElement canonical = JsonSerializer.SerializeToElement(common, Options);
        for (int i = 1; i < channels.Length; i++)
        {
            var g = channels[i].Generator;
            if (!JsonElement.DeepEquals(canonical, JsonSerializer.SerializeToElement(g.Timeline.Plan, Options)))
            { throw new InvalidDataException("Archived channels have different clinical plans."); }
            channels[i] = channels[i] with { Generator = g with { Timeline = g.Timeline with { Plan = common } } };
        }
        return patient with { Channels = channels };
    }
}
