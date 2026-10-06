// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Automation;
using Avalonia.Controls;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed partial class DesignPreviewSettings
{
    internal NumericUpDown AtrialRate { get; } = new() { Minimum = 20, Maximum = 600, Value = 75, Increment = 1, Width = 180, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
    internal NumericUpDown PauseDuration { get; } = new() { Minimum = 1200, Maximum = 10000, Value = 2000, Increment = 100, Width = 180, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
    internal NumericUpDown ConductionPercent { get; } = new() { Minimum = 10, Maximum = 50, Value = 33, Increment = 1, Width = 180, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
    internal TextBlock PauseDurationLabel { get; } = Text("");
    internal TextBlock ConductionPercentLabel { get; } = Text("");

    internal SeededRhythmSchedule? ReadRhythm(AvConductionPattern pattern) => SeededRhythmSchedule.Supports(pattern)
        ? new(RateSeed.Text ?? "", pattern == AvConductionPattern.SinusArrestIllustration
            ? ReadVitalValue(PauseDuration, 1, "vitals.pauseDuration") * 1_000_000L : 2_000_000_000,
            pattern == AvConductionPattern.VariableAtrialFlutterIllustration
                ? ReadVitalValue(ConductionPercent, 1, "vitals.conductionPercent") : 33) : null;

    internal TextBlock CardiacRateLabel { get; } = Text("");
    internal TextBlock AtrialRateLabel { get; } = Text("");
    internal TextBlock CardiacRateStatus { get; } = Text("");

    internal CardiacRateAdjustment? ReadCardiacRate(bool validateSources = true)
    {
        var pair = DesignPreviewWindow.ResolveStyle(EcgSelection, RespirationSelection, 0);
        var rhythm = ReadRhythm(pair.Physiology.ConductionPattern);
        pair = (pair.Physiology with { RhythmSchedule = rhythm }, pair.Ecg with { RhythmSchedule = rhythm });
        var plan = pair.Physiology.ResolvePlan();
        if (CardiacRateEnabled.IsChecked != true || !CardiacRateAdjustment.Supports(plan)) { return null; }
        var adjustment = new CardiacRateAdjustment(ReadVitalValue(HeartRate, 1, "vitals.heartRateField"),
            plan.IndependentVentricularPeriodNs is not null ? ReadVitalValue(AtrialRate, 1, "vitals.atrialRateField") : null,
            RateSeed.Text ?? "", ReadVitalValue(RateVariation, 10, "vitals.rateVariationField"));
        adjustment.Validate(plan);
        // Keep the established sinus timing adaptation; all other templates
        // retain their morphology and must fit their source support bounds.
        if (validateSources && (EcgSelection != 0 || adjustment.Rate.HeartRateBpm is < 30 or > 180))
        {
            try
            {
                _ = PhysiologyIllustrationSource.Create(pair.Physiology with { RateAdjustment = adjustment });
                _ = ProjectedEcgDemoSource.Create(pair.Ecg with { RateAdjustment = adjustment });
            }
            catch (ArgumentException error) when (error is EventWaveformException or PhysiologySignalException or ElectrodeSignalException)
            { throw new ArgumentException("CardiacRate.WaveformSupport", error); }
        }
        return adjustment;
    }

    private bool CardiacRateValid()
    {
        try { _ = ReadCardiacRate(); return true; }
        catch (ArgumentException) { return false; }
    }

    private void RefreshCardiacRate()
    {
        if (EcgSelection < 0 || EcgSelection >= EcgChoiceCount) { return; }
        var plan = DesignPreviewWindow.ResolveStyle(EcgSelection, RespirationSelection, 0).Physiology.ResolvePlan();
        bool supported = CardiacRateAdjustment.Supports(plan);
        bool independent = plan.IndependentVentricularPeriodNs is not null;
        PauseDuration.IsVisible = PauseDurationLabel.IsVisible = plan.ConductionPattern == AvConductionPattern.SinusArrestIllustration;
        ConductionPercent.IsVisible = ConductionPercentLabel.IsVisible = plan.ConductionPattern == AvConductionPattern.VariableAtrialFlutterIllustration;
        Localization.Bind(PauseDurationLabel, TextBlock.TextProperty, "vitals.pauseDuration");
        Localization.Bind(PauseDuration, AutomationProperties.NameProperty, "vitals.pauseDuration");
        Localization.Bind(ConductionPercentLabel, TextBlock.TextProperty, "vitals.conductionPercent");
        Localization.Bind(ConductionPercent, AutomationProperties.NameProperty, "vitals.conductionPercent");
        string label = plan.ConductionPattern == AvConductionPattern.SinusArrestIllustration ? "vitals.normalSinusRate" : independent ? "vitals.ventricularRate" :
            plan.CardiacActivity == CardiacActivity.AtrialOnly || AtrialFlutterReference.IsPattern(plan.ConductionPattern) ||
            plan.VentricularConductionRatio > 1 ? "vitals.atrialRate" :
            AtrialFibrillationReference.IsPattern(plan.ConductionPattern) ? "vitals.meanVentricularRate" : "vitals.heartRate";
        Localization.Bind(CardiacRateLabel, TextBlock.TextProperty, label);
        Localization.Bind(HeartRate, AutomationProperties.NameProperty, label);
        Localization.Bind(AtrialRateLabel, TextBlock.TextProperty, "vitals.atrialRate");
        Localization.Bind(AtrialRate, AutomationProperties.NameProperty, "vitals.atrialRate");
        AtrialRate.IsVisible = AtrialRateLabel.IsVisible = independent;
        HeartRate.IsEnabled = RateVariation.IsEnabled = supported;
        AtrialRate.IsEnabled = supported && independent;
        string status = supported ? "vitals.rateFromTemplate" : "vitals.rateNotApplicable";
        if (supported && CardiacRateEnabled.IsChecked == true)
        {
            status = "vitals.rateAdjusted";
            try { _ = ReadCardiacRate(); }
            catch (ArgumentException error)
            {
                status = error.Message.StartsWith("CardiacRate.", StringComparison.Ordinal)
                    ? "validation." + error.Message : "vitals.rateIncomplete";
            }
        }
        Localization.Bind(CardiacRateStatus, TextBlock.TextProperty, status);
    }
}
