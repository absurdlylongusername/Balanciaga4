using Microsoft.Extensions.Logging;

namespace Balanciaga4.IntegrationTests.Helpers;

public class NUnitLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new NUnitLogger(categoryName);
    public void Dispose() { }

    private class NUnitLogger(string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) => default!;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId,
                                TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            TestContext.Out.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{logLevel}] {category}: {message}");
            if (exception != null)
            {
                TestContext.Out.WriteLine(exception);
            }
        }
    }
}

public static class NUnitLogger
{
    private static readonly ILoggerFactory Factory = LoggerFactory.Create(builder =>
    {
        builder.AddProvider(new NUnitLoggerProvider());
    });

    public static ILogger<T> CreateLogger<T>() => Factory.CreateLogger<T>();
    public static ILogger CreateLogger(string categoryName) => Factory.CreateLogger(categoryName);
}