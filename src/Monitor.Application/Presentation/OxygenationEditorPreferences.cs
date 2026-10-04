// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Application.Presentation;

// Optional extension: old saved generator dictionaries keep their exact schema.
public sealed record OxygenationEditorPreferences(bool Realtime, int TidalVolumeMicrolitersBtps,
    int DeadSpaceMicrolitersBtps, int InspiredOxygenMillionths, bool AirwayOpen)
{
    public OxygenationPatientPreferences? Patient { get; init; }
    public decimal OxygenDemandMultiplier { get; init; } = 1;
    public VentilationTransportPlan CreateVentilation() => new(TidalVolumeMicrolitersBtps,
        DeadSpaceMicrolitersBtps, InspiredOxygenMillionths, AirwayOpen);
    public void Validate()
    {
        RealtimeOxygenationSource.ValidateVentilation(CreateVentilation());
        OxygenReservoirModel.ValidateOxygenDemandMultiplier(OxygenDemandMultiplier);
        Patient?.Validate();
    }
    public RealtimeOxygenationConfiguration CreateConfiguration()
    {
        Validate();
        return new(CreateVentilation(), Patient?.Resolved.Parameters ?? new())
        { OxygenDemandMultiplier = OxygenDemandMultiplier };
    }
}
