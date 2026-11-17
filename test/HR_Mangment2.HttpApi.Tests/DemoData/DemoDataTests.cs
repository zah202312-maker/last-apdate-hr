using System;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using HR_Mangment2.Infrastructure;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace HR_Mangment2.DemoData;

/// <summary>Runs the demo seeder on the throwaway test database, so a change that breaks it shows up in the suite.</summary>
[Collection(HostCollection.Name)]
public class DemoDataTests
{
    private readonly HostFixture _host;
    private readonly ITestOutputHelper _output;

    public DemoDataTests(HostFixture host, ITestOutputHelper output)
    {
        _host = host;
        _output = output;
    }

    [Fact]
    public async Task Demo_data_goes_in_through_the_services_once_and_its_accounts_sign_in()
    {
        const string password = "Demo-Test-1a!";
        var seeder = new DemoDataSeeder(_host.Factory.Services, HostFixture.TestTenantName, password, _output.WriteLine);

        (await seeder.SeedAsync()).ShouldBeTrue();
        (await new DemoDataSeeder(_host.Factory.Services, HostFixture.TestTenantName, password, _output.WriteLine).SeedAsync()).ShouldBeFalse();

        // A demo employee signs in with the known password, no forced change, and sees the two closed months.
        var staff = seeder.People.First(x => x.Key == "haider");
        var client = TestAuth.ClientFor(_host, (await TestAuth.SignInAsync(_host, staff.UserName, password)).AccessToken, "en");
        (await client.GetFromJsonAsync<JsonElement>("/api/app/me")).GetProperty("accessGate").GetString().ShouldBe("none");
        var slips = await client.GetFromJsonAsync<JsonElement>("/api/app/finance/payslip/my");
        slips.GetProperty("totalCount").GetInt64().ShouldBe(2);
    }
}

/// <summary>
/// Fills the development database. It does nothing unless asked for:
///   set HR_DEMO_SEED=development and HR_DEMO_PASSWORD=(the demo accounts' password), then
///   dotnet test test/HR_Mangment2.HttpApi.Tests --filter "FullyQualifiedName~DevelopmentDemoData"
/// It uses the Host's own settings (appsettings.secrets.json), so it writes to the same database as the Host.
/// Background jobs and workers stay off, and the e-mail and push deliveries it queues are removed at the end: the demo
/// people get their in-app notifications, and no message leaves the machine.
/// </summary>
public class DevelopmentDemoData
{
    private readonly ITestOutputHelper _output;

    public DevelopmentDemoData(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Seed_the_development_database_when_asked()
    {
        if (Environment.GetEnvironmentVariable("HR_DEMO_SEED") != "development")
        {
            _output.WriteLine("Skipped: set HR_DEMO_SEED=development to run.");
            return;
        }

        var password = Environment.GetEnvironmentVariable("HR_DEMO_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Set HR_DEMO_PASSWORD to the password the demo accounts should use.");
        }

        await using var host = new DevelopmentHost();
        var started = DateTime.UtcNow;
        try
        {
            await new DemoDataSeeder(host.Services, "default", password, _output.WriteLine).SeedAsync();
        }
        finally
        {
            var removed = await host.RemoveQueuedDeliveriesAsync(started);
            _output.WriteLine($"Queued e-mail and push deliveries removed: {removed}.");
        }
    }
}
