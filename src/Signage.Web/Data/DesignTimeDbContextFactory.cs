using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Signage.Web.Data;

/// <summary>Lets <c>dotnet ef migrations add</c> build the model without starting the app or a database.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SignageDbContext>
{
    public SignageDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SignageDbContext>();
        DataServiceCollectionExtensions.ConfigureNpgsql(options, "Host=localhost;Database=signage_design");
        return new SignageDbContext(options.Options);
    }
}
