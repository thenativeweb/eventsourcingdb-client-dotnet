using System.Net;

namespace EventSourcingDb;

// A server without the v2 API answers its paths with 404, as one before EventSourcingDB 1.3
// does, or with 405 where its UI serves other methods on them. Such an answer means that the
// server does not offer the v2 API even if it does not come from EventSourcingDB, as from a proxy
// that passes on only the paths of the v1 API. This is the one place that says so, for the client
// choosing its API as for the test container waiting for the database.
internal static class V2Api
{
    public static bool IsMissing(HttpStatusCode status)
    {
        return status is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed;
    }
}
