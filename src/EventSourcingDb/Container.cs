using System;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace EventSourcingDb;

public class Container
{
    private string _imageName = "thenativeweb/eventsourcingdb";
    private string _imageTag = "latest";
    private ushort _internalPort = 3000;
    private string _apiToken = "secret";
    private IContainer? _container;

    public Container WithImageTag(string tag)
    {
        this._imageTag = tag;
        return this;
    }

    public Container WithApiToken(string token)
    {
        this._apiToken = token;
        return this;
    }

    public Container WithPort(ushort port)
    {
        this._internalPort = port;
        return this;
    }

    public async Task StartAsync(CancellationToken token = default)
    {
        var builder = new ContainerBuilder()
            .WithImage($"{this._imageName}:{this._imageTag}")
            .WithExposedPort(this._internalPort)
            .WithPortBinding(this._internalPort, assignRandomHostPort: true)
            .WithCommand(
                "run",
                "--api-token", this._apiToken,
                "--data-directory-temporary",
                "--http-enabled",
                "--https-enabled=false"
            )
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request =>
                    request.ForPort(this._internalPort)
                           .ForPath("/api/v1/ping")))
            .WithCleanUp(true);

        this._container = builder.Build();
        await this._container.StartAsync(token).ConfigureAwait(false);
    }

    public string GetHost()
    {
        return this._container?.Hostname ?? throw new InvalidOperationException("Container must be running.");
    }

    public ushort GetMappedPort()
    {
        return this._container?.GetMappedPublicPort(this._internalPort)
               ?? throw new InvalidOperationException("Container must be running.");
    }

    public Uri GetBaseUrl()
    {
        return new Uri($"http://{this.GetHost()}:{this.GetMappedPort()}");
    }

    public string GetApiToken()
    {
        return this._apiToken;
    }

    public bool IsRunning()
    {
        return this._container?.State == TestcontainersStates.Running;
    }

    public async Task StopAsync(CancellationToken token = default)
    {
        if (this._container is not null)
        {
            await this._container.StopAsync(token).ConfigureAwait(false);
            await this._container.DisposeAsync().ConfigureAwait(false);
            this._container = null;
        }
    }

    public Client GetClient()
    {
        return new Client(this.GetBaseUrl(), this.GetApiToken());
    }
}
