// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Acquisition;

// Quantized source-time evidence shared by live consumers and recording batches.
public readonly record struct AcquiredSignalSample(
    string ProfileId,
    ulong StreamEpoch,
    ulong SampleIndex,
    long SourceSimTimeNs,
    short NormalizedValue,
    uint QualityFlags);
