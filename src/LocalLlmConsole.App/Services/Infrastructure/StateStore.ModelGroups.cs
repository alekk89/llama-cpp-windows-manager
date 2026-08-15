namespace LocalLlmConsole.Services;

public sealed partial class StateStore
{
    public async Task<ModelGroupSnapshot> GetModelGroupSnapshotAsync()
    {
        return await WithConnectionAsync(async () =>
        {
            var groups = new List<ModelGroupRecord>();
            await using (var groupCommand = _connection.CreateCommand())
            {
                groupCommand.CommandText = """
SELECT id, name, retention_mode, idle_minutes, eviction_priority, updated_at
FROM model_groups
ORDER BY name COLLATE NOCASE;
""";
                await using var reader = await groupCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    groups.Add(new ModelGroupRecord(
                        reader.GetString(0),
                        reader.GetString(1),
                        EnumValue(reader.GetString(2), ModelGroupRetentionMode.Inherit),
                        reader.GetInt32(3),
                        EnumValue(reader.GetString(4), ModelGroupEvictionPriority.Normal),
                        DateValue(reader.GetString(5))));
                }
            }

            var assignments = new Dictionary<string, ModelGroupAssignment>(StringComparer.OrdinalIgnoreCase);
            await using (var assignmentCommand = _connection.CreateCommand())
            {
                assignmentCommand.CommandText = """
SELECT launch_profile_id, group_id, updated_at
FROM launch_profile_group_assignments;
""";
                await using var reader = await assignmentCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var assignment = new ModelGroupAssignment(
                        reader.GetString(0),
                        reader.GetString(1),
                        DateValue(reader.GetString(2)));
                    assignments[assignment.LaunchProfileId] = assignment;
                }
            }

            return new ModelGroupSnapshot(groups, assignments);
        });
    }

    public async Task UpsertModelGroupAsync(ModelGroupRecord group)
    {
        ArgumentNullException.ThrowIfNull(group);
        await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
INSERT INTO model_groups (id, name, retention_mode, idle_minutes, eviction_priority, updated_at)
VALUES ($id, $name, $retention_mode, $idle_minutes, $eviction_priority, $updated_at)
ON CONFLICT(id) DO UPDATE SET
  name = excluded.name,
  retention_mode = excluded.retention_mode,
  idle_minutes = excluded.idle_minutes,
  eviction_priority = excluded.eviction_priority,
  updated_at = excluded.updated_at;
""";
            command.Parameters.AddWithValue("$id", group.Id);
            command.Parameters.AddWithValue("$name", group.Name);
            command.Parameters.AddWithValue("$retention_mode", group.RetentionMode.ToString());
            command.Parameters.AddWithValue("$idle_minutes", group.IdleMinutes);
            command.Parameters.AddWithValue("$eviction_priority", group.EvictionPriority.ToString());
            command.Parameters.AddWithValue("$updated_at", group.UpdatedAt.ToString("O"));
            await command.ExecuteNonQueryAsync();
        });
    }

    public async Task DeleteModelGroupAsync(string groupId)
    {
        await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM model_groups WHERE id = $id;";
            command.Parameters.AddWithValue("$id", groupId);
            await command.ExecuteNonQueryAsync();
        });
    }

    public async Task AssignLaunchProfileGroupAsync(ModelGroupAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
INSERT INTO launch_profile_group_assignments (launch_profile_id, group_id, updated_at)
VALUES ($launch_profile_id, $group_id, $updated_at)
ON CONFLICT(launch_profile_id) DO UPDATE SET
  group_id = excluded.group_id,
  updated_at = excluded.updated_at;
""";
            command.Parameters.AddWithValue("$launch_profile_id", assignment.LaunchProfileId);
            command.Parameters.AddWithValue("$group_id", assignment.GroupId);
            command.Parameters.AddWithValue("$updated_at", assignment.UpdatedAt.ToString("O"));
            await command.ExecuteNonQueryAsync();
        });
    }

    public async Task UnassignLaunchProfileGroupAsync(string launchProfileId)
    {
        await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM launch_profile_group_assignments WHERE launch_profile_id = $launch_profile_id;";
            command.Parameters.AddWithValue("$launch_profile_id", launchProfileId);
            await command.ExecuteNonQueryAsync();
        });
    }

    public async Task ReplaceModelGroupsAsync(
        IReadOnlyList<ModelGroupRecord> groups,
        IReadOnlyList<ModelGroupAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(assignments);
        await WithConnectionAsync(async () =>
        {
            await using var transaction = _connection.BeginTransaction();
            try
            {
                await using (var clearAssignments = _connection.CreateCommand())
                {
                    clearAssignments.Transaction = transaction;
                    clearAssignments.CommandText = "DELETE FROM launch_profile_group_assignments;";
                    await clearAssignments.ExecuteNonQueryAsync();
                }
                await using (var clearGroups = _connection.CreateCommand())
                {
                    clearGroups.Transaction = transaction;
                    clearGroups.CommandText = "DELETE FROM model_groups;";
                    await clearGroups.ExecuteNonQueryAsync();
                }

                foreach (var group in groups)
                {
                    await using var command = _connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = """
INSERT INTO model_groups (id, name, retention_mode, idle_minutes, eviction_priority, updated_at)
VALUES ($id, $name, $retention_mode, $idle_minutes, $eviction_priority, $updated_at);
""";
                    command.Parameters.AddWithValue("$id", group.Id);
                    command.Parameters.AddWithValue("$name", group.Name);
                    command.Parameters.AddWithValue("$retention_mode", group.RetentionMode.ToString());
                    command.Parameters.AddWithValue("$idle_minutes", group.IdleMinutes);
                    command.Parameters.AddWithValue("$eviction_priority", group.EvictionPriority.ToString());
                    command.Parameters.AddWithValue("$updated_at", group.UpdatedAt.ToString("O"));
                    await command.ExecuteNonQueryAsync();
                }

                foreach (var assignment in assignments)
                {
                    await using var command = _connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = """
INSERT INTO launch_profile_group_assignments (launch_profile_id, group_id, updated_at)
VALUES ($launch_profile_id, $group_id, $updated_at);
""";
                    command.Parameters.AddWithValue("$launch_profile_id", assignment.LaunchProfileId);
                    command.Parameters.AddWithValue("$group_id", assignment.GroupId);
                    command.Parameters.AddWithValue("$updated_at", assignment.UpdatedAt.ToString("O"));
                    await command.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }
}
