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

// Named timelines are authoring drafts. Runtime branch selection belongs to the session.
internal sealed partial class PatientTimelineSettings : UserControl
{
    internal static readonly IBrush CriticalText = Brush.Parse("#B42318");
    private static readonly IBrush CriticalBackground = Brush.Parse("#FDECEA");
    private static readonly IBrush CautionText = Brush.Parse("#7A4A00");
    // Narrower workspaces show either the graph or the inspector instead of squeezing both.
    private const double SideBySideWidth = 860;

    private sealed class TimelineDraft
    {
        internal TimelineDraft(string id, PatientTimelineEventSettings events, PatientTimelineEventSettings.EventEditor initialState)
        {
            Id = id;
            Events = events;
            InitialState = initialState;
            Panel = SettingsScroll.Create(Properties);
        }

        internal string Id { get; }
        internal PatientTimelineEventSettings Events { get; }
        internal PatientTimelineEventSettings.EventEditor InitialState { get; }
        internal TextBox Name { get; } = new() { MaxLength = 80, MinHeight = 40 };
        internal CheckBox AllowShock { get; } = new() { IsChecked = true };
        internal CheckBox AllowPacing { get; } = new() { IsChecked = true };
        internal ComboBox[] Routes { get; } = [Route(), Route(), Route()];
        internal string?[] RouteIds { get; set; } = [];
        // Entry details: initial patient state and rules in force until a node updates them.
        internal StackPanel Properties { get; } = new() { Spacing = 16, Margin = new Thickness(20) };
        internal Expander TreatmentSection { get; } = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        internal ScrollViewer Panel { get; }
        private static ComboBox Route() => new() { MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Stretch };
    }

    private readonly DesktopLocalization _localization;
    private readonly List<TimelineDraft> _timelines = [];
    private readonly ContentControl _detail = new();
    private readonly Grid _workspace = new();
    private readonly Grid _graphWorkspace = new() { RowDefinitions = new("Auto,*") };
    private readonly ContentControl _detailDraft = new();
    private readonly Border _inspector = new()
    {
        Background = DesktopFluentStyle.Surface,
        BorderBrush = DesktopFluentStyle.Stroke,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        ClipToBounds = true
    };
    private readonly Border _inspectorFooter = new() { BorderBrush = DesktopFluentStyle.Stroke, BorderThickness = new Thickness(0, 1, 0, 0) };
    private readonly Border _statusBar = new()
    {
        BorderBrush = DesktopFluentStyle.Stroke,
        BorderThickness = new Thickness(0, 1, 0, 0),
        Padding = new Thickness(0, 8),
        Margin = new Thickness(20, 0, 20, 4)
    };
    private readonly TextBlock _inspectorScope = new() { FontSize = 12, Foreground = DesktopFluentStyle.SecondaryText, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _inspectorTitle = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _position = new()
    {
        MinWidth = 48,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = DesktopFluentStyle.SecondaryText
    };
    private bool _selectingGraph;
    private bool _entrySelected = true;
    // Only meaningful in a narrow workspace, where details replace the graph.
    private bool _detailOpen;
    internal PatientTimelineGraph Graph { get; }
    internal Button AddNode { get; } = Button();
    internal Button RemoveNode { get; } = DestructiveButton();
    internal Button MoveEarlier { get; } = Button();
    internal Button MoveLater { get; } = Button();
    internal Button BackToGraph { get; } = Button();
    internal Button PreviousNode { get; } = Button();
    internal Button NextNode { get; } = Button();
    internal Button TriggerManual { get; } = Button();
    private string _initialTimelineId = "main";
    private PatientTimelineProgram? _applied;
    private bool _restoring;
    private bool _refreshingChoices;
    internal event Action? DraftChanged;
    private string _selectedTimelineId = "main";
    internal Button AddTimeline { get; } = Button();
    internal Button RemoveTimeline { get; } = DestructiveButton();
    internal TextBlock Status { get; } = new() { FontSize = 12, Foreground = CautionText, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    internal TextBlock Progress { get; } = new() { FontSize = 12, Foreground = DesktopFluentStyle.SecondaryText, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    internal TextBlock Error { get; } = new() { Foreground = CriticalText, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    internal bool InspectorVisible => _inspector.IsVisible;
    internal string InspectorTitle => _inspectorTitle.Text ?? "";
    private TimelineDraft Initial => _timelines.Single(item => item.Id == _initialTimelineId);
    private TimelineDraft Active => _timelines.FirstOrDefault(item => item.Id == _selectedTimelineId) ?? Initial;
    private bool ShowsLine => _entrySelected || SelectedEditor is null;
    internal string SelectedTimelineId => Active.Id;
    internal PatientTimelineEventSettings ActiveEvents => Active.Events;
    internal PatientTimelineEventSettings.EventEditor EntryState => Active.InitialState;
    internal TextBox TimelineName => Active.Name;
    internal CheckBox AllowShock => Active.AllowShock;
    internal CheckBox AllowPacing => Active.AllowPacing;
    internal ComboBox ShockSuccessTimeline => Active.Routes[0];
    internal ComboBox ShockFailureTimeline => Active.Routes[1];
    internal ComboBox PacingTimeline => Active.Routes[2];
    internal ListBox Events => Active.Events.Events;
    internal Button Add => Active.Events.Add;
    internal Button Remove => Active.Events.Remove;
    internal IReadOnlyList<PatientTimelineEventSettings.EventEditor> Editors => Initial.Events.Editors;
    internal PatientTimelineEventSettings.EventEditor? SelectedEditor => Active.Events.SelectedEditor;

    internal PatientTimelineSettings(DesktopLocalization localization)
    {
        _localization = localization;
        Graph = new(localization);
        var root = new Grid { RowDefinitions = new("Auto,Auto,*,Auto") };

        root.Children.Add(BuildFileCommands());

        // Authoring and view commands stay with their canvas, aligned with the inspector.
        var commands = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        Graph.ZoomControls.Margin = new Thickness(12, 0, 0, 0);
        Graph.ZoomControls.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(Graph.ZoomControls, 1);
        commands.Children.Add(Graph.ZoomControls);
        var create = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        create.Children.Add(AddNode);
        create.Children.Add(AddTimeline);
        commands.Children.Add(create);
        _graphWorkspace.Children.Add(commands);
        Grid.SetRow(Graph, 1);
        _graphWorkspace.Children.Add(Graph);

        // Runtime information is separate from the selected authoring file.
        var strip = new DockPanel();
        TriggerManual.Classes.Add("accent");
        TriggerManual.Margin = new Thickness(12, 0, 0, 0);
        TriggerManual.IsVisible = false;
        DockPanel.SetDock(TriggerManual, Dock.Right);
        strip.Children.Add(TriggerManual);
        var runState = new StackPanel { Spacing = 4 };
        runState.Children.Add(_detailDraft);
        runState.Children.Add(Progress);
        strip.Children.Add(runState);
        _statusBar.Child = strip;
        Grid.SetRow(_statusBar, 3);
        root.Children.Add(_statusBar);
        foreach (var source in new Control[] { Status, Progress, TriggerManual })
        {
            source.PropertyChanged += (_, change) =>
            {
                if (change.Property == IsVisibleProperty) { RefreshStatusBar(); }
            };
        }
        var error = Notice(Error);
        error.Margin = new Thickness(20, 10, 20, 0);
        Grid.SetRow(error, 1);
        root.Children.Add(error);

        _inspector.Child = BuildInspector();
        _workspace.Margin = new Thickness(20, 12, 20, 12);
        _workspace.Children.Add(_graphWorkspace);
        _workspace.Children.Add(_inspector);
        Grid.SetRow(_workspace, 2);
        root.Children.Add(_workspace);
        _workspace.SizeChanged += (_, _) => ArrangeWorkspace();
        Content = root;

        Caption(AddTimeline, "timeline.addBranch", "+");
        Caption(AddNode, "timeline.add", "+");
        localization.Bind(TriggerManual, AutomationProperties.NameProperty, "timeline.enterManual");
        AddTimeline.Click += (_, _) => AddBranch();
        RemoveTimeline.Click += (_, _) => RemoveBranch();
        AddNode.Click += (_, _) => AddEvent();
        RemoveNode.Click += (_, _) => RemoveSelected();
        MoveEarlier.Click += (_, _) => Active.Events.MoveSelected(-1);
        MoveLater.Click += (_, _) => Active.Events.MoveSelected(1);
        BackToGraph.Click += (_, _) =>
        {
            _detailOpen = false;
            ArrangeWorkspace();
        };
        PreviousNode.Click += (_, _) => Step(-1);
        NextNode.Click += (_, _) => Step(1);
        Graph.Selected += SelectGraphNode;
        Graph.RouteSelected += (_, key) =>
        {
            if (key is PatientTimelineEventSettings.EventEditor editor) { editor.RevealTreatment(); }
            else
            {
                Active.TreatmentSection.IsExpanded = true;
                Dispatcher.UIThread.Post(() => Active.TreatmentSection.BringIntoView(), DispatcherPriority.Loaded);
            }
        };
        localization.LocaleChanged += RefreshGraph;
        Restore([]);
        InitializeFiles();
        RefreshStatusBar();
    }

    internal void AddEvent()
    {
        bool atStart = _entrySelected;
        _entrySelected = false;
        Active.Events.AddEvent(atStart);
        _detailOpen = true;
        RefreshGraph();
        Graph.Select(Active.Id, SelectedEditor);
        ShowActive();
        ArrangeWorkspace();
        Graph.RevealSelection();
        if (SelectedEditor is { } editor) { FocusName(editor.EventName); }
    }

    internal void RemoveSelected()
    {
        Active.Events.RemoveSelected();
        if (SelectedEditor is null) { _entrySelected = true; }
        RefreshGraph();
        Graph.Select(Active.Id, ShowsLine ? null : SelectedEditor);
        ShowActive();
    }
    internal IReadOnlyList<PatientTimelineEvent> Read() => Initial.Events.Read();

    internal PatientTimelineProgram ReadProgram()
    {
        var program = new PatientTimelineProgram(_initialTimelineId, _timelines.Select(draft =>
            new PatientTimelineDefinition(draft.Id, draft.Name.Text?.Trim() ?? "", draft.Events.Read(),
                new PatientTimelineTreatmentPolicy(draft.AllowShock.IsChecked == true, draft.AllowPacing.IsChecked == true,
                    RouteId(draft, 0), RouteId(draft, 1), RouteId(draft, 2)))
            { InitialState = draft.InitialState.ReadInitialState() }).ToArray());
        return program.Snapshot();
    }

    internal void Restore(IReadOnlyList<PatientTimelineEvent> events) => RestoreProgram(new("main",
        [new("main", _localization.Get("timeline.main"), PatientTimelineEvent.Snapshot(events), new())]));

    internal void RestoreProgram(PatientTimelineProgram program)
    {
        var owned = program.Snapshot();
        _restoring = true;
        _entrySelected = true;
        _detailOpen = false;
        _initialTimelineId = owned.InitialTimelineId;
        var drafts = owned.Timelines.Select(CreateDraft).ToArray();
        _timelines.Clear();
        _timelines.AddRange(drafts);
        RefreshChoices(_initialTimelineId, owned.Timelines.ToDictionary(item => item.Id, item => item.Treatment, StringComparer.Ordinal));
        _restoring = false;
        if (!_loadingFile) { MarkApplied(owned); }
        Graph.ScheduleInitialView();
        RefreshGraph();
        Graph.Select(_initialTimelineId, null);
        ShowActive();
        ArrangeWorkspace();
    }

    internal void MarkApplied(PatientTimelineProgram? program = null)
    {
        _applied = (program ?? ReadProgram()).Snapshot();
        _appliedFileId = SelectedFileId;
        _appliedEnabled = TimelineEnabled.IsChecked == true;
        RefreshDraftStatus();
    }

    internal void MarkApplied(IReadOnlyList<PatientTimelineEvent> events)
    {
        var program = ReadProgram();
        MarkApplied(program with
        {
            Timelines = program.Timelines.Select(item => item.Id == program.InitialTimelineId
            ? item with { Events = PatientTimelineEvent.Snapshot(events) } : item).ToArray()
        });
    }

    internal void AddBranch()
    {
        if (_timelines.Count >= PatientTimelineProgram.MaximumTimelines) { return; }
        var draft = CreateDraft(new(Guid.NewGuid().ToString("N"),
            _localization.Format("timeline.newBranch", _timelines.Count), [], new()));
        _timelines.Add(draft);
        _entrySelected = true;
        _detailOpen = true;
        RefreshChoices(draft.Id);
        Changed();
        Graph.Select(draft.Id, null);
        ArrangeWorkspace();
        Graph.RevealSelection();
        FocusName(draft.Name);
    }

    internal void RemoveBranch()
    {
        var active = Active;
        if (active.Id == _initialTimelineId) { return; }
        if (_timelines.Any(draft => Enumerable.Range(0, 3).Any(index => RouteId(draft, index) == active.Id) ||
            draft.Events.Editors.Any(editor => Routes(editor.Treatment.Read()).Any(route => route.TargetLineId == active.Id))))
        {
            Error.IsVisible = true;
            _localization.Bind(Error, TextBlock.TextProperty, "timeline.branchReferenced", active.Name.Text);
            return;
        }
        _timelines.Remove(active);
        _entrySelected = true;
        RefreshChoices(_initialTimelineId);
        Changed();
        Graph.Select(_initialTimelineId, null);
    }

    private TimelineDraft CreateDraft(PatientTimelineDefinition definition)
    {
        var events = new PatientTimelineEventSettings(_localization, definition.Id != _initialTimelineId, showStatus: false);
        events.Restore(definition.Events);
        events.Progress.IsVisible = false;
        events.UseGraphInspector();
        var initialState = new PatientTimelineEventSettings.EventEditor(_localization, entryState: true);
        if (definition.InitialState is { } state) { initialState.Restore(state); }
        var draft = new TimelineDraft(definition.Id, events, initialState);
        draft.Name.Text = definition.Name;
        draft.AllowShock.IsChecked = definition.Treatment.AllowShock;
        draft.AllowPacing.IsChecked = definition.Treatment.AllowPacing;
        AddField(draft.Properties, "timeline.lineName", draft.Name);
        draft.Properties.Children.Add(initialState);
        var rules = new StackPanel { Spacing = 10 };
        _localization.Bind(draft.AllowShock, ContentControl.ContentProperty, "timeline.allowShock");
        _localization.Bind(draft.AllowPacing, ContentControl.ContentProperty, "timeline.allowPacing");
        rules.Children.Add(draft.AllowShock);
        rules.Children.Add(draft.AllowPacing);
        AddField(rules, "timeline.shockSuccess", draft.Routes[0]);
        AddField(rules, "timeline.shockFailure", draft.Routes[1]);
        AddField(rules, "timeline.pacingStarted", draft.Routes[2]);
        draft.TreatmentSection.Content = rules;
        _localization.Bind(draft.TreatmentSection, HeaderedContentControl.HeaderProperty, "timeline.entryTreatment");
        draft.Properties.Children.Add(draft.TreatmentSection);
        draft.Name.TextChanged += (_, _) =>
        {
            if (_restoring || _refreshingChoices) { return; }
            RefreshChoices(Active.Id);
            Changed();
        };
        draft.AllowShock.IsCheckedChanged += (_, _) => Changed();
        draft.AllowPacing.IsCheckedChanged += (_, _) => Changed();
        foreach (var route in draft.Routes)
        { route.SelectionChanged += (_, _) => { if (!_refreshingChoices) { Changed(); } }; }
        events.DraftChanged += Changed;
        initialState.DraftChanged += Changed;
        events.SelectionChanged += () =>
        {
            if (_restoring || _refreshingChoices || _selectingGraph) { return; }
            _entrySelected = false;
            Graph.Select(draft.Id, events.SelectedEditor);
            ShowActive();
        };
        return draft;
    }

    private void RefreshChoices(string selectedId, Dictionary<string, PatientTimelineTreatmentPolicy>? restoredPolicies = null)
    {
        _refreshingChoices = true;
        _selectedTimelineId = selectedId;
        foreach (var draft in _timelines)
        {
            string?[] targets = restoredPolicies is not null && restoredPolicies.TryGetValue(draft.Id, out var policy)
                ? [policy.ShockSuccessTimelineId, policy.ShockFailureTimelineId, policy.PacingTimelineId]
                : Enumerable.Range(0, 3).Select(index => RouteId(draft, index)).ToArray();
            draft.RouteIds = _timelines.Select(item => (string?)item.Id).Prepend(null).ToArray();
            for (int index = 0; index < draft.Routes.Length; index++)
            {
                _localization.SetChoices(draft.Routes[index], _timelines.Select(item =>
                    (Func<Monitor.Application.Localization.ITextLocalizer, string>)(_ => item.Name.Text ?? ""))
                    .Prepend(text => text.GetString("timeline.continueCurrent")));
                draft.Routes[index].SelectedIndex = Array.IndexOf(draft.RouteIds, targets[index]);
            }
        }
        foreach (var draft in _timelines)
        {
            foreach (var editor in draft.Events.Editors)
            { editor.Treatment.SetTimelines(_timelines.Select(item => (item.Id, item.Name.Text ?? "")).ToArray()); }
        }
        _refreshingChoices = false;
        ShowActive();
        RefreshGraph();
    }

    private Grid BuildInspector()
    {
        // A navigation-bar back button; it exists only while details replace the graph.
        Caption(BackToGraph, "timeline.returnGraph", "‹");
        BackToGraph.Padding = new Thickness(8, 0);
        BackToGraph.Margin = new Thickness(0, 0, 8, 0);
        BackToGraph.VerticalAlignment = VerticalAlignment.Center;
        BackToGraph.Background = Brushes.Transparent;
        BackToGraph.BorderBrush = Brushes.Transparent;
        BackToGraph.Foreground = DesktopFluentStyle.Accent;
        foreach (var (button, symbol, key) in new[] { (PreviousNode, "‹", "timeline.previousNode"), (NextNode, "›", "timeline.nextNode") })
        {
            button.Content = symbol;
            button.FontSize = 18;
            button.Padding = new Thickness(0);
            button.MinWidth = 40;
            _localization.Bind(button, AutomationProperties.NameProperty, key);
            _localization.Bind(button, ToolTip.TipProperty, key);
        }
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        navigation.Children.Add(PreviousNode);
        navigation.Children.Add(_position);
        navigation.Children.Add(NextNode);
        var titles = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        titles.Children.Add(_inspectorScope);
        titles.Children.Add(_inspectorTitle);
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new Thickness(12, 10, 12, 10) };
        header.Children.Add(BackToGraph);
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        Grid.SetColumn(navigation, 2);
        header.Children.Add(navigation);

        // Actions on the selected item stay with its details, destructive ones at the far end.
        Caption(MoveEarlier, "timeline.moveEarlier");
        Caption(MoveLater, "timeline.moveLater");
        Caption(RemoveNode, "timeline.remove");
        Caption(RemoveTimeline, "timeline.removeBranch");
        var footer = new Grid { ColumnDefinitions = new("Auto,Auto,*,Auto,Auto"), Margin = new Thickness(16, 10) };
        MoveLater.Margin = new Thickness(8, 0, 0, 0);
        footer.Children.Add(MoveEarlier);
        Grid.SetColumn(MoveLater, 1);
        footer.Children.Add(MoveLater);
        Grid.SetColumn(RemoveNode, 3);
        footer.Children.Add(RemoveNode);
        Grid.SetColumn(RemoveTimeline, 4);
        footer.Children.Add(RemoveTimeline);
        _inspectorFooter.Child = footer;

        var inspector = new Grid { RowDefinitions = new("Auto,*,Auto") };
        inspector.Children.Add(new Border { BorderBrush = DesktopFluentStyle.Stroke, BorderThickness = new Thickness(0, 0, 0, 1), Child = header });
        Grid.SetRow(_detail, 1);
        inspector.Children.Add(_detail);
        Grid.SetRow(_inspectorFooter, 2);
        inspector.Children.Add(_inspectorFooter);
        return inspector;
    }

    private void ShowActive()
    {
        if (_timelines.Count == 0) { return; }
        Control body = ShowsLine ? Active.Panel : Active.Events;
        if (!ReferenceEquals(_detail.Content, body)) { _detail.Content = body; }
        AddTimeline.IsEnabled = _timelines.Count < PatientTimelineProgram.MaximumTimelines;
        RemoveTimeline.IsEnabled = Active.Id != _initialTimelineId;
        RefreshActions();
    }

    internal void SelectTimeline(string lineId)
    {
        SelectGraphNode(lineId, null);
        Graph.Select(lineId, null);
        Graph.RevealSelection();
    }

    private void SelectGraphNode(string lineId, object? eventKey)
    {
        int index = _timelines.FindIndex(item => item.Id == lineId);
        if (index < 0) { return; }
        _selectingGraph = true;
        _entrySelected = eventKey is null;
        _selectedTimelineId = lineId;
        if (eventKey is PatientTimelineEventSettings.EventEditor editor) { Active.Events.SelectEditor(editor); }
        _selectingGraph = false;
        _detailOpen = true;
        ShowActive();
        ArrangeWorkspace();
    }

    // Steps through the active line in graph order without leaving the details.
    private void Step(int direction)
    {
        var nodes = Nodes(Active);
        int target = NodeIndex(nodes) + direction;
        if (target < 0 || target >= nodes.Count) { return; }
        Graph.Select(Active.Id, nodes[target]);
        SelectGraphNode(Active.Id, nodes[target]);
        Graph.RevealSelection();
    }

    private void ArrangeWorkspace()
    {
        bool sideBySide = _workspace.Bounds.Width >= SideBySideWidth;
        bool graph = sideBySide || !_detailOpen;
        if (_workspace.ColumnDefinitions.Count != (sideBySide ? 2 : 1))
        { _workspace.ColumnDefinitions = new(sideBySide ? "*,380" : "*"); }
        Graph.IsVisible = graph;
        _graphWorkspace.IsVisible = graph;
        Graph.ZoomControls.IsVisible = graph;
        if (_fileCommands is not null) { _fileCommands.IsVisible = graph; }
        // Keep the restart notice visible while narrow-window details replace the file bar.
        var draftHost = graph ? _fileDraft : _detailDraft;
        if (!ReferenceEquals(draftHost.Content, Status))
        {
            _fileDraft.Content = null;
            _detailDraft.Content = null;
            Status.Margin = graph ? new Thickness(16, 0) : default;
            draftHost.Content = Status;
        }
        RefreshStatusBar();
        _inspector.IsVisible = sideBySide || _detailOpen;
        Grid.SetColumn(_inspector, sideBySide ? 1 : 0);
        _inspector.Margin = sideBySide ? new Thickness(12, 0, 0, 0) : default;
        BackToGraph.IsVisible = !sideBySide;
    }

    private void RefreshActions()
    {
        if (_timelines.Count == 0) { return; }
        var active = Active;
        bool line = ShowsLine;
        bool initial = active.Id == _initialTimelineId;
        AddNode.IsEnabled = active.Events.Editors.Count < PatientTimelineEvent.MaximumEvents;
        RemoveNode.IsEnabled = !line;
        int index = line ? -1 : active.Events.Editors.ToList().IndexOf(SelectedEditor!);
        MoveEarlier.IsEnabled = index > 0;
        MoveLater.IsEnabled = index >= 0 && index < active.Events.Editors.Count - 1;
        MoveEarlier.IsVisible = !line;
        MoveLater.IsVisible = !line;
        RemoveNode.IsVisible = !line;
        RemoveTimeline.IsVisible = line && !initial;
        _inspectorFooter.IsVisible = !line || !initial;
        var nodes = Nodes(active);
        int position = NodeIndex(nodes);
        PreviousNode.IsEnabled = position > 0;
        NextNode.IsEnabled = position < nodes.Count - 1;
        _position.Text = $"{position + 1} / {nodes.Count}";
        string lineName = active.Name.Text ?? "";
        _localization.Bind(_inspectorScope, TextBlock.TextProperty, text => line
            ? text.GetString(initial ? "timeline.graph.initial" : "timeline.graph.branch") : lineName);
        _inspectorTitle.Text = line ? lineName : SelectedEditor!.EventName.Text;
    }

    private void RefreshStatusBar() => _statusBar.IsVisible = Progress.IsVisible || TriggerManual.IsVisible ||
        ReferenceEquals(_detailDraft.Content, Status) && Status.IsVisible;

    private void RefreshGraph()
    {
        if (_restoring || _refreshingChoices || _timelines.Count == 0) { return; }
        var choices = _timelines.Select(item => (item.Id, item.Name.Text ?? "")).ToArray();
        var lines = _timelines.Select(draft =>
        {
            var events = OrderedEditors(draft).Select(editor =>
            {
                editor.Treatment.SetTimelines(choices);
                return new PatientTimelineGraphEvent(editor, editor.EventName.Text ?? "", editor.StartSeconds.Value,
                    editor.DurationSeconds.Value, StateSummary(editor), editor.IsValid, Routes(editor.Treatment.Read()),
                    editor.Trigger.SelectedIndex == (int)PatientTimelineTrigger.Manual,
                    editor.Trigger.SelectedIndex == (int)PatientTimelineTrigger.AfterPrevious);
            }).ToArray();
            var treatment = new PatientTimelineTreatmentPolicy(draft.AllowShock.IsChecked == true, draft.AllowPacing.IsChecked == true,
                RouteId(draft, 0), RouteId(draft, 1), RouteId(draft, 2));
            return new PatientTimelineGraphLine(draft.Id, draft.Name.Text ?? "", draft.Id == _initialTimelineId,
                events, Routes(treatment), !string.IsNullOrWhiteSpace(draft.Name.Text) && draft.InitialState.IsValid, StateSummary(draft.InitialState));
        }).ToArray();
        Graph.SetLines(lines);
        RefreshActions();
    }

    private string StateSummary(PatientTimelineEventSettings.EventEditor editor)
    {
        var summary = editor.Targets.Where(item => item.Value.Enabled.IsChecked == true)
            .Select(item => _localization.Get(item.Key switch
            {
                "HeartRate" => "timeline.heartRate",
                "RespiratoryRate" => "timeline.respiratoryRate",
                "Saturation" => "timeline.saturation",
                "EtCo2" => "timeline.etco2",
                "Abp" => "timeline.abp",
                "Pa" => "timeline.pa",
                "Cvp" => "timeline.cvp",
                "Nibp" => "timeline.nibp",
                _ => "timeline.temperature"
            }) + " → " + string.Join("/", item.Value.Values.Select(field => field.Value?.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture) ?? "—"))).ToList();
        if (editor.EcgTemplate.SelectedIndex > 0)
        { summary.Add(_localization.Get(DesignPreviewSettings.EcgTemplateKey(editor.EcgTemplate.SelectedIndex - 1))); }
        if (editor.ChangeVentilation.IsChecked == true) { summary.Add(_localization.Get("timeline.ventilation")); }
        if (editor.Treatment.Override.IsChecked == true) { summary.Add(_localization.Get("timeline.treatmentTab")); }
        return string.Join(" · ", summary);
    }

    // Legacy all-absolute lines read in time order; any relative or manual node keeps authored order.
    private static PatientTimelineEventSettings.EventEditor[] OrderedEditors(TimelineDraft draft) =>
        draft.Events.Editors.All(editor => editor.Trigger.SelectedIndex == (int)PatientTimelineTrigger.TimelineTime)
            ? draft.Events.Editors.OrderBy(editor => editor.StartSeconds.Value ?? decimal.MaxValue).ToArray()
            : draft.Events.Editors.ToArray();

    // The line entry comes first, matching the cards of its graph row.
    private static List<object?> Nodes(TimelineDraft draft) => OrderedEditors(draft).Cast<object?>().Prepend(null).ToList();

    private int NodeIndex(List<object?> nodes) => ShowsLine ? 0 : Math.Max(0, nodes.IndexOf(SelectedEditor));

    private static List<PatientTimelineGraphRoute> Routes(PatientTimelineTreatmentPolicy? policy)
    {
        var routes = new List<PatientTimelineGraphRoute>();
        if (policy?.ShockSuccessTimelineId is { } success) { routes.Add(new(success, PatientTimelineGraphRouteKind.ShockSuccess)); }
        if (policy?.ShockFailureTimelineId is { } failure) { routes.Add(new(failure, PatientTimelineGraphRouteKind.ShockFailure)); }
        if (policy?.PacingTimelineId is { } pacing) { routes.Add(new(pacing, PatientTimelineGraphRouteKind.Pacing)); }
        return routes;
    }

    private static string? RouteId(TimelineDraft draft, int index)
    {
        int selected = draft.Routes[index].SelectedIndex;
        return selected >= 0 && selected < draft.RouteIds.Length ? draft.RouteIds[selected] : null;
    }

    private void Changed()
    {
        if (_restoring || _refreshingChoices) { return; }
        Error.IsVisible = false;
        RefreshDraftStatus();
        RefreshGraph();
        DraftChanged?.Invoke();
    }

    private void RefreshDraftStatus()
    {
        bool changed;
        Error.IsVisible = false;
        try { changed = _applied is null || _appliedFileId != SelectedFileId || _appliedEnabled != (TimelineEnabled.IsChecked == true) || !Equivalent(ReadProgram(), _applied); }
        catch (ArgumentException error)
        {
            changed = true;
            string? key = error.Message switch
            {
                "PatientTimeline.InvalidDefinition" => "timeline.invalidDefinition",
                "PatientTimeline.InvalidBranch" => "timeline.invalidBranch",
                "PatientTimeline.InvalidProgram" => "timeline.invalidProgram",
                "PatientTimeline.InvalidEvent" or "PatientTimeline.InvalidTime" => "timeline.invalidTime",
                _ => null
            };
            if (key is not null)
            {
                Error.IsVisible = true;
                _localization.Bind(Error, TextBlock.TextProperty, key);
            }
        }
        _localization.Bind(Status, TextBlock.TextProperty, text => changed ? text.GetString("timeline.draft") : "");
        Status.IsVisible = changed;
    }

    private static bool Equivalent(PatientTimelineProgram first, PatientTimelineProgram second) =>
        first.InitialTimelineId == second.InitialTimelineId && first.Timelines.Count == second.Timelines.Count &&
        first.Timelines.Zip(second.Timelines).All(pair => pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name &&
            pair.First.Treatment == pair.Second.Treatment && pair.First.InitialState == pair.Second.InitialState &&
            pair.First.Events.SequenceEqual(pair.Second.Events));

    private void AddField(Panel parent, string key, Control field)
    {
        var label = new Label { Target = field, Padding = new Thickness(0) };
        _localization.Bind(label, ContentControl.ContentProperty, key);
        _localization.Bind(field, AutomationProperties.NameProperty, key);
        var group = new StackPanel { Spacing = 6 };
        group.Children.Add(label);
        group.Children.Add(field);
        parent.Children.Add(group);
    }

    // The glyph is decoration; assistive technology reads the plain localized label.
    private void Caption(Button button, string key, string? glyph = null)
    {
        _localization.Bind(button, ContentControl.ContentProperty, text => glyph is null ? text.GetString(key) : glyph + "  " + text.GetString(key));
        _localization.Bind(button, AutomationProperties.NameProperty, key);
    }

    // Created controls are named in place, so typing replaces the generated name.
    private static void FocusName(TextBox name) => Dispatcher.UIThread.Post(() =>
    {
        if (!name.IsEffectivelyVisible) { return; }
        name.Focus();
        name.SelectAll();
    }, DispatcherPriority.Loaded);

    // A Fluent-style critical message bar that is shown exactly while its text is visible.
    internal static Border Notice(TextBlock text)
    {
        var notice = new Border { Background = CriticalBackground, CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8), Child = text };
        Follow(notice, text);
        return notice;
    }

    private static void Follow(Control container, Control source)
    {
        container.IsVisible = source.IsVisible;
        source.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty) { container.IsVisible = source.IsVisible; }
        };
    }

    private static Button Button() => new() { MinHeight = 40, Padding = new Thickness(12, 0), HorizontalContentAlignment = HorizontalAlignment.Center };

    private static Button DestructiveButton()
    {
        var button = Button();
        button.Background = Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent;
        button.Foreground = CriticalText;
        return button;
    }
}
