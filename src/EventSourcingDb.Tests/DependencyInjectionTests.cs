using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventSourcingDb.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventSourcingDb.Tests;

public sealed class DependencyInjectionTests : EventSourcingDbTests
{
    [Fact]
    public async Task ClientIsAvailableUsingDependencyInjection()
    {
        var url = Container!.GetBaseUrl();
        var apiToken = Container!.GetApiToken();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("EventSourcingDb:BaseUrl", url.ToString()),
                    new KeyValuePair<string, string?>("EventSourcingDb:ApiToken", apiToken)
                ]
            )
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventSourcingDb(configuration);

        var serviceProvider = services.BuildServiceProvider();

        var client = serviceProvider.GetService<IClient>();

        if (client is null)
        {
            throw new Exception("IClient is not registered.");
        }

        await client.PingAsync();
    }
}
