using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Vela.Api.Tests.Telemetry;

/// <summary>
/// The shipped appsettings.json decides which categories reach the console (Log Analytics) and the Azure Monitor log exporter.
/// EF Core logs every SQL statement at Information; at the 0.15 GB/day workspace cap that would crowd out the warnings and errors
/// the operations runbook depends on (FINALAPI I1).
/// </summary>
public class LogLevelConfigTests
{
    private static ILoggerFactory ProductionLogging()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(l => l.AddConfiguration(config.GetSection("Logging")).AddProvider(new CapturingLoggerProvider()));
        return services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command")]
    [InlineData("Microsoft.EntityFrameworkCore.Infrastructure")]
    [InlineData("Microsoft.EntityFrameworkCore.Migrations")]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("Microsoft.AspNetCore.Routing.EndpointMiddleware")]
    [InlineData("System.Net.Http.HttpClient.Default.LogicalHandler")]
    public void Framework_chatter_is_warning_and_above_only(string category)
    {
        var logger = ProductionLogging().CreateLogger(category);

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    [Theory]
    [InlineData("Vela.Api.Account.DeletionJob")]
    [InlineData("Microsoft.Hosting.Lifetime")]
    public void Application_and_lifetime_messages_keep_information(string category)
    {
        Assert.True(ProductionLogging().CreateLogger(category).IsEnabled(LogLevel.Information));
    }
}
