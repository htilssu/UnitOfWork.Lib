using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace UnitOfWork.Lib.Tests;

public class Item
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Item> Items => Set<Item>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>().HasIndex(i => i.Name).IsUnique();
    }
}

/// <summary>Stands in for a transient database failure.</summary>
public class TransientTestException : Exception;

/// <summary>Execution strategy that retries on <see cref="TransientTestException"/>, like EnableRetryOnFailure does for real providers.</summary>
public class TestRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
    : ExecutionStrategy(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(1))
{
    protected override bool ShouldRetryOn(Exception exception) => exception is TransientTestException;
}

/// <summary>SQLite in-memory database that lives as long as the connection is open.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public TestDatabase()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public TestDbContext CreateContext(bool retrying = false)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection, sqlite =>
            {
                if (retrying)
                    sqlite.ExecutionStrategy(dependencies => new TestRetryingExecutionStrategy(dependencies));
            })
            .Options;
        return new TestDbContext(options);
    }

    /// <summary>Names stored in the database, read through a fresh context.</summary>
    public async Task<List<string>> StoredNamesAsync()
    {
        await using var context = CreateContext();
        return await context.Items.AsNoTracking().OrderBy(i => i.Name).Select(i => i.Name).ToListAsync();
    }

    public void Dispose() => _connection.Dispose();
}
