// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Determinism;

public sealed class DeterminismArithmeticException(string reasonCode) : ArithmeticException(reasonCode)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed class DeterminismConfigurationException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}
