using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace StreamDeck.WindowsAgent;

public sealed class LocalDashboardServer(AgentState state) : IAsyncDisposable
{
    private WebApplication? app;

    public int Port { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (app is not null)
        {
            return;
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http1);
        });

        app = builder.Build();
        app.MapGet("/", () => Results.Content(DashboardHtml.Content, "text/html; charset=utf-8"));
        app.MapGet("/api/status", () => Results.Json(state.Snapshot()));

        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        Port = app.Urls.Select(TryParsePort).First(port => port > 0);
        state.SetDashboardListening(Port);
    }

    public void OpenDashboard()
    {
        if (Port <= 0)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = $"http://127.0.0.1:{Port}/",
            UseShellExecute = true
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.DisposeAsync().ConfigureAwait(false);
            app = null;
        }
    }

    private static int TryParsePort(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Port : 0;
    }
}
