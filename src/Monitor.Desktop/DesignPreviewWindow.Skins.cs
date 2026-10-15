// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed partial class DesignPreviewWindow
{
    private readonly string? _skinPath;
    private MonitorSkinPackage _skinPackage = MonitorSkinPackage.Generic;
    private readonly MonitorTrendHistory _trendHistory = new();
    internal MonitorSkinPackage CurrentSkinPackage => _skinPackage;
    private static readonly FilePickerFileType SkinFileType = new("SiMonitor skin") { Patterns = ["*.simskin"] };

    private bool LoadMonitorSkin()
    {
        if (_skinPath is null) { return true; }
        try
        {
            using var stream = File.OpenRead(_skinPath);
            _skinPackage = MonitorSkinPackage.Read(stream);
            return true;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return false; }
    }

    private void ConnectSkinSettings()
    {
        Settings.RestoreMonitorAppearance(_skinPackage);
        Settings.MonitorAppearanceChanged += ChangeMonitorSkin;
        Settings.ImportSkin.Click += async (_, _) => await ImportMonitorSkin();
        Settings.ExportSkin.Click += async (_, _) => await ExportMonitorSkin();
        Settings.RecommendedSkinLayout.Click += (_, _) => ApplyRecommendedSkinLayout();
    }

    internal void ChangeMonitorSkin(MonitorSkinPackage package)
    {
        if (_closed || _applying) { Settings.RestoreMonitorAppearance(_skinPackage); return; }
        DisarmDefibrillator();
        bool twelveLead = (_monitor.Skin as GenericMonitorSkin)?.TwelveLeadVisible == true;
        _skinPackage = package;
        _monitor = new(_session, Localization);
        MonitorView = new(_monitor, Localization, CreateSkin(), _trendHistory);
        ((GenericMonitorSkin)_monitor.Skin!).SetTwelveLeadVisible(twelveLead);
        BindAlarmAttention();
        MonitorView.AdditionalNotices = CurrentNotices;
        MonitorView.BeatSourceText = () => Settings.Sound.BeatSourceLabel;
        MonitorView.NoticeColorEnabled = () => Settings.Alerts.NoticeColorEnabled.IsChecked == true;
        MonitorView.AudioPauseStatus.Text = Settings.Sound.AudioPauseText;
        MonitorView.Refresh();
        RefreshNibp();
        Settings.Sound.RefreshAlarmNotices(MonitorView.ActiveNotices);
        Settings.RestoreMonitorAppearance(package);
        if (Page == 0) { SelectPage(0); }
        bool saved = _skinPath is null || package.Save(_skinPath);
        Localization.Bind(Settings.SkinStatus, TextBlock.TextProperty, saved ? "skin.changed" : "skin.saveFailed");
    }

    internal void ApplyRecommendedSkinLayout()
    {
        if (_closed || _applying) { return; }
        // This explicit action may change monitored channels; selecting a skin alone never does.
        var defaults = MonitorDisplayConfiguration.Default(MonitorSkin.FourRows);
        var display = _skinPackage.Bedside ? new MonitorDisplayConfiguration(MonitorSkin.ThreeRows, defaults.Slots.Take(3).ToArray()) : defaults;
        _session.UpdateDisplay(display);
        Settings.RestoreDisplay(display, Settings.PaperLayout.SelectedIndex, Settings.MeasurementUnits.SelectedIndex == 1);
        _displayPreferences = _displayPreferences with { Display = display };
        var skin = (GenericMonitorSkin)_monitor.Skin!;
        skin.LayoutRows.SelectedIndex = _skinPackage.Bedside ? 0 : 1;
        BindAlarmAttention();
        MonitorView.RefreshDisplay();
        Settings.Sound.RefreshAlarmNotices(MonitorView.ActiveNotices);
        RefreshDefibrillator();
        if (Page == 0) { SelectPage(0); }
        SaveDisplayPreferences();
        Localization.Bind(Settings.SkinStatus, TextBlock.TextProperty, "skin.layoutApplied");
    }

    private async Task ImportMonitorSkin()
    {
        var editor = Settings;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Localization.Get("skin.import"),
                AllowMultiple = false,
                FileTypeFilter = [SkinFileType]
            });
            foreach (var file in files)
            {
                using (file)
                {
                    if (_closed || _applying || !ReferenceEquals(editor, Settings)) { continue; }
                    await using var stream = await file.OpenReadAsync();
                    // Bound reads for both ordinary files and provider streams before parsing.
                    byte[] buffer = new byte[MonitorSkinPackage.MaximumBytes + 1];
                    int count = 0, read;
                    while (count < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(count))) != 0) { count += read; }
                    using var memory = new MemoryStream(buffer, 0, count, writable: false);
                    var package = MonitorSkinPackage.Read(memory);
                    if (!_closed && !_applying && ReferenceEquals(editor, Settings)) { ChangeMonitorSkin(package); }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        { if (!_closed && ReferenceEquals(editor, Settings)) { Localization.Bind(editor.SkinStatus, TextBlock.TextProperty, "skin.importFailed"); } }
    }

    private async Task ExportMonitorSkin()
    {
        var package = _skinPackage;
        var editor = Settings;
        try
        {
            using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Localization.Get("skin.export"),
                SuggestedFileName = package.Id + ".simskin",
                DefaultExtension = "simskin",
                FileTypeChoices = [SkinFileType],
                ShowOverwritePrompt = true
            });
            if (file is null || _closed) { return; }
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) { stream.SetLength(0); }
            package.Write(stream);
            if (ReferenceEquals(editor, Settings)) { Localization.Bind(editor.SkinStatus, TextBlock.TextProperty, "skin.exported"); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        { if (!_closed && ReferenceEquals(editor, Settings)) { Localization.Bind(editor.SkinStatus, TextBlock.TextProperty, "skin.exportFailed"); } }
    }
}
