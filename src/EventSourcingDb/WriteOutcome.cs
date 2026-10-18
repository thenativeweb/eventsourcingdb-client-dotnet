using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb;

// EventSourcingDB never stops a write halfway, so a write whose answer is lost may have been
// stored nonetheless. The rule is the same in every official SDK: a write is certainly not
// stored only if the connection could not be established at all, or if the server explicitly
// refused it. Every other failure is reported as an OutcomeUnknownException.
internal static class WriteOutcome
{
    public static async Task<T> GuardAsync<T>(
        Func<Task<T>> write,
        Func<DbApiException, bool> isRefusal,
        CancellationToken token)
    {
        // A request that is cancelled before it starts is certainly not sent.
        token.ThrowIfCancellationRequested();

        try
        {
            return await write().ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsCertainlyNotStored(exception, isRefusal))
        {
            throw new OutcomeUnknownException(exception);
        }
    }

    private static bool IsCertainlyNotStored(Exception exception, Func<DbApiException, bool> isRefusal)
    {
        return exception switch
        {
            DbApiException dbApiException => isRefusal(dbApiException),
            HttpRequestException
            {
                HttpRequestError: HttpRequestError.NameResolutionError
                    or HttpRequestError.ConnectionError
                    or HttpRequestError.SecureConnectionError
            } => true,
            _ => false,
        };
    }
}
