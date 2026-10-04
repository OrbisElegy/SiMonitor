// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

internal sealed class BoundaryConfirmation
{
    public bool Active { get; private set; }
    private long? _pendingSinceNs;

    public void Update(bool breached, long now, BoundaryConfirmationTiming timing)
    {
        if (breached == Active)
        {
            _pendingSinceNs = null;
            return;
        }
        _pendingSinceNs ??= now;
        long delay = (long)(breached ? timing.TriggerMilliseconds : timing.RecoveryMilliseconds) * 1_000_000;
        if (now - _pendingSinceNs.Value < delay) { return; }
        Active = breached;
        _pendingSinceNs = null;
    }

    public void Reset()
    {
        Active = false;
        _pendingSinceNs = null;
    }
}
