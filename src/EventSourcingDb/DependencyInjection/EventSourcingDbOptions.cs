using System;
using System.ComponentModel.DataAnnotations;

namespace EventSourcingDb.DependencyInjection;

public class EventSourcingDbOptions
{
    [Required]
    public required string ApiToken { get; set; }

    [Required, Url]
    public required Uri BaseUrl { get; set; }

    /// <summary>
    /// How long a request waits for anything to arrive from the server before it fails, see
    /// <see cref="ClientOptions.StallTimeout"/>. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan StallTimeout { get; set; } = ClientOptions.DefaultStallTimeout;
}
