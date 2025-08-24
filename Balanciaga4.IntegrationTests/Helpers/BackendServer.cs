using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Balanciaga4.IntegrationTests.Helpers;

public sealed class BackendServer : IAsyncDisposable
{
    private int Port { get; }
    private string Marker { get; }
    private IHost? _host;
    private ILogger Logger { get; }

    public BackendServer(ILogger logger, int port, string marker)
    {
        Logger = logger;
        Port = port;
        Marker = marker;
    }

    public async Task StartAsync(string? bigFilePath = null, CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(BackendServer).Assembly.FullName
        });

        builder.WebHost.UseKestrel(o =>
        {
            o.ListenLocalhost(Port);
        });

        var app = builder.Build();
        //
        // app.MapGet("/", () =>
        // {
        //     Logger.LogInformation("Index hit {Port}", Port);
        //     return Results.Text(Marker, "text/plain");
        // });
        //
        // if (!string.IsNullOrWhiteSpace(bigFilePath))
        // {
        //     app.MapGet("/big.bin", async context =>
        //     {
        //         context.Response.ContentType = "application/octet-stream";
        //         await using var fileStream = File.OpenRead(bigFilePath!);
        //         await fileStream.CopyToAsync(context.Response.Body, cancellationToken);
        //     });
        // }

        app.Run(async context =>
        {
            // If the client asked to close, confirm it in the response
            if (context.Request.Headers.Connection.ToString()
                       .Contains("close", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.Connection = "close";
            }

            if (bigFilePath != null && context.Request.Path == "/big.bin")
            {
                Logger.LogInformation("Big file hit {Port}", Port);
                context.Response.ContentType = "application/octet-stream";
                await using var fileStream = File.OpenRead(bigFilePath);
                await fileStream.CopyToAsync(context.Response.Body, cancellationToken);
            }
            else
            {
                Logger.LogInformation("Index hit {Port}", Port);
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(Marker, cancellationToken);
            }
        });

        _host = app;
        Logger.LogInformation("Starting backend server {Marker} http://localhost:{Port}", Marker, Port);
        await app.StartAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is null) return;
        await _host.StopAsync();
        _host.Dispose();
        _host = null;
    }
}
