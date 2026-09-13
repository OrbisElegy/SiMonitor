// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Infrastructure.Presentation;

namespace Monitor.Desktop;

internal static class DesktopSmokeChecks
{
    public static void VerifyPublicationStates(MainWindow window)
    {
        foreach (CapturedRecordSvgStatus status in new[] { CapturedRecordSvgStatus.Refreshing, CapturedRecordSvgStatus.Denied,
            CapturedRecordSvgStatus.Cancelled, CapturedRecordSvgStatus.Failed, CapturedRecordSvgStatus.Withdrawn })
        {
            CapturedRecordSvgPublication publication = new(status, "DesktopSmoke.Status", null);
            window.ApplyPublication(publication);
            if (!window.HasNoRecordContent || !ReferenceEquals(window.CurrentPublication, publication))
            { throw new InvalidOperationException("Unavailable publication retained native content."); }
        }
        try
        {
            window.ApplyPublication(new(CapturedRecordSvgStatus.Ready, "SvgPresentation.Ready", null));
            throw new InvalidOperationException("Incomplete ready publication accepted.");
        }
        catch (ArgumentException)
        {
            if (!window.HasNoRecordContent || window.CurrentPublication is not null)
            { throw new InvalidOperationException("Invalid publication retained native state."); }
        }
        window.ApplyPublication(new(CapturedRecordSvgStatus.NotRendered, "SvgPresentation.NotRendered", null));
        if (!window.HasUnloadedRecordState)
        { throw new InvalidOperationException("Unloaded state did not recover."); }
        Console.WriteLine("ok: native publication withdrawal, rejection and recovery");
    }
}
