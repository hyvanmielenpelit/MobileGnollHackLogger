using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace Overseer.Services.Security;

/// <summary>
/// Runs queued bulk operations one at a time, one account at a time, each account in its own DI scope. A cancel
/// request is seen before the next account; <see cref="AdminUserManagementOptions.BulkConsecutiveFailureLimit"/>
/// failures in a row stop the run. At startup, operations left queued or running are marked interrupted.
/// </summary>
public sealed class AdminBulkOperationRunner : BackgroundService, IAdminBulkOperationQueue
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AdminUserManagementOptions _options;
    private readonly ILogger<AdminBulkOperationRunner> _logger;
    private readonly TimeProvider _time;
    private readonly Channel<long> _queue = Channel.CreateUnbounded<long>(new UnboundedChannelOptions { SingleReader = true });

    public AdminBulkOperationRunner(IServiceScopeFactory scopeFactory, AdminUserManagementOptions options,
        ILogger<AdminBulkOperationRunner> logger, TimeProvider? time = null)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public void Enqueue(long operationId) => _queue.Writer.TryWrite(operationId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverInterruptedAsync(stoppingToken);
        }
        catch (Exception ex) when (ex.IsCanceledBy(stoppingToken))
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not mark interrupted bulk operations.");
        }

        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                while (_queue.Reader.TryRead(out long operationId))
                {
                    await ProcessOperationAsync(operationId, stoppingToken);
                }
            }
        }
        catch (Exception ex) when (ex.IsCanceledBy(stoppingToken))
        {
        }
    }

    /// <summary>Marks every queued or running operation interrupted (a requested cancel as canceled); processed items stay.</summary>
    public async Task<int> RecoverInterruptedAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ops = await db.AdminBulkOperations
            .Where(o => o.Status == AdminBulkOperationStatus.Queued || o.Status == AdminBulkOperationStatus.Running
                || o.Status == AdminBulkOperationStatus.CancelRequested)
            .ToListAsync(ct);
        foreach (var op in ops)
        {
            op.Status = op.Status == AdminBulkOperationStatus.CancelRequested ? AdminBulkOperationStatus.Canceled : AdminBulkOperationStatus.Interrupted;
            op.CompletedUtc = UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return ops.Count;
    }

    /// <summary>Runs one queued operation to its end. Never throws for a failure of the operation itself.</summary>
    public async Task ProcessOperationAsync(long operationId, CancellationToken ct = default)
    {
        try
        {
            await ProcessCoreAsync(operationId, ct);
        }
        catch (Exception ex) when (!ex.IsCanceledBy(ct))
        {
            _logger.LogError(ex, "Bulk operation {OperationId} stopped unexpectedly.", operationId);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var op = await db.AdminBulkOperations.FirstOrDefaultAsync(o => o.Id == operationId, CancellationToken.None);
                if (op != null && op.Status is AdminBulkOperationStatus.Running or AdminBulkOperationStatus.Queued or AdminBulkOperationStatus.CancelRequested)
                {
                    op.Status = AdminBulkOperationStatus.Interrupted;
                    op.CompletedUtc = UtcNow;
                    await db.SaveChangesAsync(CancellationToken.None);
                }
            }
            catch (Exception inner)
            {
                _logger.LogWarning(inner, "Could not mark bulk operation {OperationId} interrupted.", operationId);
            }
        }
    }

    private async Task ProcessCoreAsync(long operationId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var op = await db.AdminBulkOperations.FirstOrDefaultAsync(o => o.Id == operationId, ct);
        if (op == null) return;
        if (op.Status == AdminBulkOperationStatus.CancelRequested)
        {
            op.Status = AdminBulkOperationStatus.Canceled;
            op.CompletedUtc = UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }
        if (op.Status != AdminBulkOperationStatus.Queued) return;

        op.Status = AdminBulkOperationStatus.Running;
        op.StartedUtc = UtcNow;
        await db.SaveChangesAsync(ct);

        var options = AdminBulkUserOperationService.ReadOptions(op);
        var criteria = AdminBulkUserOperationService.ReadCriteria(op);
        var itemIds = await db.AdminBulkOperationItems
            .Where(i => i.OperationId == op.Id && !i.Excluded && i.Outcome == AdminBulkItemOutcome.Pending)
            .OrderBy(i => i.Id)
            .Select(i => i.Id)
            .ToListAsync(ct);

        int limit = _options.BulkConsecutiveFailureLimit;
        int consecutiveFailures = 0;
        bool canceled = false;
        string? stopMessage = null;
        foreach (long itemId in itemIds)
        {
            var status = await db.AdminBulkOperations.AsNoTracking().Where(o => o.Id == op.Id).Select(o => o.Status).FirstAsync(ct);
            if (status == AdminBulkOperationStatus.CancelRequested)
            {
                canceled = true;
                break;
            }

            var item = await db.AdminBulkOperationItems.FirstAsync(i => i.Id == itemId, ct);
            AdminBulkItemResult result;
            try
            {
                using var itemScope = _scopeFactory.CreateScope();
                var bulk = itemScope.ServiceProvider.GetRequiredService<AdminBulkUserOperationService>();
                result = await bulk.ProcessItemAsync(op, options, criteria, item.UserId, ct);
            }
            catch (Exception ex) when (!ex.IsCanceledBy(ct))
            {
                _logger.LogWarning(ex, "Bulk operation {OperationId} failed on user {UserId}.", op.Id, item.UserId);
                result = new AdminBulkItemResult(AdminBulkItemOutcome.Failed, ex.Message);
            }

            item.Outcome = result.Outcome;
            item.Message = Cut(result.Message, 512);
            item.BeforeStatus = Cut(result.BeforeStatus, 256);
            item.AfterStatus = Cut(result.AfterStatus, 256);
            item.ProcessedUtc = UtcNow;
            switch (result.Outcome)
            {
                case AdminBulkItemOutcome.Changed: op.ChangedCount++; break;
                case AdminBulkItemOutcome.Skipped: op.SkippedCount++; break;
                case AdminBulkItemOutcome.Failed: op.FailedCount++; break;
            }
            consecutiveFailures = result.Outcome == AdminBulkItemOutcome.Failed ? consecutiveFailures + 1 : 0;
            await db.SaveChangesAsync(CancellationToken.None);

            if (limit > 0 && consecutiveFailures >= limit)
            {
                stopMessage = $"Stopped after {consecutiveFailures} consecutive failures.";
                break;
            }
        }

        var finalStatus = await db.AdminBulkOperations.AsNoTracking().Where(o => o.Id == op.Id).Select(o => o.Status).FirstAsync(CancellationToken.None);
        op.Status = canceled || finalStatus == AdminBulkOperationStatus.CancelRequested
            ? AdminBulkOperationStatus.Canceled
            : AdminBulkOperationStatus.Completed;
        if (stopMessage != null)
        {
            options.StopMessage = stopMessage;
            op.OptionsJson = AdminBulkUserOperationService.WriteOptions(options);
        }
        op.CompletedUtc = UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static string? Cut(string? value, int max) => value == null ? null : value.Length <= max ? value : value[..max];
}
