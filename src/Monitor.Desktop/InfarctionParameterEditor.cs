// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Regional authoring controls reuse the existing fixed infarction snapshots.
internal sealed class InfarctionParameterEditor : StackPanel
{
    internal CheckBox[] ChestLeads { get; } = Enumerable.Range(1, 6).Select(i => new CheckBox { Content = $"V{i}" }).ToArray();
    internal NumericUpDown Delay { get; } = new() { Minimum = 0, Maximum = 500, Increment = 10, Value = 0, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _chest = new() { Spacing = 8 };
    private readonly TextBlock _region = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    internal event Action? Changed;
    private EcgChestInfarctionPlan? _preset;
    internal InfarctionParameterEditor()
    {
        Spacing = 8;
        Delay.ValueChanged += (_, _) => Changed?.Invoke();

        Children.Add(_region);
        _chest.Children.Add(new TextBlock { Text = "胸导联区域（至少选择一个）" });
        var row = new WrapPanel();
        foreach (var lead in ChestLeads)
        {
            lead.Margin = new Avalonia.Thickness(0, 0, 16, 0);
            AutomationProperties.SetName(lead, $"梗死快照目标胸导联 {lead.Content}");
            lead.IsCheckedChanged += (_, _) => Changed?.Invoke();
            row.Children.Add(lead);
        }
        _chest.Children.Add(row); Children.Add(_chest);
        const string label = "局部复极延长（ms；T 时限与 QT 同步增加）";
        Children.Add(new TextBlock { Text = label }); Children.Add(Delay);
        AutomationProperties.SetName(Delay, label);
        var reset = new Button { Content = "恢复梗死模板参数", MinHeight = 44 };
        reset.Click += (_, _) => Reset(_preset); Children.Add(reset);
        Children.Add(new TextBlock { Text = "应用后同步十二导联与监护；卡片保留模板预览，快照不会随时间演变。", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
    }
    internal void Reset(EcgChestInfarctionPlan? preset)
    {
        _preset = preset; IsVisible = preset is not null;
        if (preset is null) { return; }
        _chest.IsVisible = preset.Territory is InfarctionTerritory.Anteroseptal or InfarctionTerritory.Anterior or InfarctionTerritory.ExtensiveAnterior;
        int mask = preset.Territory switch
        {
            InfarctionTerritory.Anteroseptal => 7,
            InfarctionTerritory.Anterior => 28,
            InfarctionTerritory.ExtensiveAnterior => 31,
            _ => 0
        };
        for (int i = 0; i < 6; i++) { ChestLeads[i].IsChecked = (mask & (1 << i)) != 0; }
        Delay.Value = preset.RepolarizationDelayNs / 1_000_000m;
        _region.Text = _chest.IsVisible ? "仅修改所选胸导联；其他胸导联及肢体导联保持原样。"
            : "保留模板区域及肢体导联投影关系，仅调整局部复极时限。";
        Changed?.Invoke();
    }
    internal EcgChestInfarctionPlan? Read(EcgChestInfarctionPlan? preset)
    {
        if (preset is null) { return null; }
        if (preset != _preset) { throw new ArgumentException("Preview.StaleInfarction"); }
        decimal delay = Delay.Value ?? throw new ArgumentException("Preview.InvalidInfarctionDelay");
        if (delay is < 0 or > 500 || delay != decimal.Truncate(delay)) { throw new ArgumentException("Preview.InvalidInfarctionDelay"); }
        var edited = preset with { RepolarizationDelayNs = (long)delay * 1_000_000 };
        if (!_chest.IsVisible) { return edited; }
        int mask = ChestLeads.Select((lead, i) => lead.IsChecked == true ? 1 << i : 0).Aggregate(0, (a, b) => a | b);
        if (mask == 0) { throw new ArgumentException("Preview.InfarctionChestRequired"); }
        int original = preset.Territory switch { InfarctionTerritory.Anteroseptal => 7, InfarctionTerritory.Anterior => 28, _ => 31 };
        return mask == original ? edited : edited with { Territory = InfarctionTerritory.CustomChest, ChestMask = mask };
    }
}
