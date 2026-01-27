using System;
using System.Text;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EventSourcingDb.Tests;

internal class XUnitLogger<T> : ILogger<T>
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly string _categoryName;
    private readonly LoggerExternalScopeProvider _scopeProvider;

    public XUnitLogger(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _scopeProvider = new LoggerExternalScopeProvider();
        _categoryName = typeof(T).Name;
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => _scopeProvider.Push(state);

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        var sb = new StringBuilder();
        sb.Append(GetLogLevelString(logLevel))
            .Append(" [").Append(_categoryName).Append("] ")
            .Append(DateTime.Now + " ")
            .Append(formatter(state, exception));

        if (exception is not null)
        {
            sb.Append('\n').Append(exception);
        }

        _scopeProvider.ForEachScope(
            (scope, state) =>
            {
                state.Append("\n => ");
                state.Append(scope);
            }, sb
        );

        _testOutputHelper.WriteLine(sb.ToString());
    }

    private static string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel))
        };
    }
}
