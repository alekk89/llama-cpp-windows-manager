using System.Globalization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.Localization;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;

[Collection(LocalizationStateTestCollection.Name)]
public sealed class ModelGroupsTests : ManagerRegressionTestBase
{
    [Fact]
    public void ModelGroupRowsExposeCompactReadablePolicyLabels()
    {
        Loc.LoadLanguage("en");
        var inherited = new ModelGroupEditorRow
        {
            RetentionMode = ModelGroupRetentionMode.Inherit,
            IdleMinutes = 30,
            EvictionPriority = ModelGroupEvictionPriority.Normal
        };
        var pinned = new ModelGroupEditorRow
        {
            RetentionMode = ModelGroupRetentionMode.Pinned,
            IdleMinutes = 30,
            EvictionPriority = ModelGroupEvictionPriority.High
        };
        var idle = new ModelGroupEditorRow
        {
            RetentionMode = ModelGroupRetentionMode.IdleTimeout,
            IdleMinutes = 12,
            EvictionPriority = ModelGroupEvictionPriority.Low
        };

        Assert.Equal(("Inherit global", "—", "Normal"),
            (inherited.RetentionLabel, inherited.IdleMinutesLabel, inherited.EvictionPriorityLabel));
        Assert.Equal(("Pinned", "—", "High — unload last"),
            (pinned.RetentionLabel, pinned.IdleMinutesLabel, pinned.EvictionPriorityLabel));
        Assert.Equal(("Idle timeout", "12", "Low — unload first"),
            (idle.RetentionLabel, idle.IdleMinutesLabel, idle.EvictionPriorityLabel));
    }

    [Fact]
    public void ModelGroupNameEditorRejectsMissingDuplicateAndOversizedNames()
    {
        Loc.LoadLanguage("en");
        var rows = new[]
        {
            new ModelGroupEditorRow { EditorKey = "group:interactive", Name = "Interactive" },
            new ModelGroupEditorRow { EditorKey = "pending:batch", Name = "Batch" }
        };

        Assert.Equal("A group name is required.", ModelGroupDialogFactory.ValidateProposedName(rows, "  "));
        Assert.Equal("A group named 'interactive' already exists.", ModelGroupDialogFactory.ValidateProposedName(rows, " interactive "));
        Assert.Equal("Group names must be 80 characters or fewer.", ModelGroupDialogFactory.ValidateProposedName(rows, new string('x', 81)));
        Assert.Null(ModelGroupDialogFactory.ValidateProposedName(rows, "Interactive renamed", "group:interactive"));
        Assert.Null(ModelGroupDialogFactory.ValidateProposedName(rows, "Interactive", "group:interactive"));
    }

    [Fact]
    public async Task ModelGroupsPersistAssignmentsAndResolveEffectiveRetentionPolicy()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "manager.db"));
        await store.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var pinnedModel = new ModelRecord("pinned-model", "Pinned", Path.Combine(root, "pinned.gguf"), OwnershipKind.External, "{}", now);
        var idleModel = new ModelRecord("idle-model", "Idle", Path.Combine(root, "idle.gguf"), OwnershipKind.External, "{}", now);
        await store.UpsertModelAsync(pinnedModel);
        await store.UpsertModelAsync(idleModel);
        var defaults = AppSettings.CreateDefault(root);
        var pinnedProfile = new NamedModelLaunchProfile(
            "default:pinned-model", pinnedModel.Id, "Default", ModelLaunchSettings.FromAppSettings(defaults), now, true);
        var idleProfile = new NamedModelLaunchProfile(
            "default:idle-model", idleModel.Id, "Default", ModelLaunchSettings.FromAppSettings(defaults), now, true);
        await store.SaveNamedModelLaunchProfileAsync(pinnedProfile);
        await store.SaveNamedModelLaunchProfileAsync(idleProfile);
        var service = new ModelGroupService(store);

        var pinned = await service.CreateAsync("Pinned models", ModelGroupRetentionMode.Pinned, 30, ModelGroupEvictionPriority.High);
        var idle = await service.CreateAsync("Batch", ModelGroupRetentionMode.IdleTimeout, 7, ModelGroupEvictionPriority.Low);
        await service.AssignAsync(pinnedProfile.Id, pinned.Id);
        await service.AssignAsync(idleProfile.Id, idle.Name);

        var snapshot = await service.SnapshotAsync();
        Assert.Equal(2, snapshot.Groups.Count);
        Assert.Equal(pinned.Id, snapshot.Assignments[pinnedProfile.Id].GroupId);
        Assert.Equal(idle.Id, snapshot.Assignments[idleProfile.Id].GroupId);

        await service.UnassignAsync(idleProfile.Id);
        snapshot = await service.SnapshotAsync();
        Assert.False(snapshot.Assignments.ContainsKey(idleProfile.Id));
        await service.AssignAsync(idleProfile.Id, idle.Id);
        snapshot = await service.SnapshotAsync();

        var pinnedPolicy = ModelGroupService.EffectivePolicy(snapshot, pinnedProfile.Id, globalIdleMinutes: 20);
        Assert.False(pinnedPolicy.AllowsIdleUnload);
        Assert.Equal(ModelGroupEvictionPriority.High, pinnedPolicy.EvictionPriority);
        var idlePolicy = ModelGroupService.EffectivePolicy(snapshot, idleProfile.Id, globalIdleMinutes: 20);
        Assert.True(idlePolicy.AllowsIdleUnload);
        Assert.Equal(7, idlePolicy.IdleMinutes);
        Assert.Equal(ModelGroupEvictionPriority.Low, idlePolicy.EvictionPriority);
        var inherited = ModelGroupService.EffectivePolicy(snapshot, "unassigned", globalIdleMinutes: 20);
        Assert.True(inherited.AllowsIdleUnload);
        Assert.Equal(20, inherited.IdleMinutes);

        var updated = await service.UpdateAsync(idle.Id, "Interactive", ModelGroupRetentionMode.Inherit, 15, ModelGroupEvictionPriority.Normal);
        Assert.Equal("Interactive", updated.Name);
        Assert.Equal(ModelGroupRetentionMode.Inherit, updated.RetentionMode);

        await service.DeleteAsync(pinned.Id);
        snapshot = await service.SnapshotAsync();
        Assert.DoesNotContain(snapshot.Groups, group => group.Id == pinned.Id);
        Assert.False(snapshot.Assignments.ContainsKey(pinnedProfile.Id));
    }

    [Fact]
    public async Task ReplacingModelGroupsRollsBackTheEntireEditWhenAnyDatabaseWriteFails()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "manager.db"));
        await store.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var model = new ModelRecord("model", "Model", Path.Combine(root, "model.gguf"), OwnershipKind.External, "{}", now);
        await store.UpsertModelAsync(model);
        var profile = new NamedModelLaunchProfile(
            "default:model",
            model.Id,
            "Default",
            ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root)),
            now,
            true);
        await store.SaveNamedModelLaunchProfileAsync(profile);
        var original = new ModelGroupRecord(
            "group:original",
            "Original",
            ModelGroupRetentionMode.Pinned,
            30,
            ModelGroupEvictionPriority.High,
            now);
        await store.UpsertModelGroupAsync(original);
        await store.AssignLaunchProfileGroupAsync(new ModelGroupAssignment(profile.Id, original.Id, now));

        await Assert.ThrowsAsync<SqliteException>(() => store.ReplaceModelGroupsAsync(
            [
                original with { Name = "Duplicate" },
                original with { Id = "group:duplicate", Name = "Duplicate" }
            ],
            []));

        var snapshot = await store.GetModelGroupSnapshotAsync();
        Assert.Equal(original, Assert.Single(snapshot.Groups));
        Assert.Equal(original.Id, snapshot.Assignments[profile.Id].GroupId);
    }

    [Fact]
    public async Task ExistingModelGroupAssignmentMigratesToTheDefaultLaunchProfile()
    {
        var root = CreateTempRoot();
        var databasePath = Path.Combine(root, "state", "manager.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await using (var legacy = new SqliteConnection($"Data Source={databasePath}"))
        {
            await legacy.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = legacy.CreateCommand();
            command.CommandText = """
CREATE TABLE migrations (id INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_at TEXT NOT NULL);
CREATE TABLE models (
  id TEXT PRIMARY KEY, name TEXT NOT NULL, model_path TEXT NOT NULL,
  ownership TEXT NOT NULL, metadata_json TEXT NOT NULL, updated_at TEXT NOT NULL);
CREATE TABLE model_launch_profiles (
  id TEXT PRIMARY KEY, model_id TEXT NOT NULL, name TEXT NOT NULL COLLATE NOCASE,
  settings_json TEXT NOT NULL, updated_at TEXT NOT NULL, is_default INTEGER NOT NULL DEFAULT 0,
  UNIQUE(model_id, name));
CREATE TABLE model_groups (
  id TEXT PRIMARY KEY, name TEXT NOT NULL COLLATE NOCASE UNIQUE, retention_mode TEXT NOT NULL,
  idle_minutes INTEGER NOT NULL, eviction_priority TEXT NOT NULL, updated_at TEXT NOT NULL);
CREATE TABLE model_group_assignments (
  model_id TEXT PRIMARY KEY, group_id TEXT NOT NULL, updated_at TEXT NOT NULL);
INSERT INTO migrations VALUES (1, 'baseline-v1', '2026-08-15T00:00:00Z');
INSERT INTO migrations VALUES (2, 'named-model-launch-profiles', '2026-08-15T00:00:00Z');
INSERT INTO migrations VALUES (3, 'real-default-model-launch-profiles', '2026-08-15T00:00:00Z');
INSERT INTO migrations VALUES (4, 'model-groups-and-retention-priority', '2026-08-15T00:00:00Z');
INSERT INTO models VALUES ('legacy-model', 'Legacy', 'legacy.gguf', 'External', '{}', '2026-08-15T00:00:00Z');
INSERT INTO model_launch_profiles VALUES ('default:legacy-model', 'legacy-model', 'Default', '{}', '2026-08-15T00:00:00Z', 1);
INSERT INTO model_launch_profiles VALUES ('profile:legacy-model:batch', 'legacy-model', 'Batch', '{}', '2026-08-15T00:00:00Z', 0);
INSERT INTO model_groups VALUES ('group:legacy', 'Legacy group', 'Pinned', 30, 'High', '2026-08-15T00:00:00Z');
INSERT INTO model_group_assignments VALUES ('legacy-model', 'group:legacy', '2026-08-15T00:00:00Z');
""";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using (var store = new StateStore(databasePath))
        {
            await store.InitializeAsync();
            var snapshot = await store.GetModelGroupSnapshotAsync();
            Assert.Equal("group:legacy", snapshot.Assignments["default:legacy-model"].GroupId);
            Assert.False(snapshot.Assignments.ContainsKey("profile:legacy-model:batch"));
        }

        await using var verify = new SqliteConnection($"Data Source={databasePath}");
        await verify.OpenAsync(TestContext.Current.CancellationToken);
        await using var table = verify.CreateCommand();
        table.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'model_group_assignments';";
        Assert.Equal(0L, (long)(await table.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task GroupAwareIdleUnloadEvictsOneLowestPriorityIdleModelFirst()
    {
        var root = CreateTempRoot();
        var service = new RuntimeIdleUnloadPolicyService();
        var now = DateTimeOffset.Parse("2026-08-15T12:00:00Z", CultureInfo.InvariantCulture);
        var high = GroupPollResult(root, "high", 8081);
        var low = GroupPollResult(root, "low", 8082);
        var unloaded = new List<string>();

        EffectiveModelRetentionPolicy Policy(RuntimeMetricPollResult result)
            => new(
                AllowsIdleUnload: true,
                IdleMinutes: 1,
                result.Session.ModelId == "low" ? ModelGroupEvictionPriority.Low : ModelGroupEvictionPriority.High);

        Assert.Equal(0, await service.ApplyAsync(
            [high, low], Policy, now, (_, _) => Task.CompletedTask,
            maximumUnloads: 1, TestContext.Current.CancellationToken));
        Assert.Equal(1, await service.ApplyAsync(
            [high, low], Policy, now.AddSeconds(61), (result, _) =>
            {
                unloaded.Add(result.Session.ModelId);
                return Task.CompletedTask;
            }, maximumUnloads: 1, TestContext.Current.CancellationToken));

        Assert.Equal(["low"], unloaded);
    }

    [Fact]
    public void ControlCliBuildsModelGroupCrudAndAssignmentRequests()
    {
        var create = LocalLlmConsole.ControlCli.ControlCliRequestFactory.BuildForTests(
            "groups", "create", "--name", "Batch", "--retention", "idle-timeout", "--idle-minutes", "12", "--priority", "low");
        Assert.Equal("POST", create.Method);
        Assert.Equal("/api/v1/model-groups", create.Path);
        Assert.Equal("Batch", create.Body?["name"]?.GetValue<string>());
        Assert.Equal(12, create.Body?["idleMinutes"]?.GetValue<int>());

        var assign = LocalLlmConsole.ControlCli.ControlCliRequestFactory.BuildForTests("groups", "assign", "qwen", "128K", "--group", "Batch");
        Assert.Equal("PUT", assign.Method);
        Assert.Equal("/api/v1/models/qwen/profiles/128K/group", assign.Path);
        Assert.Equal("Batch", assign.Body?["group"]?.GetValue<string>());

        var unassign = LocalLlmConsole.ControlCli.ControlCliRequestFactory.BuildForTests("groups", "unassign", "qwen", "128K");
        Assert.Equal("DELETE", unassign.Method);
        Assert.Equal("/api/v1/models/qwen/profiles/128K/group", unassign.Path);
        Assert.Throws<InvalidOperationException>(() => LocalLlmConsole.ControlCli.ControlCliRequestFactory.BuildForTests(
            "groups", "create", "--name", "Invalid", "--idle-minutes", "soon"));
    }

    private static RuntimeMetricPollResult GroupPollResult(string root, string modelId, int port)
    {
        var settings = AppSettings.CreateDefault(root) with { Port = port };
        var session = new LoadedModelSessionSnapshot(
            $"session-{modelId}", modelId, modelId, "runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cpu,
            settings, Path.Combine(root, $"{modelId}.log"), DateTimeOffset.UtcNow, "", 0,
            LoadedModelSessionStatus.Running, IsRunning: true, IsSelected: false);
        return new RuntimeMetricPollResult(
            session,
            RuntimeMetricPollerService.RuntimeKey(session),
            [],
            new RuntimeSlotSnapshot(0, 0, false, null, null, null),
            "");
    }
}
