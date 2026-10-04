// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Application.Presentation;

// Saves inputs, explicit/automatic selections and the resolved values/provenance.
// No live reservoirs, accumulated oxygen consumption or sampled history.
public sealed record OxygenationPatientPreferences(OxygenationPatientProfile Profile,
    OxygenationBaselineOverrides Overrides, OxygenationBaselineResolution Resolved)
{
    public void Validate()
    {
        if (Profile is null || Overrides is null || Resolved is null ||
            Resolved.ReferenceSetId != OxygenationPatientDefaults.ReferenceSetId ||
            Resolved != OxygenationPatientDefaults.Resolve(Profile, Overrides))
        { throw new ArgumentException("OxygenationDefaults.PreferenceMismatch"); }
    }
}
