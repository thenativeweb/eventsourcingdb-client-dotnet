using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventSourcingDb.Tests;

public sealed class DependencyInjectionTests : EventSourcingDbTests
{
    [Fact]
    public async Task HttpClientConfigurationViaDependencyInjectionIsWorking()
    {
        var url = Container!.GetBaseUrl();
        var apiToken = Container.GetApiToken();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("EventSourcingDb:BaseUrl", url.ToString()),
                    new KeyValuePair<string, string?>("EventSourcingDb:ApiToken", apiToken)
                ]
            )
            .Build();

        const int simulatedNetworkOutageDurationInMs = 500;
        const int retryDelayInMs = simulatedNetworkOutageDurationInMs + 100;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventSourcingDb(configuration)
            .AddHttpMessageHandler(() => new RetryOnceAfter(retryDelayInMs))
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(10));

        var serviceProvider = services.BuildServiceProvider();

        var client = serviceProvider.GetService<IClient>();

        if (client is null)
        {
            throw new Exception("IClient is not registered.");
        }

        await Container.PauseAsync();

        // With the container paused, ping fails after one retry
        // The task is canceled due to the configured timeout, thus we expect a TaskCanceledException
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await client.PingAsync());

        await Task.Run(async () =>
            {
                await Task.Delay(simulatedNetworkOutageDurationInMs);
                await Container.UnpauseAsync();
            }
        );

        // With the container unpaused within the retry delay, ping succeeds
        await client.PingAsync();
    }
}

public class RetryOnceAfter(int delayInMs) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            catch (Exception) when (attempt < 2)
            {
                attempt++;
                await Task.Delay(delayInMs, cancellationToken);
            }
        }
    }
}
