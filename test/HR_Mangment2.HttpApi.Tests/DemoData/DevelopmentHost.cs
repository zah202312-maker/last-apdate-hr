using System;
using System.Threading.Tasks;
using HR_Mangment2.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BackgroundWorkers;

namespace HR_Mangment2.DemoData;

/// <summary>
/// The real Host in the Development environment, on the database its own settings name, with nothing running in the
/// background and no e-mail leaving the process.
/// </summary>
public sealed class DevelopmentHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public DevelopmentHost()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<AbpBackgroundJobOptions>(options => options.IsJobExecutionEnabled = false);
                services.PostConfigure<AbpBackgroundWorkerOptions>(options => options.IsEnabled = false);
                services.Replace(ServiceDescriptor.Singleton<Volo.Abp.Emailing.IEmailSender, CapturingEmailSender>());
            });
        });

        _ = _factory.Server;
    }

    public IServiceProvider Services => _factory.Services;

    /// <summary>The deliveries the seeding queued: the in-app notifications stay, the e-mail and push copies go.</summary>
    public async Task<int> RemoveQueuedDeliveriesAsync(DateTime sinceUtc)
    {
        var connectionString = Services.GetRequiredService<IConfiguration>().GetConnectionString("Default");
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "DELETE FROM \"AbpBackgroundJobs\" WHERE \"JobName\" = 'HrNotificationDelivery' AND \"CreationTime\" >= @since", connection);
        command.Parameters.AddWithValue("since", sinceUtc.AddMinutes(-1));
        return await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
