using System.ComponentModel.DataAnnotations;

namespace Tally.Web.Infrastructure.Data;

public enum DatabaseProvider
{
    Sqlite,
    Postgres,
}

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    [Required]
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Apply pending migrations at startup. Handy for local runs and demos; production runs migrations as a deploy step.</summary>
    public bool MigrateOnStartup { get; set; }

    /// <summary>Create demo users and claims when the database is empty.</summary>
    public bool SeedDemoData { get; set; }
}
