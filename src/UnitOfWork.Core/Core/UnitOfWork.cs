using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnitOfWork.Lib.Interfaces;

namespace UnitOfWork.Lib.Core;

public class UnitOfWork : IUnitOfWork
{
    private readonly DbContext _dbContext;

    private IDbContextTransaction? _transaction;

    public UnitOfWork(DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public void Dispose()
    {
        _transaction?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        return _transaction?.DisposeAsync() ?? default;
    }

    public async Task<int> SaveChangeAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            throw new InvalidOperationException("Transaction has already been started.");
        }
        if (_dbContext.Database.CreateExecutionStrategy().RetriesOnFailure)
        {
            throw new InvalidOperationException(
                $"The configured execution strategy retries on failure and does not support transactions started with {nameof(BeginTransactionAsync)}. " +
                $"Use {nameof(ExecuteInTransactionAsync)} instead so the whole transaction can be retried as a unit.");
        }
        _transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
            return;

        await _transaction.RollbackAsync(cancellationToken);
        _transaction = null;
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("Transaction has not been started.");
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _transaction.CommitAsync(cancellationToken);
        _transaction = null;
    }

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.CurrentTransaction != null)
        {
            await operation(cancellationToken);
            return;
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async (ct) =>
        {
            _dbContext.ChangeTracker.Clear();
            await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);
            await operation(ct);
            await _dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, cancellationToken);
    }
}