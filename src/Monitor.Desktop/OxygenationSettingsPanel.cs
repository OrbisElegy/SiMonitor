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
    internal CheckBox Realtime { get; } = new() { Content = "使用实时氧合模型" };
    internal OxygenationPatientPanel Patient { get; } = new();
    internal NumericUpDown DemandMultiplier { get; } = Field(1, 4, 1, .1m);
    internal NumericUpDown TidalVolume { get; } = Field(0, 1500, 450, 10);
    internal NumericUpDown DeadSpace { get; } = Field(0, 500, 150, 10);
    internal NumericUpDown InspiredOxygen { get; } = Field(10, 100, 21, 1);
    internal CheckBox AirwayOpen { get; } = new() { Content = "气道开放", IsChecked = true };
    internal Button UpdateVentilation { get; } = new()
    {
        Content = "更新通气／耗氧，保留当前氧储备",
        MinHeight = 44,
        HorizontalAlignment = HorizontalAlignment.Left
    };
    internal TabItem PatientPage { get; }
    internal TabItem VentilationPage { get; }
    private readonly StackPanel _ventilation = new() { Spacing = 10 };
    private bool _sourceEnabled = true;
    protected override Type StyleKeyOverride => typeof(TabControl);

    internal OxygenationSettingsPanel()
    {
        Padding = new Thickness(0);
        Add("潮气量 VT（mL，BTPS）", TidalVolume);
        Add("死腔量 VD（mL，BTPS）", DeadSpace);
        Add("吸入氧浓度 FiO₂（%）", InspiredOxygen);
        _ventilation.Children.Add(AirwayOpen);
        Add("教师耗氧倍增器（1–4 倍）", DemandMultiplier);
        _ventilation.Children.Add(new TextBlock
        {
            Text = "首次启用需从头开始；通气更新同步作用于 RESP 和 CO₂ 波形。",
            TextWrapping = TextWrapping.Wrap
        });
        _ventilation.Children.Add(UpdateVentilation);
        PatientPage = Page("患者基线", Patient);
        VentilationPage = Page("通气与耗氧", _ventilation);
        Items.Add(PatientPage);
        Items.Add(VentilationPage);
        AutomationProperties.SetName(this, "指脉氧参数分组");
        Realtime.IsCheckedChanged += (_, _) => RefreshPages();
        RefreshPages();
    }

    internal void SetSourceContent(Control content)
    {
        Items.Insert(0, Page("信号源", content));
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

    private void Add(string label, Control control)
    {
        _ventilation.Children.Add(new TextBlock { Text = label });
        _ventilation.Children.Add(control);
        AutomationProperties.SetName(control, label);
    }

    private static TabItem Page(string title, Control content) => new()
    {
        Header = title,
        Content = content,
        Padding = new Thickness(0),
        Margin = new Thickness(0, 0, 20, 0),
        FontSize = 14,
        MinHeight = 44
    };

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
        DesignPreviewSettings.ReadVitalValue(TidalVolume, 1000, "潮气量 VT"),
        DesignPreviewSettings.ReadVitalValue(DeadSpace, 1000, "死腔量 VD"),
        DesignPreviewSettings.ReadVitalValue(InspiredOxygen, 10000, "吸入氧浓度 FiO₂"), AirwayOpen.IsChecked == true);

    internal decimal ReadDemandMultiplier() => DesignPreviewSettings.ReadVitalValue(DemandMultiplier, 100, "耗氧倍增器") / 100m;
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
