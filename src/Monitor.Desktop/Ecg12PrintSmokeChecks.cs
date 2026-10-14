// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Monitor.Desktop;

internal static class Ecg12PrintSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            window.Pause();
            window.SelectPage(1);
            var session = window.Session;
            var blocks = window.EcgPage!.Paper.Blocks;
            window.EcgPage.Print.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dialog = window.PrintWindow ?? throw new InvalidOperationException("Print toolbar did not open its dialog.");
            try
            {
                dialog.Heading.Text = "心电图技能考核";
                dialog.Subtitle.Text = "标准十二导联 · A4";
                dialog.ShowCandidate.IsChecked = true;
                dialog.Candidate.Text = "姓名：____________    考号：____________";
                dialog.ShowMeasurements.IsChecked = true;
                dialog.AnswerChoices[Ecg12PrintAnswer.Pr].IsChecked = false;
                dialog.AnswerChoices[Ecg12PrintAnswer.QrsAxis].IsChecked = false;
                dialog.Diagnosis.Text = "";
                dialog.RefreshPreview();
                Require(dialog.Document.Options.AnswerFields.SequenceEqual(new[] { Ecg12PrintAnswer.HeartRate, Ecg12PrintAnswer.Qrs, Ecg12PrintAnswer.Qt, Ecg12PrintAnswer.Qtc }),
                    "only selected manual measurement questions are included");
                Require(dialog.Document.EndTimeNs == 7_160_000_000 && dialog.Document.Options.StartTimeNs == 0,
                    "A4 duration preserves 25 mm/s without squeezing a ten-second strip");
                Require(Ecg12PrintDocument.SampleX(1_000_000_000, dialog.Document.Options) - Ecg12PrintDocument.SamplesLeftMm(25) == 25,
                    "one second is physically 25 mm");
                Require(dialog.Document.GridTopMm == 38 && Ecg12PrintDocument.GridBottomMm == 246 && dialog.Document.RowHeightMm > 16,
                    "captions inside the grid release height to the ECG and increase lead spacing");
                Directory.CreateDirectory("artifacts");
                using (var pdf = File.Create("artifacts/ecg-print-a4-color.pdf")) { dialog.Document.WritePdf(pdf); }
                float colorGridTop = dialog.Document.GridTopMm;
                byte[] color = dialog.Document.PreviewPng();
                File.WriteAllBytes("artifacts/ecg-print-a4-color.png", color);
                DesktopViewportSmokeChecks.Layout(dialog, 1120, 880);
                using (var screenshot = new RenderTargetBitmap(new PixelSize(1120, 880), new Vector(96, 96)))
                {
                    screenshot.Render((Control)dialog.Content!);
                    screenshot.Save("artifacts/ecg-print-dialog.png", PngBitmapEncoderOptions.Default);
                }
                dialog.PrintTemplate.SelectedIndex = 1;
                dialog.ShowCandidate.IsChecked = false;
                dialog.ShowMeasurements.IsChecked = false;
                dialog.Diagnosis.Text = "填写诊断";
                dialog.RefreshPreview();
                Require(!dialog.Document.Options.ShowCandidate && !dialog.Document.Options.ShowMeasurements,
                    "candidate and measurement data can be omitted from the output");
                Require(dialog.Document.GridTopMm == colorGridTop - 10, "hidden candidate information releases its vertical space");
                byte[] monochrome = dialog.Document.PreviewPng();
                File.WriteAllBytes("artifacts/ecg-print-a4-monochrome.png", monochrome);
                using (var pdf = File.Create("artifacts/ecg-print-a4-monochrome.pdf")) { dialog.Document.WritePdf(pdf); }
                using var mono = SKBitmap.Decode(monochrome);
                using var colored = SKBitmap.Decode(color);
                Require(mono.Width == 840 && mono.Height == 1188, "preview is a complete A4 page at four pixels per millimeter");
                var colorPixel = colored.GetPixel(56, (int)(colorGridTop * 4) + 22);
                var monoPixel = mono.GetPixel(56, (int)(colorGridTop * 4) + 22);
                var labelGrid = colored.GetPixel(108, (int)(colorGridTop * 4) + 26);
                Require(labelGrid.Red > labelGrid.Green, "lead labels leave the underlying grid visible without a white mask");
                Require(colorPixel.Red > colorPixel.Green && monoPixel.Red == monoPixel.Green && monoPixel.Green == monoPixel.Blue && monoPixel.Red > 245,
                    "color has a continuous minor grid where monochrome leaves space between dots");
                dialog.Speed.SelectedIndex = 1;
                dialog.Gain.SelectedIndex = 0;
                dialog.Start.Value = 2;
                dialog.RefreshPreview();
                Require(dialog.Document.Options.GainMmPerMillivolt == 5 && dialog.Document.EndTimeNs == 5_480_000_000 &&
                    Ecg12PrintDocument.SampleX(3_000_000_000, dialog.Document.Options) - Ecg12PrintDocument.SamplesLeftMm(50) == 50,
                    "time offset, speed and gain apply to all twelve rows at physical scale");
                dialog.Heading.Text = new string('测', 80);
                dialog.Subtitle.Text = new string('长', 100);
                dialog.Candidate.Text = new string('考', 180);
                dialog.ShowCandidate.IsChecked = true;
                dialog.Diagnosis.Text = new string('诊', 240);
                dialog.RefreshPreview();
                using var longPdf = new MemoryStream();
                dialog.Document.WritePdf(longPdf);
                Require(longPdf.Length > 1000, "long text stays exportable inside the reserved page regions");
                Require(ReferenceEquals(session, window.Session) && ReferenceEquals(blocks, window.EcgPage.Paper.Blocks),
                    "printing preserves monitor state and the frozen paper samples");
            }
            finally { dialog.Close(); }
            window.EcgPage.Print.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var reopened = window.PrintWindow!;
            Require(reopened.PrintTemplate.SelectedIndex == 1 && reopened.Start.Value == 2, "print settings survive reopening in the current window");
            reopened.Close();
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("ECG printing: " + message); }
    }
}
