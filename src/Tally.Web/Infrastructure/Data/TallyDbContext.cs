using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tally.Domain;

namespace Tally.Web.Infrastructure.Data;

/// <summary>
/// The application's unit of work. The model is provider-neutral; each database provider gets a
/// small subclass so it can keep its own migration history (see SqliteTallyDbContext and
/// PostgresTallyDbContext).
/// </summary>
public abstract class TallyDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<ExpenseClaim> Claims => Set<ExpenseClaim>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All timestamps are UTC. SQLite does not store DateTime.Kind, so restore it on read.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("Users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Email).HasMaxLength(256).IsRequired();
            user.HasIndex(u => u.Email).IsUnique();
            user.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
            user.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
            user.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<ExpenseClaim>(claim =>
        {
            claim.ToTable("ExpenseClaims");
            claim.HasKey(c => c.Id);
            claim.Property(c => c.Description).HasMaxLength(ExpenseClaim.MaxDescriptionLength).IsRequired();
            claim.Property(c => c.Category).HasConversion<string>().HasMaxLength(20);
            claim.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            claim.Property(c => c.ReviewNote).HasMaxLength(ExpenseClaim.MaxNoteLength);
            claim.Property(c => c.Version).IsConcurrencyToken();

            claim.ComplexProperty(c => c.Amount, money =>
            {
                money.Property(m => m.MinorUnits).HasColumnName("AmountMinorUnits");
                money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsFixedLength();
            });

            claim.HasOne<User>().WithMany().HasForeignKey(c => c.SubmitterId).OnDelete(DeleteBehavior.Restrict);
            claim.HasOne<User>().WithMany().HasForeignKey(c => c.ReviewerId).OnDelete(DeleteBehavior.Restrict);

            // Supports "my claims, newest first" and "the review queue, oldest first".
            claim.HasIndex(c => new { c.SubmitterId, c.SubmittedAtUtc });
            claim.HasIndex(c => new { c.Status, c.SubmittedAtUtc });
        });
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}

public sealed class SqliteTallyDbContext(DbContextOptions<SqliteTallyDbContext> options) : TallyDbContext(options);

public sealed class PostgresTallyDbContext(DbContextOptions<PostgresTallyDbContext> options) : TallyDbContext(options);
