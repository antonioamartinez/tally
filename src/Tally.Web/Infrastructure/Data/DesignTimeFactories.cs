using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tally.Web.Infrastructure.Data;

// Used only by `dotnet ef` so migrations can be generated without a running database.

internal sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteTallyDbContext>
{
    public SqliteTallyDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteTallyDbContext>().UseSqlite("Data Source=design.db").Options);
}

internal sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresTallyDbContext>
{
    public PostgresTallyDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresTallyDbContext>().UseNpgsql("Host=localhost;Database=tally").Options);
}
