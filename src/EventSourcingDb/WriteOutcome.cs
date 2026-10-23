using System;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb;

// EventSourcingDB never stops a write halfway, so a write whose answer is lost may have been
// stored nonetheless. The rule is the same in every official SDK: a write is certainly not
// stored only if the connection could not be established at all, so that nothing of the request
// was sent, if the server explicitly refused it, if the client refused it before sending it, as
// for a recursive precondition on a server without the v2 API, or if finding out which API the
// server offers failed before it was sent. Every other failure is reported as an
// OutcomeUnknownException.
internal static class WriteOutcome
{
    // The content must stay undisposed until the write has ended, since telling the outcome
    // apart may need to look at it.
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
        catch (Exception exception)
        {
            // Once the body refused to be sent again, what the write failed with tells about an
            // attempt that never reached the server, while the first one may have been stored.
            if (content.SentAgainCause is { } sentAgainCause)
            {
                throw new OutcomeUnknownException(sentAgainCause);
            }

            if (await IsCertainlyNotStoredAsync(exception, content, isRefusal).ConfigureAwait(false))
            {
                throw;
            }

            throw new OutcomeUnknownException(exception);
        }
    }

    private static async Task<bool> IsCertainlyNotStoredAsync(Exception exception, WriteContent content, Func<DbApiException, bool> isRefusal)
    {
        // Until the body is handed on to a connection, nothing of the request was sent, whatever
        // failed: resolving the host, connecting, TLS, or a stall or a cancellation meanwhile.
        // Once it was, even an error of connecting leaves the outcome open, since the HttpClient
        // may have sent the request on a connection that broke before it tried again.
        if (!content.IsHandedOn)
        {
            return true;
        }

        if (exception is not DbApiException dbApiException || !isRefusal(dbApiException))
        {
            return false;
        }

        // A refusal only tells about the attempt that it answers. If a handler sent the write
        // again, an earlier attempt may have been stored, and lost its answer.
        return !await content.MayHaveBeenSentMoreThanOnceAsync().ConfigureAwait(false);
    }
}
