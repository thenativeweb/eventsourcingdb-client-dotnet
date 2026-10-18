using System;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventSourcingDb.DependencyInjection;

public static class ServiceCollectionExtensions
{
    // The timeout that HttpClient uses unless an application sets a different one.
    private static readonly TimeSpan _defaultHttpClientTimeout = TimeSpan.FromSeconds(100);

    public static IHttpClientBuilder AddEventSourcingDb(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<EventSourcingDbOptions>? configureOptions = null,
        JsonSerializerOptions? jsonSerializerOptions = null
    )
    {
        services
            .AddOptions<EventSourcingDbOptions>()
            .Bind(configuration.GetSection("EventSourcingDb"))
            .ValidateOnStart();

        if (configureOptions is not null)
        {
            services.PostConfigure(configureOptions);
        }

        // Every resolved client is one of its own, since typed HttpClients are transient, so the
        // clients of this registration share which API the server offers, instead of each one
        // finding it out on its first request.
        var apiVersionDetector = new ApiVersionDetector();

        return services.AddHttpClient<IClient, Client>((client, sp) =>
            {
                var options = sp.GetRequiredService<IOptions<EventSourcingDbOptions>>().Value;

                client.BaseAddress = options.BaseUrl;
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiToken);

                var logger = sp.GetRequiredService<ILogger<Client>>();
                var clientOptions = new ClientOptions { StallTimeout = options.StallTimeout };

                return new Client(client, clientOptions, jsonSerializerOptions, logger, apiVersionDetector);
            }
        )
        // The client bounds every request by its stall timeout, so the default timeout of HttpClient
        // must not cut requests short. A timeout that the application configured, for all clients or
        // on the returned builder, still applies.
        .ConfigureHttpClient(client =>
        {
            if (client.Timeout == _defaultHttpClientTimeout)
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
            }
        });
    }
}
