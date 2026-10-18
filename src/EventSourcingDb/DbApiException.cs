using System;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace EventSourcingDb;

/// <summary>
/// An error that EventSourcingDB reports, whether it refuses a request with a status code or
/// answers it with an error within its response.
/// </summary>
public class DbApiException : Exception
{
    public DbApiException(
        HttpStatusCode status,
        string description,
        string? code = null,
        JsonElement? details = null,
        TimeSpan? retryAfter = null
    ) : base(CreateMessage(status, description))
    {
        Status = status;
        Description = description;
        Code = code;
        Details = details;
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// The code of the error, exactly as the server sends it, which is the URL of the page that
    /// documents the error, such as <see cref="ErrorCodes.PreconditionViolated"/>. It is null for
    /// an error without a code.
    /// </summary>
    public string? Code { get; }

    /// <summary>
    /// The description of the error, as the server sends it.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// The details of the error, as the server sends them. They are null for an error without
    /// details.
    /// </summary>
    public JsonElement? Details { get; }

    /// <summary>
    /// The HTTP status of the response, which is 200 for an error that the server reports within
    /// its response.
    /// </summary>
    public HttpStatusCode Status { get; }

    /// <summary>
    /// For a status of 429, how long to wait before trying again, as the Retry-After header of the
    /// response tells it. It is null if the response has no such header, and for any other status.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    private static string CreateMessage(HttpStatusCode status, string description)
    {
        if (!string.IsNullOrWhiteSpace(description))
        {
            return description;
        }

        return string.Create(CultureInfo.InvariantCulture, $"EventSourcingDB answered with the status {(int)status}.");
    }
}
