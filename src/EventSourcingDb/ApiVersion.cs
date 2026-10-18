namespace EventSourcingDb;

// The API through which the client talks to a server. Servers before EventSourcingDB 1.3 only
// offer v1.
internal enum ApiVersion
{
    V1,
    V2
}
