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
        _imageTag = tag;
        return this;
    }

    public Container WithApiToken(string token)
    {
        _apiToken = token;
        return this;
    }

    public Container WithPort(ushort port)
    {
        _internalPort = port;
        return this;
    }

    public async Task StartAsync(CancellationToken token = default)
    {
        var builder = new ContainerBuilder()
            .WithImage($"{_imageName}:{_imageTag}")
            .WithExposedPort(_internalPort)
            .WithPortBinding(_internalPort, assignRandomHostPort: true)
            .WithCommand(
                "run",
                "--api-token", _apiToken,
                "--data-directory-temporary",
                "--http-enabled",
                "--https-enabled=false"
            )
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request =>
                    request.ForPort(_internalPort)
                           .ForPath("/api/v1/ping")))
            .WithCleanUp(true);

        _container = builder.Build();
        await _container.StartAsync(token).ConfigureAwait(false);
    }

    public string GetHost()
    {
        return _container?.Hostname ?? throw new InvalidOperationException("Container must be running.");
    }

    public ushort GetMappedPort()
    {
        return _container?.GetMappedPublicPort(_internalPort)
               ?? throw new InvalidOperationException("Container must be running.");
    }

    public Uri GetBaseUrl()
    {
        return new Uri($"http://{GetHost()}:{GetMappedPort()}");
    }

    public string GetApiToken()
    {
        return _apiToken;
    }

    public bool IsRunning()
    {
        return _container?.State == TestcontainersStates.Running;
    }

    public async Task StopAsync(CancellationToken token = default)
    {
        if (_container is not null)
        {
            await _container.StopAsync(token).ConfigureAwait(false);
            await _container.DisposeAsync().ConfigureAwait(false);
            _container = null;
        }
    }

    public Client GetClient()
    {
        return new Client(GetBaseUrl(), GetApiToken());
    }
}
