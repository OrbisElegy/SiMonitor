// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Simulation.Physiology;

namespace Monitor.Infrastructure.Preferences;

public sealed record DisplayPreferences(MonitorDisplayConfiguration Display, int PaperLayout, MonitorAlarmPreferences? Alarms = null, MonitorSoundPreferences? Sound = null, MonitorGeneratorPreferences? Generator = null, bool MeasurementMillimeters = false, DefibrillatorConfiguration? Defibrillator = null)
{
    public PatientTimelineLibrary? PatientTimelines { get; init; }
    public bool TherapyControlsVisible { get; init; }
    public EcgLead MonitorEcgLead { get; init; } = EcgLead.II;
    public MonitorDisplayConfiguration? UnlockedSkinDisplay { get; init; }
    public bool CustomDefibrillatorEnergySteps { get; init; }
}

// Local preferences and generator inputs; no runtime waveform or playback state.
public sealed class DisplayPreferenceStore(string path)
{
    private const int MaximumBytes = 8_388_608;
    private static readonly JsonSerializerOptions Options = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16, WriteIndented = true, RespectRequiredConstructorParameters = true };
    private sealed record Slot([property: JsonRequired] int Channel, [property: JsonRequired] bool Automatic,
        [property: JsonRequired] double Minimum, [property: JsonRequired] double Maximum, [property: JsonRequired] int Speed);
    private sealed record Layout([property: JsonRequired] MonitorSkin Skin, [property: JsonRequired] Slot[] Slots);
    private sealed class Document
    {
        public required int Version { get; init; }
        public PatientTimelineLibrary? PatientTimelines { get; init; }
        public required MonitorSkin Skin { get; init; }
        public required Slot[] Slots { get; init; }
        public required int PaperLayout { get; init; }
        public bool TherapyControlsVisible { get; init; }
        public EcgLead MonitorEcgLead { get; init; } = EcgLead.II;
        public Layout? UnlockedSkinDisplay { get; init; }
        public bool MeasurementMillimeters { get; init; }
        public MonitorAlarmPreferences? Alarms { get; init; }
        public MonitorSoundPreferences? Sound { get; init; }
        public DefibrillatorConfiguration? Defibrillator { get; init; }
        private bool _customDefibrillatorEnergySteps;
        public bool CustomDefibrillatorEnergySteps
        {
            get => _customDefibrillatorEnergySteps;
            init { _customDefibrillatorEnergySteps = value; HasCustomDefibrillatorEnergySteps = true; }
        }
        [JsonIgnore]
        public bool HasCustomDefibrillatorEnergySteps { get; private set; }
        private MonitorGeneratorPreferences? _generator;
        public MonitorGeneratorPreferences? Generator
        {
            get => _generator;
            init { _generator = value; HasGenerator = true; }
        }
        [JsonIgnore]
        public bool HasGenerator { get; private set; }
    }
    public DisplayPreferences Load(out bool rejected)
    {
        rejected = false;
        try
        {
            using var stream = File.OpenRead(path);
            byte[] bytes = new byte[MaximumBytes + 1];
            int count = 0, read;
            while (count < bytes.Length && (read = stream.Read(bytes, count, bytes.Length - count)) != 0) { count += read; }
            if (count > MaximumBytes) { throw new ArgumentException("Preferences.TooLarge"); }
            var data = JsonSerializer.Deserialize<Document>(bytes.AsSpan(0, count), Options);
            if (data is null || data.Version is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 20) || data.PaperLayout is < 0 or > 1 || data.Slots is null || data.Slots.Any(s => s is null))
            { throw new ArgumentException("Preferences.InvalidDocument"); }
            if (!Enum.IsDefined(data.MonitorEcgLead)) { throw new ArgumentException("Preferences.InvalidEcgLead"); }
            if (data.Version >= 2 && data.Alarms is null) { throw new ArgumentException("Preferences.MissingAlarms"); }
            var alarms = data.Alarms;
            // Retired presentation-only SVT policy may be present in saved
            // configurations. Keep all remaining preferences and reject other
            // unknown/malformed entries as before.
            if (alarms?.Notifications is { } notifications && notifications.TryGetValue("ecg-svt", out var retiredSvt))
            {
                if (retiredSvt is null) { throw new ArgumentException("AlarmNotification.InvalidCondition"); }
                retiredSvt.Validate();
                alarms = alarms with { Notifications = notifications.Where(p => p.Key != "ecg-svt").ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) };
            }
            alarms?.Validate();
            if (data.Version >= 3 && data.Sound is null) { throw new ArgumentException("Preferences.MissingSound"); }
            data.Sound?.Validate();
            if (data.Version >= 5 && !data.HasGenerator) { throw new ArgumentException("Preferences.MissingGenerator"); }
            if (data.Version == 4 && data.Generator is null) { throw new ArgumentException("Preferences.MissingGenerator"); }
            data.Generator?.Validate();
            var generator = data.Generator;
            if (data.Version < 18 && generator is not null)
            { generator = generator with { AedElectricalConversions = EcgElectricalTherapy.Snapshot(generator.ElectricalConversions) }; }
            if ((data.Version >= 16 || data.CustomDefibrillatorEnergySteps) && data.Defibrillator is null)
            { throw new ArgumentException("Preferences.MissingDefibrillator"); }
            var defibrillator = data.Defibrillator?.Snapshot() ?? DefibrillatorConfiguration.Default;
            bool customEnergySteps = data.HasCustomDefibrillatorEnergySteps ? data.CustomDefibrillatorEnergySteps :
                data.Defibrillator is not null && !defibrillator.EnergyStepsJoules.SequenceEqual(DefibrillatorConfiguration.Default.EnergyStepsJoules);
            var display = RestoreLayout(new(data.Skin, data.Slots));
            var unlocked = data.UnlockedSkinDisplay is { } layout ? RestoreLayout(layout) : null;
            return new(display, data.PaperLayout, alarms, data.Sound, generator, data.MeasurementMillimeters, defibrillator)
            {
                PatientTimelines = data.PatientTimelines?.Snapshot(),
                TherapyControlsVisible = data.TherapyControlsVisible,
                MonitorEcgLead = data.MonitorEcgLead,
                UnlockedSkinDisplay = unlocked,
                CustomDefibrillatorEnergySteps = customEnergySteps
            };
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { rejected = true; }
        return new(MonitorDisplayConfiguration.Default(MonitorSkin.FourRows), 0);
    }
    public bool Save(DisplayPreferences preferences)
    {
        if (preferences.PaperLayout is < 0 or > 1) { throw new ArgumentException("Preferences.InvalidPaperLayout"); }
        if (!Enum.IsDefined(preferences.MonitorEcgLead)) { throw new ArgumentException("Preferences.InvalidEcgLead"); }
        if (preferences.CustomDefibrillatorEnergySteps && preferences.Defibrillator is null)
        { throw new ArgumentException("Preferences.MissingDefibrillator"); }
        var alarms = preferences.Alarms ?? MonitorAlarmPreferences.Default;
        alarms.Validate();
        var sound = preferences.Sound ?? MonitorSoundPreferences.Default; sound.Validate();
        preferences.Generator?.Validate();
        var display = CaptureLayout(preferences.Display);
        var unlocked = preferences.UnlockedSkinDisplay is { } layout ? CaptureLayout(layout) : null;
        var data = new Document
        {
            Version = 20,
            PatientTimelines = preferences.PatientTimelines?.Snapshot(),
            TherapyControlsVisible = preferences.TherapyControlsVisible,
            MonitorEcgLead = preferences.MonitorEcgLead,
            UnlockedSkinDisplay = unlocked,
            CustomDefibrillatorEnergySteps = preferences.CustomDefibrillatorEnergySteps,
            Defibrillator = (preferences.Defibrillator ?? DefibrillatorConfiguration.Default).Snapshot(),
            Generator = preferences.Generator,
            Sound = sound,
            Alarms = alarms,
            Skin = display.Skin,
            PaperLayout = preferences.PaperLayout,
            MeasurementMillimeters = preferences.MeasurementMillimeters,
            Slots = display.Slots
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(data, Options);
        if (bytes.Length > MaximumBytes) { return false; }
        string fullPath = Path.GetFullPath(path);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static MonitorDisplayConfiguration RestoreLayout(Layout layout)
    {
        if (layout.Slots is null || layout.Slots.Any(slot => slot is null)) { throw new ArgumentException("Preferences.InvalidDisplay"); }
        return new(layout.Skin, layout.Slots.Select(slot => new MonitorDisplaySlot(slot.Channel, slot.Automatic,
            new(slot.Minimum, slot.Maximum), slot.Speed)).ToArray());
    }

    private static Layout CaptureLayout(MonitorDisplayConfiguration display)
    {
        ArgumentNullException.ThrowIfNull(display);
        var snapshot = new MonitorDisplayConfiguration(display.Skin, display.Slots);
        return new(snapshot.Skin, snapshot.Slots.Select(slot => new Slot(slot.Channel, slot.Automatic,
            slot.Range.Minimum, slot.Range.Maximum, slot.SpeedTenthsMmPerSecond)).ToArray());
    }
}
