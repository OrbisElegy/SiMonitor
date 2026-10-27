// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Preferences;

namespace Monitor.Desktop;

internal sealed partial class DesignPreviewWindow
{
    private static readonly FilePickerFileType TimelineFileType = new("SiMonitor patient timeline") { Patterns = ["*.simtimeline"] };
    private readonly DispatcherTimer _timelineSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _timelineFileBusy;
    private bool _timelineTimerConnected;
    private bool _timelineFilesDirty;

    private void ConnectTimelineFiles()
    {
        var editor = Settings.Timeline;
        editor.ImportFile.Click += async (_, _) => await ImportPatientTimeline();
        editor.ExportFile.Click += async (_, _) => await ExportPatientTimeline();
        editor.EnabledChanged += () =>
        {
            if (!ReferenceEquals(editor, Settings.Timeline) || _closed) { return; }
            try
            {
                if (editor.TimelineEnabled.IsChecked != true)
                {
                    _session.StopPatientTimeline();
                    _manualTimelineEntry = null;
                    editor.MarkDisabled();
                    MonitorView.Refresh();
                }
                UpdateState();
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                editor.TimelineEnabled.IsChecked = true;
                editor.ReportFileStatus("timeline.disableFailed", true);
            }
            _timelineFilesDirty = true;
            SaveTimelineFiles();
        };
        editor.DraftChanged += () =>
        {
            if (!ReferenceEquals(editor, Settings.Timeline) || _closed) { return; }
            _timelineFilesDirty = true;
            _timelineSaveTimer.Stop();
            _timelineSaveTimer.Start();
        };
        if (!_timelineTimerConnected)
        {
            _timelineTimerConnected = true;
            _timelineSaveTimer.Tick += (_, _) => { _timelineSaveTimer.Stop(); SaveTimelineFiles(); };
        }
    }

    private void SaveTimelineFiles()
    {
        if (_closed || _preferences is null || !_timelineFilesDirty) { return; }
        SaveDisplayPreferences();
    }

    private async Task ImportPatientTimeline()
    {
        if (_timelineFileBusy || _applying) { return; }
        _timelineFileBusy = true;
        var editor = Settings.Timeline;
        string selected = editor.SelectedFileId;
        try
        {
            // Validate before opening a picker so an invalid draft is never silently replaced.
            _ = editor.ReadFile();
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Localization.Get("timeline.importFile"),
                AllowMultiple = false,
                FileTypeFilter = [TimelineFileType]
            });
            foreach (var file in files)
            {
                using (file)
                {
                    if (_closed || _applying || !ReferenceEquals(editor, Settings.Timeline) || editor.SelectedFileId != selected) { continue; }
                    await using var stream = await file.OpenReadAsync();
                    byte[] buffer = new byte[PatientTimelineFileFormat.MaximumBytes + 1];
                    int count = 0, read;
                    while (count < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(count))) != 0) { count += read; }
                    using var memory = new MemoryStream(buffer, 0, count, writable: false);
                    PatientTimelineFile document = PatientTimelineFileFormat.Read(memory);
                    document = document with { Name = Path.GetFileNameWithoutExtension(file.Name) };
                    if (!_closed && !_applying && ReferenceEquals(editor, Settings.Timeline) && editor.SelectedFileId == selected)
                    {
                        editor.Import(document);
                        SaveTimelineFiles();
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        { if (!_closed && ReferenceEquals(editor, Settings.Timeline)) { editor.ReportFileStatus("timeline.importFailed", true); } }
        finally { _timelineFileBusy = false; }
    }

    private async Task ExportPatientTimeline()
    {
        if (_timelineFileBusy || _applying) { return; }
        _timelineFileBusy = true;
        var editor = Settings.Timeline;
        try
        {
            PatientTimelineFile document = editor.ReadFile();
            string suggested = string.Concat(document.Name.Select(character => Path.GetInvalidFileNameChars().Contains(character) || character is '/' or '\\' ? '_' : character));
            using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Localization.Get("timeline.exportFile"),
                SuggestedFileName = suggested + ".simtimeline",
                DefaultExtension = "simtimeline",
                FileTypeChoices = [TimelineFileType],
                ShowOverwritePrompt = true
            });
            if (file is null || _closed || !ReferenceEquals(editor, Settings.Timeline)) { return; }
            string name = Path.GetFileNameWithoutExtension(file.Name);
            document = document with { Name = name };
            byte[] bytes = PatientTimelineFileFormat.Write(document);
            await using (var stream = await file.OpenWriteAsync())
            {
                if (stream.CanSeek) { stream.SetLength(0); }
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            }
            if (!_closed && ReferenceEquals(editor, Settings.Timeline))
            {
                editor.RenameFile(document.Id, name);
                editor.ReportFileStatus("timeline.exported");
                SaveTimelineFiles();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        { if (!_closed && ReferenceEquals(editor, Settings.Timeline)) { editor.ReportFileStatus("timeline.exportFailed", true); } }
        finally { _timelineFileBusy = false; }
    }
}
