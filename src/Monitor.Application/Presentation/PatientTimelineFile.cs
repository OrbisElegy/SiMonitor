// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

// A portable authoring document contains the complete graph, never session state.
public sealed record PatientTimelineFile(string Id, string Name, PatientTimelineProgram Program)
{
    public PatientTimelineFile Snapshot()
    {
        if (!PatientTimelineDefinition.ValidId(Id) || string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Name.Any(char.IsControl))
        { throw new ArgumentException("PatientTimeline.InvalidFile"); }
        ArgumentNullException.ThrowIfNull(Program);
        return this with { Program = Program.Snapshot() };
    }
}

public sealed record PatientTimelineLibrary(string SelectedFileId, bool Enabled, IReadOnlyList<PatientTimelineFile> Files)
{
    public const int MaximumFiles = 16;

    public PatientTimelineLibrary Snapshot()
    {
        ArgumentNullException.ThrowIfNull(Files);
        if (Files.Count is < 1 or > MaximumFiles || Files.Any(file => file is null))
        { throw new ArgumentException("PatientTimeline.InvalidLibrary"); }
        var owned = Files.Select(file => file.Snapshot()).ToArray();
        if (owned.Select(file => file.Id).Distinct(StringComparer.Ordinal).Count() != owned.Length ||
            !owned.Any(file => file.Id == SelectedFileId))
        { throw new ArgumentException("PatientTimeline.InvalidLibrary"); }
        return this with { Files = Array.AsReadOnly(owned) };
    }
}
