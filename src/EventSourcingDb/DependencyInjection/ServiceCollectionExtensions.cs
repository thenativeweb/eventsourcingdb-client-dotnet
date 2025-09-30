using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventSourcingDb.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static void AddEventSourcingDb(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<EventSourcingDbOptions>? configureOptions = null
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

        services.AddScoped<IClient>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<EventSourcingDbOptions>>().Value;
                var logger = sp.GetRequiredService<ILogger<Client>>();

                return new Client(options.BaseUrl, options.ApiToken, logger);
            }
        );
    }
}
