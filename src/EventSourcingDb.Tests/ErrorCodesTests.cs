using System.Linq;
using System.Reflection;
using Xunit;

namespace EventSourcingDb.Tests;

public sealed class ErrorCodesTests
{
    // The slugs of all error codes, as cmd/server/api/error.go of EventSourcingDB lists them on
    // main (63877b24). When the server adds or removes a code, this list and ErrorCodes change
    // together.
    private static readonly string[] _slugsOfTheServer =
    [
        "subject-missing",
        "subject-malformed",
        "subject-too-long",
        "subject-too-deep",
        "order-unknown",
        "event-id-missing",
        "event-id-malformed",
        "bound-type-missing",
        "bound-type-unknown",
        "range-unsatisfiable",
        "from-latest-event-incomplete",
        "if-event-is-missing-unknown",
        "selection-conflicting",
        "event-type-missing",
        "event-type-malformed",
        "event-type-unknown",
        "source-missing",
        "source-malformed",
        "data-missing",
        "traceparent-malformed",
        "tracestate-malformed",
        "tracestate-without-traceparent",
        "events-missing",
        "precondition-type-missing",
        "precondition-unknown",
        "payload-missing",
        "license-invalid",
        "capacity-exceeded",
        "event-schema-violated",
        "event-schema-unregistered",
        "event-schema-missing",
        "event-schema-malformed",
        "event-schema-not-object",
        "event-schema-too-complex",
        "event-schema-already-registered",
        "last-audited-event-missing",
        "last-audited-event-mismatched",
        "last-audited-event-unknown",
        "event-hash-missing",
        "event-hash-malformed",
        "event-chain-broken",
        "last-backed-up-event-missing",
        "last-backed-up-event-mismatched",
        "last-backed-up-event-unknown",
        "store-not-empty",
        "event-hash-version-unknown",
        "backup-malformed",
        "backup-incomplete",
        "backup-format-unknown",
        "full-backup-missing",
        "backup-increment-disconnected",
        "backup-failed",
        "backup-unreadable",
        "backup-ahead-of-clock",
        "import-ahead-of-clock",
        "import-malformed",
        "import-unreadable",
        "event-too-large",
        "data-malformed",
        "query-missing",
        "query-malformed",
        "query-too-deep",
        "query-too-long",
        "query-unevaluable",
        "query-not-boolean",
        "precondition-violated",
        "store-read-only",
        "read-only-mode-already-enabled",
        "disk-full",
        "restore-in-progress",
        "store-failed",
        "indexes-rebuilding",
        "server-shutting-down",
        "file-unreadable",
    ];

    [Fact]
    public void HasAConstantForEveryCodeOfTheServerAndNoOther()
    {
        var constants = typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!);

        var expected = _slugsOfTheServer.ToDictionary(ToConstantName, slug => $"https://docs.eventsourcingdb.io/errors/{slug}");

        Assert.Equal(expected.OrderBy(pair => pair.Key), constants.OrderBy(pair => pair.Key));
    }

    // The name of a constant comes from the slug, each of its words capitalized, such as
    // PreconditionViolated for precondition-violated.
    private static string ToConstantName(string slug)
    {
        return string.Concat(slug
            .Split('-')
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }
}
