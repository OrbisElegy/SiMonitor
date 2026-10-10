// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Infrastructure.Preferences;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PacingPermissionSpecifications
{
    internal static Specification[] All =>
    [
        new(nameof(PacingPermissionRejectsWithoutChangingTheSession), PacingPermissionRejectsWithoutChangingTheSession),
        new(nameof(PacingPermissionsPersistIndependentlyAndMigrateDisabled), PacingPermissionsPersistIndependentlyAndMigrateDisabled)
    ];

    private static void PacingPermissionRejectsWithoutChangingTheSession()
    {
        LocalMonitorPreviewSession Session(bool allowed, string template = "ecgTemplate.t000") =>
            new(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), true,
                electricalTherapy: new(template, ElectricalConversionSettings.Default) { PacingAllowed = allowed });
        void Reject(LocalMonitorPreviewSession session)
        {
            long now = session.SimulationTimeNs;
            var blocks = session.Blocks.ToArray();
            var measurements = session.Measurements;
            try { session.SchedulePacing(PacingIllustration.DualChamberDdd, new(90, 60)); }
            catch (InvalidOperationException error) when (error.Message == "Pacing.TemplateDisabled")
            {
                Check.That(session.SimulationTimeNs == now && session.Blocks.SequenceEqual(blocks) &&
                    session.Measurements == measurements && session.PendingSourceTimeNs is null && session.ActivePacing is null,
                    "denial leaves source, time, history and measurements unchanged");
                return;
            }
            throw new InvalidOperationException("Expected pacing permission rejection");
        }
        var denied = Session(false);
        for (int i = 0; i < 25; i++) { denied.Advance(200_000_000); }
        Reject(denied);
        Reject(new(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default()));
        Reject(Session(true, "unregistered-template"));
        denied.ScheduleSource(Session(true), 1_000_000_000);
        Check.That(!denied.PacingAllowed, "a pending permission does not change the active source");
        for (int i = 0; i < 10; i++) { denied.Advance(200_000_000); }
        Check.That(denied.PacingAllowed, "permission activates with the new source");
        denied.SchedulePacing(PacingIllustration.DualChamberDdd, new(90, 60));
        denied.Advance(200_000_000);
        Check.That(denied.ActivePacing == PacingIllustration.DualChamberDdd, "an explicitly enabled template permits pacing");
        denied.StopPacing();
        denied.Advance(200_000_000);
        denied.ScheduleSource(Session(false), 0);
        denied.Advance(200_000_000);
        Reject(denied);
    }

    private static void PacingPermissionsPersistIndependentlyAndMigrateDisabled()
    {
        var map = EcgPacingPermissions.TemplateIds.Select((id, index) => (id, value: index % 2 == 0)).ToDictionary(p => p.id, p => p.value);
        var generator = new MonitorGeneratorPreferences(0, "窦性参考", 0, 0, "", new Dictionary<string, decimal?>(),
            new Dictionary<string, bool>(), new Dictionary<string, int>())
        { PacingPermissions = EcgPacingPermissions.Snapshot(map) };
        map.Clear();
        Check.That(generator.PacingPermissions.Count == 180, "the permission bank owns its snapshot");
        string path = Path.Combine(Path.GetTempPath(), "pacing-permissions-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new DisplayPreferenceStore(path);
            Check.That(store.Save(new(MonitorDisplayConfiguration.Default(), 0, Generator: generator)), "save every template permission");
            var loaded = store.Load(out bool rejected).Generator!;
            Check.That(!rejected && loaded.PacingPermissions.OrderBy(p => p.Key).SequenceEqual(generator.PacingPermissions.OrderBy(p => p.Key)),
                "all per-template flags survive reloading independently");
            var document = JsonNode.Parse(File.ReadAllText(path))!;
            Check.That(document["Version"]!.GetValue<int>() == 18, "device configuration schema uses version 18");
            document["Version"] = 14;
            document["Generator"]!.AsObject().Remove("PacingPermissions");
            File.WriteAllText(path, document.ToJsonString());
            Check.That(store.Load(out rejected).Generator!.PacingPermissions.Count == 0 && !rejected,
                "older preferences default to denied without losing other settings");
            document["Generator"]!["PacingPermissions"] = JsonNode.Parse("{\"ecgTemplate.t999\":true}");
            File.WriteAllText(path, document.ToJsonString());
            _ = store.Load(out rejected);
            Check.That(rejected, "unknown template identities cannot grant permissions");
        }
        finally { File.Delete(path); }
    }
}
