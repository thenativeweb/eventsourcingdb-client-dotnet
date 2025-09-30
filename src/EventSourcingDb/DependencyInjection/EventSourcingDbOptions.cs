using System;
using System.ComponentModel.DataAnnotations;

namespace EventSourcingDb.DependencyInjection;

public class EventSourcingDbOptions
{
    [Required]
    public required string ApiToken { get; set; }

    [Required, Url]
    public required Uri BaseUrl { get; set; }
}
