namespace LocalLlmConsole.Services;

// Raised before any write when the database records schema migrations this build does not know.
public sealed class StateStoreSchemaTooNewException : InvalidOperationException
{
    public StateStoreSchemaTooNewException(string databasePath, int migrationId, string migrationName, int latestKnownMigrationId)
        : base(
            $"The application database '{databasePath}' was created by a newer version of llama.cpp Windows Manager " +
            $"(schema migration {migrationId} '{migrationName}'; this version supports up to {latestKnownMigrationId}). " +
            "The database was left unchanged. Install the newer version again, or restore a backup made by this version.")
    {
        DatabasePath = databasePath;
        MigrationId = migrationId;
        MigrationName = migrationName;
        LatestKnownMigrationId = latestKnownMigrationId;
    }

    public string DatabasePath { get; }

    public int MigrationId { get; }

    public string MigrationName { get; }

    public int LatestKnownMigrationId { get; }
}
