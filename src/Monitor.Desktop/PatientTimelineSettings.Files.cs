// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed partial class PatientTimelineSettings
{
    private readonly List<PatientTimelineFile> _files = [];
    private string _selectedFileId = "";
    private string _appliedFileId = "";
    private bool _appliedEnabled = true;
    private bool _loadingFile;
    private bool _refreshingFiles;
    private Control? _fileCommands;
    private readonly ContentControl _fileDraft = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _fileStatusTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    internal ComboBox Files { get; } = new() { MinHeight = 40, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
    internal Button FileActions { get; } = Button();
    internal Button NewFile { get; } = Button();
    internal Button ImportFile { get; } = Button();
    internal Button ExportFile { get; } = Button();
    internal Button RemoveFile { get; } = Button();
    internal ToggleSwitch TimelineEnabled { get; } = new() { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    internal TextBlock FileStatus { get; } = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false };
    internal TextBox Seed { get; } = new() { MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Stretch };
    internal Button GenerateSeed { get; } = Button();
    internal Expander SeedDetails { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal TextBlock SeedError { get; } = new() { Foreground = CriticalText, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    internal string SelectedFileId => _selectedFileId;
    internal string SelectedFileName => _files.FirstOrDefault(file => file.Id == _selectedFileId)?.Name ?? "";
    internal event Action? EnabledChanged;

    private StackPanel BuildFileCommands()
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(20, 12, 20, 0) };
        var commands = new Grid { ColumnDefinitions = new("Auto,Auto,*,Auto") };
        commands.Children.Add(Files);
        FileActions.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(FileActions, 1);
        commands.Children.Add(FileActions);
        Status.Margin = new Thickness(16, 0);
        Status.TextTrimming = TextTrimming.CharacterEllipsis;
        _fileDraft.Content = Status;
        Grid.SetColumn(_fileDraft, 2);
        commands.Children.Add(_fileDraft);
        Grid.SetColumn(TimelineEnabled, 3);
        commands.Children.Add(TimelineEnabled);
        var menu = new StackPanel { Spacing = 4, MinWidth = 180 };
        foreach (var button in new[] { NewFile, ImportFile, ExportFile, RemoveFile })
        {
            if (ReferenceEquals(button, RemoveFile)) { menu.Children.Add(new Separator { Margin = new Thickness(0, 4) }); }
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            if (ReferenceEquals(button, RemoveFile)) { button.Foreground = CriticalText; }
            button.Click += (_, _) => FileActions.Flyout?.Hide();
            menu.Children.Add(button);
        }
        FileActions.Flyout = new Flyout { Content = menu, Placement = PlacementMode.BottomEdgeAlignedLeft };
        _localization.Bind(FileActions, ContentControl.ContentProperty, text => text.GetString("timeline.fileActions") + "  ▾");
        _localization.Bind(FileActions, AutomationProperties.NameProperty, "timeline.fileActions");
        panel.Children.Add(commands);
        panel.Children.Add(BuildSeedEditor());
        panel.Children.Add(FileStatus);
        _localization.Bind(Files, AutomationProperties.NameProperty, "timeline.files");
        Caption(NewFile, "timeline.newFile");
        Caption(ImportFile, "timeline.importFile");
        Caption(ExportFile, "timeline.exportFile");
        Caption(RemoveFile, "timeline.removeFile");
        _localization.Bind(TimelineEnabled, AutomationProperties.NameProperty, "timeline.enabled");
        _localization.Bind(TimelineEnabled, ToggleSwitch.OnContentProperty, "settings.stateEnabled");
        _localization.Bind(TimelineEnabled, ToggleSwitch.OffContentProperty, "settings.stateDisabled");
        _fileStatusTimer.Tick += (_, _) => { _fileStatusTimer.Stop(); FileStatus.IsVisible = false; };
        DetachedFromVisualTree += (_, _) => _fileStatusTimer.Stop();
        Files.SelectionChanged += (_, _) =>
        {
            if (_refreshingFiles || Files.SelectedIndex < 0 || Files.SelectedIndex >= _files.Count) { return; }
            string id = _files[Files.SelectedIndex].Id;
            if (id == _selectedFileId) { return; }
            try
            {
                StoreCurrentFile();
                LoadFile(id);
                DraftChanged?.Invoke();
            }
            catch (ArgumentException)
            {
                RefreshFileChoices();
                ReportFileStatus("timeline.fixBeforeFileChange", true);
            }
        };
        NewFile.Click += (_, _) => CreateFile();
        RemoveFile.Click += (_, _) => RemoveSelectedFile();
        TimelineEnabled.IsCheckedChanged += (_, _) =>
        {
            if (_loadingFile || _files.Count == 0) { return; }
            RefreshDraftStatus();
            EnabledChanged?.Invoke();
            DraftChanged?.Invoke();
        };
        _fileCommands = panel;
        return panel;
    }

    private void InitializeFiles()
    {
        Seed.Text = NewSeed();
        var file = new PatientTimelineFile(Guid.NewGuid().ToString("N"), _localization.Format("timeline.fileName", 1), ReadProgram());
        _files.Add(file);
        _selectedFileId = file.Id;
        RefreshFileChoices();
        MarkApplied();
    }

    internal void MarkDisabled()
    {
        _appliedEnabled = false;
        RefreshDraftStatus();
    }

    internal PatientTimelineFile ReadFile() => new PatientTimelineFile(_selectedFileId, SelectedFileName, ReadProgram()).Snapshot();

    internal PatientTimelineProgram ReadProgramForRestart() => TimelineEnabled.IsChecked == true ? ReadProgram() :
        ReadLastValidLibrary().Files.Single(file => file.Id == _selectedFileId).Program;

    internal PatientTimelineLibrary ReadLibrary()
    {
        StoreCurrentFile();
        return new PatientTimelineLibrary(_selectedFileId, TimelineEnabled.IsChecked == true, _files.ToArray()).Snapshot();
    }

    // Invalid in-progress fields stay in the editor; persistence retains their last valid version.
    internal PatientTimelineLibrary ReadLastValidLibrary()
    {
        try { StoreCurrentFile(); }
        catch (ArgumentException) { }
        return new PatientTimelineLibrary(_selectedFileId, TimelineEnabled.IsChecked == true, _files.ToArray()).Snapshot();
    }

    internal void RestoreLibrary(PatientTimelineLibrary library)
    {
        var owned = library.Snapshot();
        foreach (var file in owned.Files) { ValidateTemplates(file.Program); }
        bool migrated = owned.Files.Any(file => file.Program.SeedHex is null);
        var files = owned.Files.Select(file => file with { Program = MigrateSeed(file.Program, out _) }).ToArray();
        _files.Clear();
        _files.AddRange(files);
        _loadingFile = true;
        try { TimelineEnabled.IsChecked = owned.Enabled; }
        finally { _loadingFile = false; }
        LoadFile(owned.SelectedFileId);
        // The library supersedes the legacy program embedded in generator preferences.
        RequiresMigrationSave = migrated;
    }

    internal void Import(PatientTimelineFile file)
    {
        var owned = file.Snapshot();
        owned = owned with { Program = MigrateSeed(owned.Program, out _) };
        ValidateTemplates(owned.Program);
        if (_files.Count >= PatientTimelineLibrary.MaximumFiles) { throw new ArgumentException("PatientTimeline.TooManyFiles"); }
        StoreCurrentFile();
        owned = owned with { Id = Guid.NewGuid().ToString("N"), Name = UniqueFileName(owned.Name) };
        _files.Add(owned);
        LoadFile(owned.Id);
        DraftChanged?.Invoke();
    }

    internal void RenameFile(string id, string name)
    {
        int index = _files.FindIndex(file => file.Id == id);
        if (index < 0) { return; }
        _files[index] = (_files[index] with { Name = name }).Snapshot();
        RefreshFileChoices();
        DraftChanged?.Invoke();
    }

    internal void CreateFile()
    {
        if (_files.Count >= PatientTimelineLibrary.MaximumFiles) { return; }
        try
        {
            Import(new("new", UniqueFileName(_localization.Format("timeline.fileName", _files.Count + 1)),
                new("main", [new("main", _localization.Get("timeline.main"), [], new())]) { SeedHex = NewSeed() }));
            FileStatus.IsVisible = false;
        }
        catch (ArgumentException) { ReportFileStatus("timeline.fixBeforeFileChange", true); }
    }

    internal void RemoveSelectedFile()
    {
        if (_files.Count <= 1) { return; }
        // Removing from the library never deletes an imported or exported disk file.
        _files.RemoveAll(file => file.Id == _selectedFileId);
        LoadFile(_files[0].Id);
        DraftChanged?.Invoke();
    }

    internal void ReportFileStatus(string key, bool error = false)
    {
        _fileStatusTimer.Stop();
        FileStatus.Foreground = error ? CriticalText : DesktopFluentStyle.SecondaryText;
        _localization.Bind(FileStatus, TextBlock.TextProperty, key);
        FileStatus.IsVisible = true;
        if (!error) { _fileStatusTimer.Start(); }
    }

    private void StoreCurrentFile()
    {
        int index = _files.FindIndex(file => file.Id == _selectedFileId);
        if (index >= 0) { _files[index] = ReadFile(); }
    }

    private void LoadFile(string id)
    {
        var file = _files.Single(item => item.Id == id);
        _loadingFile = true;
        try
        {
            _selectedFileId = id;
            RestoreProgram(file.Program);
            RefreshFileChoices();
        }
        finally { _loadingFile = false; }
        FileStatus.IsVisible = false;
        RefreshDraftStatus();
    }

    private void RefreshFileChoices()
    {
        _refreshingFiles = true;
        try
        {
            _localization.SetChoices(Files, _files.Select(file =>
                (Func<Monitor.Application.Localization.ITextLocalizer, string>)(_ => file.Name)));
            Files.SelectedIndex = _files.FindIndex(file => file.Id == _selectedFileId);
            NewFile.IsEnabled = ImportFile.IsEnabled = _files.Count < PatientTimelineLibrary.MaximumFiles;
            RemoveFile.IsEnabled = _files.Count > 1;
        }
        finally { _refreshingFiles = false; }
    }

    private string UniqueFileName(string name)
    {
        string result = name;
        for (int index = 2; _files.Any(file => file.Name == result); index++)
        { result = name[..Math.Min(name.Length, 72)] + $" ({index})"; }
        return result;
    }

    private static void ValidateTemplates(PatientTimelineProgram program)
    {
        foreach (string id in program.Timelines.SelectMany(line => line.InitialState is { } state ? line.Events.Prepend(state) : line.Events)
            .Select(item => item.EcgTemplateId).OfType<string>())
        {
            if (!Enumerable.Range(0, DesignPreviewSettings.EcgChoiceCount).Any(index => DesignPreviewSettings.EcgTemplateKey(index) == id))
            { throw new ArgumentException("PatientTimeline.InvalidTemplate"); }
        }
    }
}
