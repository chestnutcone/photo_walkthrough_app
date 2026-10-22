using Microsoft.EntityFrameworkCore;

namespace Vela.Api.Data;

public static class DbContextOptionsExtensions
{
    /// <summary>Single place for Npgsql and naming options, shared by the app and tests.</summary>
    public static DbContextOptionsBuilder ConfigureVela(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
}
