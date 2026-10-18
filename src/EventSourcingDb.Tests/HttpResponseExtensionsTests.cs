using System;
using System.Reflection;
using Xunit;

namespace EventSourcingDb.Tests;

public sealed class HttpResponseExtensionsTests
{
    [Fact]
    public void OffersOnlyTheCheckOfTheServerHeaderPublicly()
    {
        var publicMethods = typeof(HttpResponseExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        // A failed status is an error that the server reports, which the client reports as a
        // DbApiException, so a public check that throws an HttpRequestException for it would
        // contradict it.
        Assert.Equal([nameof(HttpResponseExtensions.ThrowIfNotValidServerHeader)], Array.ConvertAll(publicMethods, method => method.Name));
    }
}
