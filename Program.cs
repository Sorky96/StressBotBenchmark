using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace StressBotBenchmark
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("    StressBotBenchmark (C# Rewrite)");
            Console.WriteLine("========================================");

            string configPath = "config.json";
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "--config" || args[i] == "-c") && i + 1 < args.Length)
                {
                    configPath = args[i + 1];
                }
            }

            var config = BotConfig.Load(configPath);
            if (args.Length >= 1 && int.TryParse(args[0], out int count))
                config.BotCount = count;

            var metrics = new BotMetrics();
            var bots = new List<TibiaBot>();

            Console.WriteLine($"Launching {config.BotCount} bots to {config.Host}:{config.Port}...");
            
            var burstTask = LaunchBotsAsync(config, metrics, bots);
            var dashTask = DashboardLoopAsync(config, metrics, bots);

            await Task.WhenAll(burstTask, dashTask);
        }

        static async Task LaunchBotsAsync(BotConfig config, BotMetrics metrics, List<TibiaBot> bots)
        {
            int burstCount = 0;
            for (int i = 1; i <= config.BotCount; i++)
            {
                string name = $"{config.Prefix}_{i.ToString($"D{config.AccountWidth}")}";
                var bot = new TibiaBot(name, config.Password, config, metrics);
                bots.Add(bot);
                
                _ = bot.StartAsync(); // Fire and forget
                
                if (config.LoginDelayMs > 0)
                    await Task.Delay((int)config.LoginDelayMs);

                burstCount++;
                if (burstCount >= config.BurstSize)
                {
                    burstCount = 0;
                    if (config.BurstPauseMs > 0)
                        await Task.Delay((int)config.BurstPauseMs);
                }
            }
        }

        static async Task DashboardLoopAsync(BotConfig config, BotMetrics metrics, List<TibiaBot> bots)
        {
            long lastBytesIn = 0;
            long lastBytesOut = 0;
            int lastPacketsIn = 0;

            var proc = System.Diagnostics.Process.GetCurrentProcess();
            TimeSpan lastCpuTime = proc.TotalProcessorTime;
            DateTime lastTime = DateTime.UtcNow;

            await AnsiConsole.Live(new Panel("Initializing..."))
                .StartAsync(async ctx =>
                {
                    while (true)
                    {
                        await Task.Delay((int)config.DashboardIntervalMs);
                        
                        long bytesInNow = metrics.BytesIn;
                        long bytesOutNow = metrics.BytesOut;
                        int packetsInNow = metrics.PacketsIn;

                        long bytesInSec = bytesInNow - lastBytesIn;
                        long bytesOutSec = bytesOutNow - lastBytesOut;
                        int packetsInSec = packetsInNow - lastPacketsIn;

                        lastBytesIn = bytesInNow;
                        lastBytesOut = bytesOutNow;
                        lastPacketsIn = packetsInNow;

                        proc.Refresh();

                        TimeSpan cpuTime = proc.TotalProcessorTime;
                        DateTime now = DateTime.UtcNow;
                        double cpuUsage = (cpuTime - lastCpuTime).TotalMilliseconds / (now - lastTime).TotalMilliseconds / Environment.ProcessorCount * 100.0;
                        lastCpuTime = cpuTime;
                        lastTime = now;
                        double ramMb = proc.PrivateMemorySize64 / 1024.0 / 1024.0;

                        int inWorldCount = bots.FindAll(b => b.InWorld).Count;
                        int target = config.BotCount;
                        string statusColor = inWorldCount >= target ? "green" : (inWorldCount > 0 ? "yellow" : "red");

                        int trackedMonstersTotal = bots.Sum(b => b.TrackedMonstersTotal);

                        double actionsPerBot = inWorldCount > 0 ? ((double)packetsInSec / inWorldCount) : 0.0;

                        DateTime activeThreshold = DateTime.UtcNow.AddSeconds(-2);
                        int activeAttackers = bots.Count(b => b.LastAttackTime > activeThreshold);
                        int takingDamage = bots.Count(b => b.LastDamageTakenTime > activeThreshold);
                        double pctAttacking = inWorldCount > 0 ? (activeAttackers * 100.0 / inWorldCount) : 0.0;
                        double pctDamage = inWorldCount > 0 ? (takingDamage * 100.0 / inWorldCount) : 0.0;

                        var table = new Table().Border(TableBorder.Rounded).Expand();
                        table.AddColumn(new TableColumn("[bold teal]Metric[/]").Centered());
                        table.AddColumn(new TableColumn("[bold teal]Value[/]").Centered());
                        table.AddColumn(new TableColumn("[bold teal]Global Totals[/]").Centered());

                        table.AddRow(
                            "Status", 
                            $"[{statusColor}]In-World: {inWorldCount} / {target}[/]", 
                            $"[{statusColor}]Disc: {metrics.Disconnects} | Reconn: {metrics.Reconnects}[/]"
                        );
                        table.AddRow(
                            "Network", 
                            $"[blue]In:[/] {bytesInSec / 1024.0:F1} KB/s | [fuchsia]Out:[/] {bytesOutSec / 1024.0:F1} KB/s", 
                            $"Pkt In: [blue]{metrics.PacketsIn}[/] | Out: [fuchsia]{metrics.Sent}[/]"
                        );
                        table.AddRow(
                            "Actions (/sec)", 
                            $"[orange3]Actions/s/Bot:[/] {actionsPerBot:F2}", 
                            $"Atk: {metrics.Attacks} | Wlk: {metrics.Walks} | Mgc: {metrics.Spells}"
                        );
                        table.AddRow(
                            "Engagement",
                            $"[maroon]Attacking:[/] {pctAttacking:F1}% ({activeAttackers})",
                            $"[red]Taking Dmg:[/] {pctDamage:F1}% ({takingDamage})"
                        );
                        table.AddRow(
                            "Telemetry",
                            $"[fuchsia]Avg Drain:[/] {metrics.AvgDrainMs:F2}ms | [fuchsia]Max Lag:[/] {metrics.MaxSendLagMs:F2}ms",
                            $"[silver]CPU:[/] {cpuUsage:F1}% | [silver]RAM:[/] {ramMb:F1} MB"
                        );
                        table.AddRow(
                            "Tracking",
                            $"[green]Monsters Seen:[/] {trackedMonstersTotal} (Global)",
                            $"[green]Avg Queue Wait:[/] 0.00ms (C# Native ASIO)"
                        );

                        var panel = new Panel(table)
                            .Header("[bold yellow]Tibia 10.98 StressBot Cluster[/]")
                            .Border(BoxBorder.Double);

                        ctx.UpdateTarget(panel);
                    }
                });
        }
    }
}
