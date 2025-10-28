using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using NSec.Cryptography;

namespace EventSourcingDb;

public class Container
{
    private const string ImageName = "thenativeweb/eventsourcingdb";
    private const string SigningKeyFileName = "/signing-key.pem";
    private string _imageTag = "latest";
    private ushort _internalPort = 3000;
    private string _apiToken = "secret";
    private Key? _key;
    private IContainer? _container;

    public Container WithImageTag(string tag)
    {
        _imageTag = tag;
        return this;
    }

    public Container WithSigningKey()
    {
        _key = Key.Create(
            SignatureAlgorithm.Ed25519,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport }
        );
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
        string[] command = [
            "run",
            "--api-token", _apiToken,
            "--data-directory-temporary",
            "--http-enabled",
            "--https-enabled=false"
        ];

        var builder = new ContainerBuilder()
            .WithImage($"{ImageName}:{_imageTag}")
            .WithExposedPort(_internalPort)
            .WithPortBinding(_internalPort, assignRandomHostPort: true);

        if (_key is not null)
        {
            var key = _key.Export(KeyBlobFormat.PkixPrivateKeyText);
            builder = builder.WithResourceMapping(key, SigningKeyFileName);
            command = [.. command, $"--signing-key-file={SigningKeyFileName}"];
        }

        builder = builder.WithCommand(command)
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

    public IClient GetClient(JsonSerializerOptions? dataSerializerOptions = null)
    {
        return new Client(GetBaseUrl(), GetApiToken(), dataSerializerOptions);
    }

    public IClient GetClient(HttpClient httpClient, JsonSerializerOptions? dataSerializerOptions = null)
    {
        return new Client(httpClient, dataSerializerOptions);
    }

    public byte[] GetVerificationKey()
    {
        if (_key is null)
        {
            throw new InvalidOperationException("Signing key is not set.");
        }

        return _key.Export(KeyBlobFormat.PkixPublicKey);
    }
}
