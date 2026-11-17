using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace HR_Mangment2;

public class Program
{
    public async static Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Async(c => c.File("Logs/logs.txt"))
            .WriteTo.Async(c => c.Console())
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting HR_Mangment2.HttpApi.Host.");
            var builder = WebApplication.CreateBuilder(args);
            // Finds the settings whether the host runs from the project folder or from bin.
            // Order matters: later files win, so the environment file overrides the base one and the secrets come last.
            // Environment variables and command-line arguments are added again so they still win over these files.
            var environmentFile = $"appsettings.{builder.Environment.EnvironmentName}.json";
            foreach (var name in new[] { "appsettings.json", environmentFile, "appsettings.secrets.json" })
            {
                foreach (var folder in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
                {
                    var file = Path.Combine(folder, name);
                    if (File.Exists(file))
                    {
                        builder.Configuration.AddJsonFile(file, optional: true, reloadOnChange: true);
                    }
                }
            }
            builder.Configuration.AddEnvironmentVariables();
            builder.Configuration.AddCommandLine(args);
            builder.Host
                .AddAppSettingsSecretsJson()
                .UseAutofac()
                .UseSerilog((context, services, loggerConfiguration) =>
                {
                    loggerConfiguration
                        .ReadFrom.Configuration(context.Configuration)
                        .ReadFrom.Services(services)
                        .WriteTo.Async(c => c.AbpStudio(services));
                });
            await builder.AddApplicationAsync<HR_Mangment2HttpApiHostModule>();
            var app = builder.Build();
            await app.InitializeApplicationAsync();
            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            if (ex is HostAbortedException)
            {
                throw;
            }

            Log.Fatal(ex, "Host terminated unexpectedly!");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
