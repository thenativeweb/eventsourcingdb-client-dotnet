using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;

namespace EventSourcingDb;

public class Container
{
    private const string ImageName = "thenativeweb/eventsourcingdb";
    private const string SigningKeyFileName = "/signing-key.pem";

    internal static readonly TimeSpan DefaultStartupTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan _maxStartupTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private string _imageTag = "latest";
    private ushort _internalPort = 3000;
    private string _apiToken = "secret";
    private Key? _key;
    private TimeSpan _startupTimeout = DefaultStartupTimeout;
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

    /// <summary>
    /// Sets how long <see cref="StartAsync"/> waits for the database in a container to become
    /// reachable, from creating the container until the database answers a ping. Pulling the image
    /// does not count. A container that runs, but whose database does not become reachable in time,
    /// is replaced, up to 3 containers in total. Defaults to 30 seconds.
    /// </summary>
    public Container WithStartupTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > _maxStartupTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                FormattableString.Invariant($"The startup timeout must be greater than zero and at most {_maxStartupTimeout.TotalMilliseconds} milliseconds.")
            );
        }

        _startupTimeout = timeout;
        return this;
    }

    internal TimeSpan StartupTimeout => _startupTimeout;

    public async Task StartAsync(CancellationToken token = default)
    {
        string[] command = [
            "run",
            "--api-token", _apiToken,
            "--data-directory-temporary",
            "--http-enabled",
            "--https-enabled=false",
            "--http-port", _internalPort.ToString(CultureInfo.InvariantCulture)
        ];

        var builder = new ContainerBuilder($"{ImageName}:{_imageTag}");

        if (_key is not null)
        {
            var key = _key.Export(KeyBlobFormat.PkixPrivateKeyText);
            builder = builder.WithResourceMapping(key, SigningKeyFileName);
            command = [.. command, $"--signing-key-file={SigningKeyFileName}"];
        }

        builder = builder.WithCommand(command)
            .WithCleanUp(true);

        _container = await ContainerStart.StartAsync(builder, _internalPort, _startupTimeout, token).ConfigureAwait(false);
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

    public async Task PauseAsync(CancellationToken token = default)
    {
        if (_container is not null)
        {
            await _container.PauseAsync(token).ConfigureAwait(false);
        }
    }

    public async Task UnpauseAsync(CancellationToken token = default)
    {
        if (_container is not null)
        {
            await _container.UnpauseAsync(token).ConfigureAwait(false);
        }
    }

    public IClient GetClient(JsonSerializerOptions? dataSerializerOptions = null, ILogger<Client>? logger = null)
    {
        return new Client(GetBaseUrl(), GetApiToken(), dataSerializerOptions, logger);
    }

    public IClient GetClient(HttpClient httpClient, JsonSerializerOptions? dataSerializerOptions = null, ILogger<Client>? logger = null)
    {
        return new Client(httpClient, dataSerializerOptions, logger);
    }

    public byte[] GetSigningKey()
    {
        if (_key is null)
        {
            throw new InvalidOperationException("Signing key is not set.");
        }

        return _key.Export(KeyBlobFormat.PkixPrivateKey);
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
