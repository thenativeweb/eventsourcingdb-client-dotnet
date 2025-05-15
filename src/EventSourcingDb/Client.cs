namespace EventSourcingDb;

public class Client
{
    private readonly Uri _baseUrl;
    private readonly string _apiToken;

    public Client(Uri baseUrl, string apiToken)
    {
        this._baseUrl = baseUrl;
        this._apiToken = apiToken;
    }
}
