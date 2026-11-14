using System.Data;
using EmsPortal.Application.Abstractions.Branding;
using EmsPortal.Application.Abstractions.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EmsPortal.Infrastructure.Persistence;

// Numbers come from database sequences, so a deleted row never frees its number and two requests created at
// once cannot collide. The sequences are global, not per tenant.

internal static class SequenceReader
{
    // SqlQueryRaw wraps its SQL in a derived table, where SQL Server forbids NEXT VALUE FOR; assigning it to an
    // output parameter is allowed and stays on the context's connection and transaction.
    public static async Task<int> NextAsync(DatabaseFacade database, string sequence, CancellationToken cancellationToken)
    {
        var next = new SqlParameter("@next", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var sql = "SET @next = NEXT VALUE FOR " + sequence + ";";
        await database.ExecuteSqlRawAsync(sql, new object[] { next }, cancellationToken);
        return (int)next.Value;
    }
}

internal sealed class RemsNumberGenerator : IRemsNumberGenerator
{
    // Room left in REMS.RemsNumber (64) for the dash and the sequence.
    private const int PrefixMax = 50;

    private readonly EmsPortalDbContext _dbContext;
    private readonly ISiteNameProvider _siteName;

    public RemsNumberGenerator(EmsPortalDbContext dbContext, ISiteNameProvider siteName)
    {
        _dbContext = dbContext;
        _siteName = siteName;
    }

    // "{site name}-{n}": the number is named after the site, as it stood when the request was raised.
    public async Task<string> GenerateAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var site = await _siteName.GetAsync(tenantId, cancellationToken);
        var prefix = site.Length <= PrefixMax ? site : site[..PrefixMax].TrimEnd();
        return $"{prefix}-{await SequenceReader.NextAsync(_dbContext.Database, "dbo.seq_RemsNumber", cancellationToken)}";
    }
}

internal sealed class ClientNumberGenerator : IClientNumberGenerator
{
    private readonly EmsPortalDbContext _dbContext;

    public ClientNumberGenerator(EmsPortalDbContext dbContext) => _dbContext = dbContext;

    public async Task<string> GenerateAsync(CancellationToken cancellationToken = default)
        => $"CL-{await SequenceReader.NextAsync(_dbContext.Database, "dbo.seq_ClientNumber", cancellationToken)}";
}
