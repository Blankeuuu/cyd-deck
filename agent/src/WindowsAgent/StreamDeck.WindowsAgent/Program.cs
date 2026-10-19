using Microsoft.Extensions.DependencyInjection;
using StreamDeck.Host;
using StreamDeck.WindowsAgent.Runtime;
using StreamDeck.WindowsAgent.Tray;

namespace StreamDeck.WindowsAgent;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AgentDiagnosticLog.Write($"Main start args={string.Join(' ', Environment.GetCommandLineArgs().Skip(1))}");

        using var singleInstance = SingleInstanceGuard.Acquire();
        if (!singleInstance.IsPrimaryInstance)
        {
            singleInstance.SignalPrimaryInstance();
            AgentDiagnosticLog.Write("Secondary instance signalled primary instance");
            return;
        }

        ApplicationConfiguration.Initialize();

        var services = new ServiceCollection()
            .AddSingleton(AgentOptions.FromArgs(Environment.GetCommandLineArgs().Skip(1).ToArray()))
            .AddSingleton<AgentState>()
            .AddSingleton<PairingApprovalService>()
            .AddSingleton<IPairingApprovalService>(provider => provider.GetRequiredService<PairingApprovalService>())
            .AddSingleton<WindowsStartupService>()
            .AddSingleton<WindowsAgentRuntime>()
            .AddSingleton(singleInstance)
            .AddSingleton<WindowsAgentApplicationContext>()
            .BuildServiceProvider(validateScopes: true);

        using (services)
        {
            Application.Run(services.GetRequiredService<WindowsAgentApplicationContext>());
        }
    }
}
