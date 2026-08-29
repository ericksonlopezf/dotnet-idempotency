// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;
using System.Threading.Tasks;
using EricksonLopez.Idempotency.Showcase.Levels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.Idempotency.Showcase;

/// <summary>
/// Provides the entry point for the EricksonLopez.Idempotency showcase runner.
/// </summary>
public static class Program
{
    /// <summary>
    /// Executes the showcase demonstration levels.
    /// </summary>
    /// <param name="args">The command-line arguments passed to the application.</param>
    /// <returns>A task representing the asynchronous program execution.</returns>
    public static async Task Main(string[] args)
    {
        bool isInteractive = args.Contains("--interactive");

        using IHost host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
            })
            .ConfigureServices(services =>
            {
                services.AddTransient<ILevel, Level0Conceptual>();
                services.AddTransient<ILevel, Level1QuickStart>();
                services.AddTransient<ILevel, Level2Configuration>();
                services.AddTransient<ILevel, Level3RealUseCases>();
                services.AddTransient<ILevel, Level4AdvancedIntegration>();
                services.AddTransient<ILevel, Level5Processing>();
                services.AddTransient<ILevel, Level6ErrorHandling>();
                services.AddTransient<ILevel, Level7Scalability>();
                services.AddTransient<ILevel, Level8Customization>();
                services.AddTransient<ILevel, Level9Extensions>();
                services.AddTransient<ILevel, Level10EnterpriseArchitecture>();
            })
            .Build();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine("     ERICKSONLOPEZ.IDEMPOTENCY — OFFICIAL EXECUTABLE SHOWCASE (LEVELS 00-10)    ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        var levels = host.Services.GetServices<ILevel>();
        int executedCount = 0;

        foreach (var level in levels)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[LEVEL] {level.Name}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"        {level.Description}");
            Console.ResetColor();
            Console.WriteLine(new string('-', 80));

            await level.ExecuteAsync();
            executedCount++;

            if (isInteractive)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("\nPress ENTER to continue to the next level...");
                Console.ResetColor();
                Console.ReadLine();
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n================================================================================");
        Console.WriteLine($"  ALL {executedCount} SHOWCASE LEVELS (00 THROUGH 10) EXECUTED SUCCESSFULLY!  ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();
    }
}
