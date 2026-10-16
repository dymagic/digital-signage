using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Signage.Web.Domain;

namespace Signage.Web.Data;

/// <summary>
/// Fills <see cref="IHasCreatedAt.CreatedAt"/> on insert (when not already set) and
/// <see cref="IHasUpdatedAt.UpdatedAt"/> on every insert and update, from the injected <see cref="TimeProvider"/>.
/// </summary>
internal sealed class TimestampInterceptor(TimeProvider time) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = time.GetUtcNow();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added && entry.Entity is IHasCreatedAt created && created.CreatedAt == default)
            {
                created.CreatedAt = now;
            }

            if (entry.State is EntityState.Added or EntityState.Modified && entry.Entity is IHasUpdatedAt updated)
            {
                if (entry.State == EntityState.Modified || updated.UpdatedAt == default)
                {
                    updated.UpdatedAt = now;
                }
            }
        }
    }
}
