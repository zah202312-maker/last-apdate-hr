using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace HR_Mangment2.DbMigrator;

class Program
{
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Volo.Abp", LogEventLevel.Warning)
#if DEBUG
                .MinimumLevel.Override("HR_Mangment2", LogEventLevel.Debug)
#else
                .MinimumLevel.Override("HR_Mangment2", LogEventLevel.Information)
#endif
                .Enrich.FromLogContext()
            .WriteTo.Async(c => c.File("Logs/logs.txt"))
            .WriteTo.Async(c => c.Console())
            .CreateLogger();

        await CreateHostBuilder(args).RunConsoleAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                foreach (var file in new[]
                         {
                             Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                             Path.Combine(AppContext.BaseDirectory, "appsettings.secrets.json"),
                             Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
                             Path.Combine(Directory.GetCurrentDirectory(), "appsettings.secrets.json")
                         })
                {
                    if (File.Exists(file))
                    {
                        config.AddJsonFile(file, optional: true, reloadOnChange: true);
                    }
                }
            })
            .AddAppSettingsSecretsJson()
            .ConfigureLogging((context, logging) => logging.ClearProviders())
            .ConfigureServices((hostContext, services) =>
            {
                services.AddHostedService<DbMigratorHostedService>();
            });
}
