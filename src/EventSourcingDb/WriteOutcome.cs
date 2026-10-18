using System;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb;

// EventSourcingDB never stops a write halfway, so a write whose answer is lost may have been
// stored nonetheless. The rule is the same in every official SDK: a write is certainly not
// stored only if the connection could not be established at all, so that nothing of the request
// was sent, or if the server explicitly refused it. Every other failure is reported as an
// OutcomeUnknownException.
internal static class WriteOutcome
{
    public static async Task<T> GuardAsync<T>(
        Func<Task<T>> write,
        WriteContent content,
        Func<DbApiException, bool> isRefusal,
        CancellationToken token)
    {
        // A request that is cancelled before it starts is certainly not sent.
        token.ThrowIfCancellationRequested();

        try
        {
            return await write().ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsCertainlyNotStored(exception, content, isRefusal))
        {
            throw new OutcomeUnknownException(exception);
        }
    }

    private static bool IsCertainlyNotStored(Exception exception, WriteContent content, Func<DbApiException, bool> isRefusal)
    {
        // Until the body is handed on to a connection, nothing of the request was sent, whatever
        // failed: resolving the host, connecting, TLS, or a stall or a cancellation meanwhile.
        // Once it was, even an error of connecting leaves the outcome open, since the HttpClient
        // may have sent the request on a connection that broke before it tried again.
        if (!content.IsHandedOn)
        {
            return true;
        }

        return exception is DbApiException dbApiException && isRefusal(dbApiException);
    }
}
