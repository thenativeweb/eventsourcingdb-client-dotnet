using System;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventSourcingDb.DependencyInjection;

public static class ServiceCollectionExtensions
{
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

        return services.AddHttpClient<IClient, Client>((client, sp) =>
            {
                var options = sp.GetRequiredService<IOptions<EventSourcingDbOptions>>().Value;

                client.BaseAddress = options.BaseUrl;
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiToken);

                var logger = sp.GetRequiredService<ILogger<Client>>();

                return new Client(client, jsonSerializerOptions, logger);
            }
        );
    }
}
