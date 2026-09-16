// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class AuthoredQrsSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.InfarctionInputs[2].IsChecked = true;
            window.IndependentComponentsInput.IsChecked = true;
            window.NecrosisShapeInput.SelectedIndex = 1;
            Click(window.ApplyEcgButton);
            string report = window.QrsMeasurementStatus.Text ?? "";
            if (!report.Contains("V3：达到所列 Q 条件；Q 时限 40 ms") ||
                ProjectedEcgDemoSource.LeadNames.Any(lead => !report.Contains(lead + "：")))
            { throw new InvalidOperationException("Projected QRS report lacks regional Q metrics or lead labels."); }
            Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
            var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
            window.QrsTemplateInput.Text = "bad"; Click(window.ApplyEcgButton);
            if (window.QrsMeasurementStatus.Text != report || !ReferenceEquals(trace, window.Trace) ||
                !ReferenceEquals(timer, window.ActiveTimer) || time != window.SimulationTimeNs)
            { throw new InvalidOperationException("Rejected input changed QRS report or live state."); }
            Click(window.ResetButton);
            if (window.QrsMeasurementStatus.Text != report) { throw new InvalidOperationException("Reset changed accepted QRS report."); }
            window.NecrosisShapeInput.SelectedIndex = 2; Click(window.ApplyEcgButton);
            if (!(window.QrsMeasurementStatus.Text ?? "").Contains("V3：QS（无 R）；Q 时限 — ms"))
            { throw new InvalidOperationException("QS was confused with Q/R qualification."); }
            window.QrsTemplateInput.Text = "0"; Click(window.ApplyEcgButton);
            if ((window.QrsMeasurementStatus.Text ?? "").Contains("V3：QS（无 R）"))
            { throw new InvalidOperationException("QRS metrics ignored template recovery."); }
            window.CardiacActivityInput.SelectedIndex = (int)CardiacActivity.AtrialOnly; Click(window.ApplyEcgButton);
            if (!(window.QrsMeasurementStatus.Text ?? "").Contains("无心室事件，不核验"))
            { throw new InvalidOperationException("Absent ventricular activity received an active QRS report."); }
            window.CardiacActivityInput.SelectedIndex = (int)CardiacActivity.AtrialAndVentricular;
            window.QtMethod.SelectedIndex = 1;
            window.QrsDurationInput.Text = "1"; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration.QrsDurationMilliseconds != 1 ||
                !(window.QrsMeasurementStatus.Text ?? "").Contains("1 ms 网格不足"))
            { throw new InvalidOperationException("Unresolved QRS blocked valid source configuration or was reported as flat."); }
            var source = ProjectedEcgDemoSource.Create();
            _ = AuthoredQrsSummary.Create(source, CardiacActivity.AtrialAndVentricular);
            var actual = source.AdvanceTo(200_000_000, 50, 1, 100);
            var reference = ProjectedEcgDemoSource.Create().AdvanceTo(200_000_000, 50, 1, 100);
            if (actual.Count != reference.Count || actual.Zip(reference).Any(p => !p.First.SequenceEqual(p.Second)))
            { throw new InvalidOperationException("QRS reporting advanced or changed the source."); }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: authored QRS measurements, Q/QS distinction, source parity and atomic native report lifecycle");
    }
}
