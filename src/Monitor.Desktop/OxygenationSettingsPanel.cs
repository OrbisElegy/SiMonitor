// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Presentation;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Optical source, patient baseline and ventilation inputs are separate tabs; the
// realtime-only tabs stay disabled until the source and realtime model are enabled.
internal sealed class OxygenationSettingsPanel : TabControl
{
    internal CheckBox Realtime { get; } = new();
    internal OxygenationPatientPanel Patient { get; }
    internal NumericUpDown DemandMultiplier { get; } = Field(1, 4, 1, .1m);
    internal NumericUpDown TidalVolume { get; } = Field(0, 1500, 450, 10);
    internal NumericUpDown DeadSpace { get; } = Field(0, 500, 150, 10);
    internal NumericUpDown InspiredOxygen { get; } = Field(10, 100, 21, 1);
    internal CheckBox AirwayOpen { get; } = new() { IsChecked = true };
    internal Button UpdateVentilation { get; } = new()
    {
        MinHeight = 44,
        HorizontalAlignment = HorizontalAlignment.Left
    };
    internal TabItem PatientPage { get; }
    internal TabItem VentilationPage { get; }
    private readonly StackPanel _ventilation = new() { Spacing = 10 };
    private readonly DesktopLocalization _localization;
    private bool _sourceEnabled = true;
    protected override Type StyleKeyOverride => typeof(TabControl);

    internal OxygenationSettingsPanel(DesktopLocalization? localization = null)
    {
        _localization = localization ?? new DesktopLocalization();
        Patient = new OxygenationPatientPanel(_localization);
        Padding = new Thickness(0);
        _localization.Bind(Realtime, ContentControl.ContentProperty, "oxygenation.realtime");
        _localization.Bind(AirwayOpen, ContentControl.ContentProperty, "oxygenation.airwayOpen");
        _localization.Bind(UpdateVentilation, ContentControl.ContentProperty, "oxygenation.updateVentilation");
        Add("oxygenation.tidalVolume", TidalVolume);
        Add("oxygenation.deadSpace", DeadSpace);
        Add("oxygenation.inspiredOxygen", InspiredOxygen);
        _ventilation.Children.Add(AirwayOpen);
        Add("oxygenation.demandMultiplier", DemandMultiplier);
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _localization.Bind(note, TextBlock.TextProperty, "oxygenation.ventilationNote");
        _ventilation.Children.Add(note);
        _ventilation.Children.Add(UpdateVentilation);
        PatientPage = Page("oxygenation.patientPage", Patient);
        VentilationPage = Page("oxygenation.ventilationPage", _ventilation);
        Items.Add(PatientPage);
        Items.Add(VentilationPage);
        _localization.Bind(this, AutomationProperties.NameProperty, "oxygenation.groupName");
        Realtime.IsCheckedChanged += (_, _) => RefreshPages();
        RefreshPages();
    }

    internal void SetSourceContent(Control content)
    {
        Items.Insert(0, Page("oxygenation.sourcePage", content));
        SelectedIndex = 0;
    }

    internal void SetSourceEnabled(bool enabled)
    {
        _sourceEnabled = enabled;
        Realtime.IsEnabled = enabled;
        RefreshPages();
    }

    private void RefreshPages()
    {
        bool editable = _sourceEnabled && Realtime.IsChecked == true;
        PatientPage.IsEnabled = editable;
        VentilationPage.IsEnabled = editable;
        if (!editable && SelectedItem is TabItem selected && (selected == PatientPage || selected == VentilationPage)) { SelectedIndex = 0; }
    }

    private void Add(string key, Control control)
    {
        var label = new TextBlock();
        _localization.Bind(label, TextBlock.TextProperty, key);
        _ventilation.Children.Add(label);
        _ventilation.Children.Add(control);
        _localization.Bind(control, AutomationProperties.NameProperty, key);
    }

    private TabItem Page(string key, Control content)
    {
        var page = new TabItem
        {
            Content = content,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 20, 0),
            FontSize = 14,
            MinHeight = 44
        };
        _localization.Bind(page, TabItem.HeaderProperty, key);
        return page;
    }

    private static NumericUpDown Field(decimal minimum, decimal maximum, decimal value, decimal increment) =>
        new()
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Increment = increment,
            Width = 180,
            HorizontalAlignment = HorizontalAlignment.Left
        };

    internal VentilationTransportPlan ReadVentilation() => new(
        DesignPreviewSettings.ReadVitalValue(TidalVolume, 1000, "oxygenation.tidalVolumeField"),
        DesignPreviewSettings.ReadVitalValue(DeadSpace, 1000, "oxygenation.deadSpaceField"),
        DesignPreviewSettings.ReadVitalValue(InspiredOxygen, 10000, "oxygenation.inspiredOxygenField"), AirwayOpen.IsChecked == true);

    internal decimal ReadDemandMultiplier() => DesignPreviewSettings.ReadVitalValue(DemandMultiplier, 100, "oxygenation.demandMultiplierField") / 100m;
    internal RealtimeOxygenationConfiguration ReadConfiguration() => Capture(true)!.CreateConfiguration();

    internal OxygenationEditorPreferences? Capture(bool opticalEnabled)
    {
        try
        {
            var ventilation = ReadVentilation();
            var result = new OxygenationEditorPreferences(Realtime.IsChecked == true, ventilation.TidalVolumeMicrolitersBtps,
                ventilation.DeadSpaceMicrolitersBtps, ventilation.InspiredOxygenMillionths, ventilation.AirwayOpen)
            { Patient = Patient.Read(), OxygenDemandMultiplier = ReadDemandMultiplier() };
            result.Validate();
            return result;
        }
        // Dormant incomplete drafts must not prevent applying another source.
        catch (ArgumentException) when (!opticalEnabled || Realtime.IsChecked != true) { return null; }
    }

    internal void Restore(OxygenationEditorPreferences? saved)
    {
        saved ??= new(false, 450000, 150000, 210000, true);
        saved.Validate();
        TidalVolume.Value = saved.TidalVolumeMicrolitersBtps / 1000m;
        DeadSpace.Value = saved.DeadSpaceMicrolitersBtps / 1000m;
        InspiredOxygen.Value = saved.InspiredOxygenMillionths / 10000m;
        AirwayOpen.IsChecked = saved.AirwayOpen;
        DemandMultiplier.Value = saved.OxygenDemandMultiplier;
        Patient.Restore(saved.Patient);
        Realtime.IsChecked = saved.Realtime;
    }
}
