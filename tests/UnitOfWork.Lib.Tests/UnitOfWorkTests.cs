using Microsoft.EntityFrameworkCore;
using Sut = UnitOfWork.Lib.Core.UnitOfWork;

namespace UnitOfWork.Lib.Tests;

public sealed class UnitOfWorkTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public void Constructor_rejects_null_context()
    {
        Assert.Throws<ArgumentNullException>(() => new Sut(null!));
    }

    [Fact]
    public async Task SaveChangeAsync_persists_changes_and_returns_affected_rows()
    {
        await using var context = _database.CreateContext();
        await using var unitOfWork = new Sut(context);
        context.Items.AddRange(new Item { Name = "a" }, new Item { Name = "b" });

        var affected = await unitOfWork.SaveChangeAsync();

        Assert.Equal(2, affected);
        Assert.Equal(["a", "b"], await _database.StoredNamesAsync());
    }

    [Fact]
    public async Task SaveChangeAsync_lets_database_errors_propagate()
    {
        await using var context = _database.CreateContext();
        await using var unitOfWork = new Sut(context);
        context.Items.AddRange(new Item { Name = "same" }, new Item { Name = "same" });

        await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.SaveChangeAsync());
    }

    [Fact]
    public async Task CommitTransactionAsync_saves_pending_changes_and_commits()
    {
        await using var context = _database.CreateContext();
        await using var unitOfWork = new Sut(context);

        await unitOfWork.BeginTransactionAsync();
        context.Items.Add(new Item { Name = "a" });
        await unitOfWork.CommitTransactionAsync();

        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(["a"], await _database.StoredNamesAsync());
    }

    [Fact]
    public async Task CommitTransactionAsync_without_transaction_throws()
    {
        await using var context = _database.CreateContext();
        await using var unitOfWork = new Sut(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.CommitTransactionAsync());
    }

    [Fact]
    public async Task CommitTransactionAsync_twice_throws()
    {
        await using var context = _database.CreateContext();
        await using var unitOfWork = new Sut(context);
        await unitOfWork.BeginTransactionAsync();
        await unitOfWork.CommitTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.CommitTransactionAsync());
    }

    [Fact]
    public async Task RollbackTransactionAsync_discards_saved_changes()
    {
        await using var context = _database.CreateContext();
        await using var unitOfWork = new Sut(context);

        await unitOfWork.BeginTransactionAsync();
        context.Items.Add(new Item { Name = "a" });
        await unitOfWork.SaveChangeAsync();
        await unitOfWork.RollbackTransactionAsync();

        Assert.Null(context.Database.CurrentTransaction);
        Assert.Empty(await _database.StoredNamesAsync());
    }

    [Fact]
    public async Task RollbackTransactionAsync_without_transaction_does_nothing()
    {
        await using var context = _database.CreateContext();
        await using var unitOfWork = new Sut(context);

        await unitOfWork.RollbackTransactionAsync();
    }

    [Fact]
    public async Task BeginTransactionAsync_twice_throws()
    {
        await using var context = _database.CreateContext();
        await using var unitOfWork = new Sut(context);
        await unitOfWork.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.BeginTransactionAsync());
    }

    [Fact]
    public async Task BeginTransactionAsync_works_again_after_commit_and_after_rollback()
    {
        await using var context = _database.CreateContext();
        await using var unitOfWork = new Sut(context);

        await unitOfWork.BeginTransactionAsync();
        context.Items.Add(new Item { Name = "committed" });
        await unitOfWork.CommitTransactionAsync();

        await unitOfWork.BeginTransactionAsync();
        context.Items.Add(new Item { Name = "rolled-back" });
        await unitOfWork.SaveChangeAsync();
        await unitOfWork.RollbackTransactionAsync();

        await unitOfWork.BeginTransactionAsync();
        await unitOfWork.CommitTransactionAsync();

        Assert.Equal(["committed"], await _database.StoredNamesAsync());
    }

    [Fact]
    public async Task BeginTransactionAsync_with_retrying_strategy_throws_and_points_to_ExecuteInTransactionAsync()
    {
        await using var context = _database.CreateContext(retrying: true);
        await using var unitOfWork = new Sut(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.BeginTransactionAsync());

        Assert.Contains(nameof(Sut.ExecuteInTransactionAsync), exception.Message);
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Dispose_rolls_back_open_transaction()
    {
        await using var context = _database.CreateContext();
        var unitOfWork = new Sut(context);
        await unitOfWork.BeginTransactionAsync();
        context.Items.Add(new Item { Name = "a" });
        await unitOfWork.SaveChangeAsync();

        unitOfWork.Dispose();

        Assert.Null(context.Database.CurrentTransaction);
        Assert.Empty(await _database.StoredNamesAsync());
    }

    [Fact]
    public async Task DisposeAsync_rolls_back_open_transaction()
    {
        await using var context = _database.CreateContext();
        var unitOfWork = new Sut(context);
        await unitOfWork.BeginTransactionAsync();
        context.Items.Add(new Item { Name = "a" });
        await unitOfWork.SaveChangeAsync();

        await unitOfWork.DisposeAsync();

        Assert.Null(context.Database.CurrentTransaction);
        Assert.Empty(await _database.StoredNamesAsync());
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_saves_and_commits()
    {
        await using var context = _database.CreateContext();
        await using var unitOfWork = new Sut(context);

        await unitOfWork.ExecuteInTransactionAsync(_ =>
        {
            context.Items.Add(new Item { Name = "a" });
            return Task.CompletedTask;
        });

        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(["a"], await _database.StoredNamesAsync());
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_rolls_back_and_rethrows_when_operation_fails()
    {
        await using var context = _database.CreateContext();
        await using var unitOfWork = new Sut(context);

        await Assert.ThrowsAsync<InvalidTimeZoneException>(() => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            context.Items.Add(new Item { Name = "a" });
            await context.SaveChangesAsync(ct);
            throw new InvalidTimeZoneException();
        }));

        Assert.Null(context.Database.CurrentTransaction);
        Assert.Empty(await _database.StoredNamesAsync());
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_rolls_back_when_final_save_fails()
    {
        await using var context = _database.CreateContext();
        await using var unitOfWork = new Sut(context);

        await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            context.Items.Add(new Item { Name = "same" });
            await context.SaveChangesAsync(ct);
            context.Items.Add(new Item { Name = "same" });
        }));

        Assert.Null(context.Database.CurrentTransaction);
        Assert.Empty(await _database.StoredNamesAsync());
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_with_retrying_strategy_retries_whole_transaction_without_duplicates()
    {
        await using var context = _database.CreateContext(retrying: true);
        await using var unitOfWork = new Sut(context);
        var attempts = 0;

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            attempts++;
            context.Items.Add(new Item { Name = "a" });
            await context.SaveChangesAsync(ct);
            if (attempts == 1)
                throw new TransientTestException();
        });

        Assert.Equal(2, attempts);
        Assert.Equal(["a"], await _database.StoredNamesAsync());
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_with_retrying_strategy_does_not_retry_non_transient_errors()
    {
        await using var context = _database.CreateContext(retrying: true);
        await using var unitOfWork = new Sut(context);
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidTimeZoneException>(() => unitOfWork.ExecuteInTransactionAsync(_ =>
        {
            attempts++;
            throw new InvalidTimeZoneException();
        }));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_inside_open_transaction_joins_it_without_committing()
    {
        await using var context = _database.CreateContext();
        await using var unitOfWork = new Sut(context);
        await unitOfWork.BeginTransactionAsync();
        var attempts = 0;

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            attempts++;
            context.Items.Add(new Item { Name = "a" });
            await context.SaveChangesAsync(ct);
        });

        Assert.Equal(1, attempts);
        Assert.NotNull(context.Database.CurrentTransaction);

        await unitOfWork.RollbackTransactionAsync();
        Assert.Empty(await _database.StoredNamesAsync());
    }
}
