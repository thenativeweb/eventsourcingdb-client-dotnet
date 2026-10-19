using System;
using System.Threading.Tasks;
using Xunit;

namespace EventSourcingDb.Tests;

public class ContainerTests : IAsyncDisposable
{
    private Container? _container;

    [Fact]
    public async Task StartsWithACustomPort()
    {
        var imageTag = DockerfileHelper.GetImageTag();

        _container = new Container()
            .WithImageTag(imageTag)
            .WithPort(4000);

        await _container.StartAsync(TestContext.Current.CancellationToken);

        var client = _container.GetClient();

        // Should not throw.
        await client.PingAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void UsesAStartupTimeoutOfThirtySecondsByDefault()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), new Container().StartupTimeout);
    }

    [Fact]
    public void UsesTheStartupTimeoutThatIsSet()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), new Container().WithStartupTimeout(TimeSpan.FromSeconds(5)).StartupTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(uint.MaxValue - 1), new Container().WithStartupTimeout(TimeSpan.FromMilliseconds(uint.MaxValue - 1)).StartupTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(uint.MaxValue)]
    public void RejectsAStartupTimeoutThatIsNotGreaterThanZeroOrLongerThanATimerCanWait(long milliseconds)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Container().WithStartupTimeout(TimeSpan.FromMilliseconds(milliseconds))
        );

        Assert.Equal("timeout", exception.ParamName);
    }

    [Fact]
    public async Task GivesEachOf3ContainersTheStartupTimeoutToBecomeReachable()
    {
        var imageTag = DockerfileHelper.GetImageTag();

        _container = new Container()
            .WithImageTag(imageTag)
            .WithStartupTimeout(TimeSpan.FromMilliseconds(1));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _container.StartAsync(TestContext.Current.CancellationToken)
        );

        Assert.Equal("Failed to start the container, the database did not become reachable within 0.001s in any of 3 containers", exception.Message);
        Assert.False(_container.IsRunning());
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.StopAsync();
        }
    }
}
