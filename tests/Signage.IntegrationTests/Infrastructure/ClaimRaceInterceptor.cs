using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Signage.IntegrationTests.Infrastructure;

/// <summary>
/// Makes a pairing claim lose a race deterministically: once armed with a code, right before the next conditional
/// claim UPDATE it marks that code as claimed, as if another admin had been a moment faster.
/// </summary>
public sealed class ClaimRaceInterceptor : DbCommandInterceptor
{
    private string? _code;

    public void Arm(string normalizedCode) => Volatile.Write(ref _code, normalizedCode);

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CommandText.Contains("UPDATE pairing_requests", StringComparison.Ordinal)
            && command.CommandText.Contains("claimed_at IS NULL", StringComparison.Ordinal)
            && Interlocked.Exchange(ref _code, null) is { } code)
        {
            await using var steal = command.Connection!.CreateCommand();
            steal.Transaction = command.Transaction;
            steal.CommandText = "UPDATE pairing_requests SET claimed_at = now() WHERE code = @code AND claimed_at IS NULL";
            var parameter = steal.CreateParameter();
            parameter.ParameterName = "code";
            parameter.Value = code;
            steal.Parameters.Add(parameter);
            await steal.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        return result;
    }
}
