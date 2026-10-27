// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed partial class PatientTimelineSettings
{
    // The internal program-restoration contract preserves unset legacy values.
    // Persisted documents are upgraded before restoration; a user text edit
    // always leaves this compatibility state and must supply a complete seed.
    private bool _restoredUnsetSeed = true;
    internal bool RequiresMigrationSave { get; private set; }

    // Randomness is used only to author a new value. Reading, restoring and
    // running a timeline always reuse its saved deterministic seed.
    private static string NewSeed() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private Expander BuildSeedEditor()
    {
        _localization.Bind(SeedDetails, Expander.HeaderProperty, "timeline.seed");
        _localization.Bind(Seed, AutomationProperties.NameProperty, "timeline.seed");
        _localization.Bind(Seed, TextBox.PlaceholderTextProperty, "timeline.seedPlaceholder");
        _localization.Bind(SeedError, TextBlock.TextProperty, "timeline.invalidSeed");
        Caption(GenerateSeed, "vitals.generateSeed");
        GenerateSeed.Margin = new Thickness(8, 0, 0, 0);
        var fields = new Grid { ColumnDefinitions = new("*,Auto") };
        fields.Children.Add(Seed);
        Grid.SetColumn(GenerateSeed, 1);
        fields.Children.Add(GenerateSeed);
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(fields);
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = DesktopFluentStyle.SecondaryText };
        _localization.Bind(hint, TextBlock.TextProperty, "timeline.seedHint");
        content.Children.Add(hint);
        content.Children.Add(Notice(SeedError));
        SeedDetails.Content = content;
        GenerateSeed.Click += (_, _) => Seed.Text = NewSeed();
        Seed.PropertyChanged += (_, change) =>
        {
            if (change.Property != TextBox.TextProperty) { return; }
            // PropertyChanged runs synchronously inside RestoreProgram's guard.
            if (!_restoring) { _restoredUnsetSeed = false; }
            RefreshSeedValidation();
            if (!_restoring && !_loadingFile)
            {
                RefreshDraftStatus();
                DraftChanged?.Invoke();
            }
        };
        RefreshSeedValidation();
        return SeedDetails;
    }

    private void RefreshSeedValidation()
    {
        bool valid = SeedDraftValid();
        SeedError.IsVisible = !valid;
        if (!valid) { SeedDetails.IsExpanded = true; }
        _localization.Bind(Seed, AutomationProperties.HelpTextProperty,
            text => text.GetString(valid ? "timeline.seedHint" : "timeline.invalidSeed"));
    }

    private bool SeedDraftValid() => DesignPreviewSettings.IsSeedFormatValid(Seed.Text) ||
        _restoredUnsetSeed && string.IsNullOrEmpty(Seed.Text);

    private static PatientTimelineProgram MigrateSeed(PatientTimelineProgram program, out bool migrated)
    {
        var owned = program.Snapshot();
        migrated = owned.SeedHex is null;
        return migrated ? owned with { SeedHex = NewSeed() } : owned;
    }

    internal void RestorePersistedProgram(PatientTimelineProgram program)
    {
        var owned = MigrateSeed(program, out bool migrated);
        RestoreProgram(owned);
        RequiresMigrationSave = migrated;
    }

    internal void RestorePersistedEvents(IReadOnlyList<PatientTimelineEvent> events) => RestorePersistedProgram(new("main",
        [new("main", _localization.Get("timeline.main"), PatientTimelineEvent.Snapshot(events), new())]));

    internal string? ReadSeedForRestart()
    {
        if (TimelineEnabled.IsChecked != true) { return null; }
        var program = ReadProgram();
        // An empty default document must not override ordinary monitor setup.
        return program.Timelines.Any(line => line.InitialState is not null || line.Events.Count > 0) ? program.SeedHex : null;
    }
}
