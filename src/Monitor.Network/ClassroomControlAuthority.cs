// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monitor.Infrastructure.Identity;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

public sealed record ClassroomScheduledControl(Guid ChangeId, ulong BaseConfigurationRevision, ulong CommitSeq,
    long EffectiveSimTimeNs, string PublicDiffSha256, string PreparationPolicy, string State, string[] AllowedActionIds)
{
    public ClassroomConfigurationContent? Content { get; init; }
    public bool RequiresTeacherConfirmation { get; init; }
}
internal sealed record ClassroomControlPlan(ClassroomScheduledControl Change, Guid[] Required, Guid[] Prepared, int Quorum)
{
    public bool Confirmed { get; init; } = true;
    public Dictionary<Guid, ClassroomConfigurationAck> Acknowledgements { get; init; } = [];
}

internal sealed partial class ClassroomControlAuthority
{
    private readonly ClassroomControlStore _store;
    private readonly Guid _sessionId;
    private readonly Guid _instanceId;
    internal Guid TeacherId { get; }
    internal ulong CommitSequence => _state.Commit;
    internal ulong ConfigurationRevision => _state.ConfigRevision;
    internal string? PlanState => _state.Plan?.Change.State;
    private ClassroomControlRecord _state;
    private IClassroomActionReducer _reducer = new ClassroomControlDevice();
    private long _simTimeNs;
    internal bool HasHeldAction => _state.Held is not null;
    internal long? HeldDeadline => _state.Held?.ExpiresAtSafetyNs;

    // The owning host serializes access to the authority, wave frontier and device projection under the same lock.
    internal ClassroomControlAuthority(Guid sessionId, Guid instanceId, Guid teacherId, string path, IIdentityPayloadProtector protector)
    {
        _sessionId = sessionId;
        _instanceId = instanceId;
        TeacherId = teacherId;
        _store = new ClassroomControlStore(path, protector, sessionId, instanceId);
        _state = _store.LoadOrCreate(new ClassroomControlRecord(0, 0, 0, false, 1, 0, 1, null, _reducer.ProfileId, _reducer.InitialState,
            ClassroomControlCatalog.Catalog.Keys.Order(StringComparer.Ordinal).ToArray(), null, null));
        _simTimeNs = _state.FrontierSimTimeNs;
    }

    internal ClassroomControlSnapshot Snapshot()
    {
        if (_state.Profile != _reducer.ProfileId) { throw new InvalidOperationException("The saved device profile must be bound before use."); }
        JsonElement device = _reducer is ClassroomControlDevice
            ? ClassroomControlDevice.Project(_state.Device, _simTimeNs) : _state.Device.Clone();
        return new(_state.Revision, _state.Commit, _state.ConfigRevision, _state.Profile, device, (string[])_state.Allowed.Clone());
    }

    internal ClassroomActionProjection Projection(string actionId, Guid actor)
    {
        ClassroomControlAction action = ClassroomControlCatalog.Catalog[actionId];
        string? reason = actor != TeacherId && actor != _state.Operator ? "OPERATOR_REQUIRED" :
            !_state.Allowed.Contains(actionId) ? "ACTION_NOT_ALLOWED" :
            !Supported(action) ? "DEVICE_ACTION_UNAVAILABLE" : null;
        return new("ActionProjection@1", action.Id, _state.Revision, reason is null ? "Enabled" : "Disabled",
            action.Interaction, action.Parameters, reason, "action." + action.Id);
    }

    private bool Supported(ClassroomControlAction action) => _state.Profile == _reducer.ProfileId && _reducer.Supports(action.Id) &&
        (action.Interaction != "PressHoldRelease" || _reducer is IClassroomMomentaryActionReducer);

    internal void ForkFrom(ClassroomControlAuthority shared, Guid studentId, long simTimeNs)
    {
        ArgumentNullException.ThrowIfNull(shared);
        if (_state.Generation != 0 || _sessionId != shared._sessionId || _instanceId == shared._instanceId ||
            studentId == Guid.Empty || studentId == TeacherId || shared._state.PrivateBranch ||
            shared._state.Profile != new ClassroomControlDevice().ProfileId || shared._state.Held is not null ||
            shared._state.Plan?.Change.State is "Scheduled" or "Prepared" || shared._state.Queued.Length != 0 || simTimeNs < shared._simTimeNs)
        { throw new InvalidOperationException("The control state cannot be forked at this boundary."); }
        _simTimeNs = simTimeNs;
        JsonElement device = ClassroomControlDevice.Advance(shared._state.Device, simTimeNs) ?? shared._state.Device.Clone();
        ClassroomConfigurationContent? content = shared._state.ActiveContent?.Snapshot();
        if (content is not null)
        {
            content = content with
            {
                NamedProjections = content.NamedProjections.ToDictionary(pair => pair.Key,
                pair => ClassroomHost.BindProjection(pair.Value, shared._instanceId, _instanceId), StringComparer.Ordinal)
            };
        }
        Save(_state with
        {
            PrivateBranch = true,
            Operator = studentId,
            Revision = shared._state.Revision,
            ConfigRevision = shared._state.ConfigRevision,
            ActiveContent = content,
            Device = device,
            Allowed = (string[])shared._state.Allowed.Clone()
        });
    }

    internal void RetireBranch(bool archive, Action<SqliteConnection, SqliteTransaction>? writeArchive = null)
    {
        if (!_state.PrivateBranch) { throw new InvalidOperationException("Only a forked private control scope can retire."); }
        _store.Retire(archive, writeArchive);
    }

    internal void BindReducer(IClassroomActionReducer reducer, long safetyTimeNs)
    {
        ArgumentNullException.ThrowIfNull(reducer);
        if (!ClassroomControlCatalog.Stable(reducer.ProfileId)) { throw new ArgumentException("Invalid device profile.", nameof(reducer)); }
        if (_state.Profile != reducer.ProfileId)
        {
            // Changing a populated classroom's profile would erase committed device state.
            if (_state.Commit != 0) { throw new InvalidOperationException("The device profile is already bound."); }
            JsonElement initial = ValidState(reducer.InitialState);
            Save(_state with { Profile = reducer.ProfileId, Device = initial, Revision = checked(_state.Revision + 1) });
        }
        _reducer = reducer;
        CancelInteractions(safetyTimeNs);
    }

    internal void Designate(Guid? actor, long safetyTimeNs)
    {
        if (actor == Guid.Empty || actor == TeacherId) { throw new ArgumentException("Invalid designated endpoint.", nameof(actor)); }
        if (_state.Operator == actor) { return; }
        var next = WithoutInteraction(safetyTimeNs);
        Save(next with { Operator = actor, Revision = checked(_state.Revision + 1) });
    }

    internal void ReleaseDesignation(Guid studentId, long now)
    {
        if (_state.Operator == studentId) { Designate(null, now); }
    }

    internal ClassroomActionResult Submit(Guid actor, ClassroomActionIntent intent, long safetyTimeNs, bool connected, out bool newlyCommitted, bool simRunning = true)
    {
        newlyCommitted = false;
        Expire(safetyTimeNs);
        if (!ClassroomControlCatalog.ValidIntent(intent)) { throw new InvalidDataException("Invalid LearnerActionIntent@1."); }
        if (intent.ClientId != actor || intent.SessionId != _sessionId || intent.InstanceId != _instanceId)
        { throw new UnauthorizedAccessException("The action identity does not match its authenticated scope."); }
        ClassroomControlReceipt? saved = _store.Lookup(actor, intent.IntentId, intent.IdempotencyKey);
        if (saved is not null)
        {
            bool identical = (saved.Intent with { Parameters = intent.Parameters }) == intent &&
                JsonElement.DeepEquals(saved.Intent.Parameters, intent.Parameters);
            return identical ? saved.Result : Reject(intent, "IDEMPOTENCY_CONFLICT", "/idempotency_key");
        }
        if (actor != TeacherId && actor != _state.Operator) { return Reject(intent, "OPERATOR_REQUIRED", "/client_id"); }

        if (_state.ReceiptCount >= 65536) { return Reject(intent, "ACTION_JOURNAL_FULL", "/idempotency_key"); }
        JsonElement nextDevice;
        ClassroomActionResult result;
        var next = _state;
        if (intent.ProjectionRevision != _state.Revision)
        {
            result = Reject(intent, "STALE_PROJECTION", "/projection_revision");
        }
        else if (Projection(intent.ActionId, actor).DisabledReasonCode is { } reason)
        {
            result = Reject(intent, reason, "/action_id");
        }
        else
        {
            try
            {
                ClassroomHeldAction? held = CheckInteraction(actor, intent, safetyTimeNs, connected, simRunning);
                nextDevice = ValidState(_reducer.Reduce(_state.Device, intent, _simTimeNs, safetyTimeNs));
                ulong revision = checked(_state.Revision + 1), commit = checked(_state.Commit + 1);
                result = new("LearnerActionCommitted@1", intent.IntentId, intent.IdempotencyKey, true, intent.ActionId,
                    revision, commit, _simTimeNs, null);
                next = next with { Revision = revision, Commit = commit, Device = nextDevice, Held = held };
            }
            catch (ClassroomActionDeniedException denied)
            {
                string reasonCode = denied.ReasonCode is { Length: >= 2 and <= 64 } code && char.IsAsciiLetterUpper(code[0]) &&
                    code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_') ? code : "DEVICE_ACTION_UNAVAILABLE";
                result = Reject(intent, reasonCode, "/parameters");
            }
            catch (OverflowException) { result = Reject(intent, "PARAMETER_OUT_OF_RANGE", "/parameters"); }
        }
        Save(next with { ReceiptCount = checked(_state.ReceiptCount + 1) }, new(intent, result));
        newlyCommitted = result.Accepted;
        return result;
    }

    private ClassroomHeldAction? CheckInteraction(Guid actor, ClassroomActionIntent action, long now, bool connected, bool simRunning)
    {
        if (ClassroomControlCatalog.Catalog[action.ActionId].Interaction != "PressHoldRelease")
        {
            if (_state.Held is not null) { throw new ClassroomActionDeniedException("INTERACTION_ACTIVE"); }
            return null;
        }
        if (!connected) { throw new ClassroomActionDeniedException("CONTROL_CONNECTION_REQUIRED"); }
        if (!simRunning) { throw new ClassroomActionDeniedException("CLASSROOM_PAUSED"); }
        Guid interactionId = action.Parameters.GetProperty("interaction_id").GetGuid();
        int separator = action.ActionId.LastIndexOf('.');
        string family = action.ActionId[..separator];
        string phase = action.ActionId[(separator + 1)..];
        if (phase == "press" && _state.Held is null)
        { return new ClassroomHeldAction(actor, interactionId, family, checked(now + 1_000_000_000)); }
        if (_state.Held is not { } held || held.ClientId != actor || held.InteractionId != interactionId || held.Family != family)
        { throw new ClassroomActionDeniedException("INTERACTION_MISMATCH"); }
        return phase switch
        {
            "renew" => held with { ExpiresAtSafetyNs = checked(now + 1_000_000_000) },
            "release" => null,
            _ => throw new ClassroomActionDeniedException("INTERACTION_ACTIVE")
        };
    }

    internal void Expire(long now)
    {
        if (_state.Held?.ExpiresAtSafetyNs <= now) { CancelInteractions(now); }
    }

    internal void Disconnect(Guid clientId, long now)
    {
        if (_state.Held?.ClientId == clientId) { CancelInteractions(now); }
    }

    internal void CancelInteractions(long now)
    {
        if (_state.Held is not null) { Save(WithoutInteraction(now)); }
    }

    private ClassroomControlRecord WithoutInteraction(long now)
    {
        if (_state.Held is not { } held) { return _state; }
        if (_state.Profile != _reducer.ProfileId || _reducer is not IClassroomMomentaryActionReducer momentary)
        { throw new InvalidOperationException("The saved device reducer is required to cancel the interaction."); }
        JsonElement released = ValidState(momentary.CancelInteraction(_state.Device, held.ClientId, held.InteractionId, now));
        return _state with { Held = null, Device = released, Revision = checked(_state.Revision + 1), Commit = checked(_state.Commit + 1) };
    }

    internal ClassroomActionResult? Lookup(Guid actor, Guid intent) => _store.Lookup(actor, intent, null)?.Result;

    internal ClassroomScheduledControl? Pending() => _state.Plan is { Change: var c }
        ? CopyChange(c) : null;

    internal ClassroomScheduledControl Schedule(Guid changeId, ulong basedOn, long effectiveSimTimeNs,
        IReadOnlyList<string> allowed, IReadOnlyList<Guid> required, int quorum)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(required);
        if (changeId == Guid.Empty || basedOn != _state.ConfigRevision || (Int128)effectiveSimTimeNs - _simTimeNs < 1_000_000_000 ||
            effectiveSimTimeNs % WaveformBlockAssembler.BlockDurationNs != 0 || allowed.Count > ClassroomControlCatalog.Catalog.Count ||
            allowed.Any(id => id is null || !ClassroomControlCatalog.Catalog.ContainsKey(id)) || allowed.Distinct().Count() != allowed.Count ||
            required.Count > 36 || required.Contains(Guid.Empty) || required.Distinct().Count() != required.Count || quorum < 0 || quorum > required.Count)
        { throw new ArgumentException("Invalid action policy scope, revision, block boundary or preparation quorum."); }
        if (PendingChange(_state.Plan) || _state.Queued.Length != 0 || AllPlans().Any(p => p.Change.ChangeId == changeId))
        { throw new InvalidOperationException("A conflicting policy change already exists."); }
        string[] ids = allowed.Order(StringComparer.Ordinal).ToArray();
        string hash = ComputePolicyHash(ids);
        ulong commit = checked(_state.Commit + 1);
        var change = new ClassroomScheduledControl(changeId, basedOn, commit, effectiveSimTimeNs, hash,
            quorum == 0 ? "BestEffortTeachingOnly" : quorum == required.Count ? "RequirePreparedAll" : "RequirePreparedQuorum",
            quorum == 0 ? "Prepared" : "Scheduled", ids);
        Save(_state with { Commit = commit, Plan = new(change, required.ToArray(), [], quorum), Finished = Archive(_state.Plan) });
        return Pending()!;
    }

    internal static string ComputePolicyHash(IEnumerable<string> sortedIds) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes("ClassroomActionAllowlist@1\n" + string.Join('\n', sortedIds))));

    internal bool Prepare(Guid actor, Guid changeId, string hash) =>
        Acknowledge(actor, new ClassroomConfigurationAck(changeId, hash, ClassroomAckStage.Prepared, null, null, null), 0, _simTimeNs);

    internal bool Cancel(Guid changeId)
    {
        var plan = AllPlans().SingleOrDefault(p => p.Change.ChangeId == changeId);
        if (!PendingChange(plan)) { return false; }
        ClassroomControlPlan CancelLater(ClassroomControlPlan p) => PendingChange(p) && p.Change.EffectiveSimTimeNs >= plan!.Change.EffectiveSimTimeNs
            ? p with { Change = p.Change with { State = "Cancelled" } } : p;
        ClassroomControlPlan[] adjusted = _state.Queued.Select(CancelLater).ToArray();
        Save(_state with
        {
            Commit = checked(_state.Commit + 1),
            Plan = _state.Plan is null ? null : CancelLater(_state.Plan),
            Finished = _state.Finished.Concat(adjusted.Where(p => !PendingChange(p))).TakeLast(8).ToArray(),
            Queued = adjusted.Where(PendingChange).ToArray()
        });
        return true;
    }

    internal void Advance(long simTimeNs, long safetyTimeNs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(simTimeNs, _simTimeNs);
        _simTimeNs = simTimeNs;
        PromoteQueued();
        Expire(safetyTimeNs);
        if (_reducer is ClassroomControlDevice && _state.Profile == _reducer.ProfileId &&
            ClassroomControlDevice.Advance(_state.Device, simTimeNs) is { } advanced)
        {
            Save(_state with { Device = advanced, Revision = checked(_state.Revision + 1), Commit = checked(_state.Commit + 1) });
        }
        if (_state.Plan is not { } p || p.Change.State is not ("Scheduled" or "Prepared") ||
            p.Change.EffectiveSimTimeNs > simTimeNs) { return; }
        var released = WithoutInteraction(safetyTimeNs);
        bool activate = p.Change.State == "Prepared" && p.Confirmed && p.Change.BaseConfigurationRevision == _state.ConfigRevision;
        Save(released with
        {
            Plan = p with { Change = p.Change with { State = activate ? "Committed" : "Rejected" } },
            Device = activate && p.Change.Content?.NamedProjections.TryGetValue("display.settings", out var settings) == true
                ? ClassroomControlDevice.ConfigureDisplay(released.Device, settings) : released.Device,
            ConfigRevision = activate ? checked(_state.ConfigRevision + 1) : _state.ConfigRevision,
            ActiveContent = activate ? p.Change.Content ?? (_state.ActiveContent is { } active ? active with { AllowedActionIds = p.Change.AllowedActionIds } : null) : _state.ActiveContent,
            Allowed = activate ? p.Change.AllowedActionIds : _state.Allowed,
            Revision = activate ? checked(released.Revision + 1) : released.Revision,
            Commit = checked(_state.Commit + 1)
        });
    }

    private static JsonElement ValidState(JsonElement state)
    {
        if (state.ValueKind != JsonValueKind.Object || JsonSerializer.SerializeToUtf8Bytes(state).Length > 8192)
        { throw new InvalidDataException("The device projection exceeded the classroom limits."); }
        return state.Clone();
    }

    private ClassroomActionResult Reject(ClassroomActionIntent intent, string code, string field)
    {
        var rejection = new ClassroomActionRejection("DomainRejection@1", code, intent.ActionId, field, _state.Revision,
            code == "STALE_PROJECTION", "action.rejected", intent.IntentId);
        return new("LearnerActionCommitted@1", intent.IntentId, intent.IdempotencyKey, false, intent.ActionId,
            _state.Revision, null, null, rejection);
    }

    private void Save(ClassroomControlRecord next, ClassroomControlReceipt? receipt = null)
    {
        var versioned = next with { Generation = checked(_state.Generation + 1), FrontierSimTimeNs = _simTimeNs };
        _store.Commit(versioned, receipt);
        _state = versioned;
    }
}
