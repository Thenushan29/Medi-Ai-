using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MediTrail.Api.Data;

/// <summary>
/// Lets `dotnet ef migrations` build the model without a live database or a running host.
/// The placeholder connection string is never opened during model construction; set
/// MEDITRAIL_CONNECTION_STRING only when running commands that actually hit the database.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MediTrailDbContext>
{
    public MediTrailDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("MEDITRAIL_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=meditrail;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<MediTrailDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new MediTrailDbContext(options);
    }
}
