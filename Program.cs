using System.Diagnostics;
using System.Text.Json;
using PipelineRunner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

class Program
{
    public static async Task Main()
    {
        var host = Host.CreateDefaultBuilder()
            .UseWindowsService() // Enables Windows Service functionality
            .ConfigureServices(services =>
            {
                services.AddHostedService<FileProcessingService>();
            })
            //.UseSerilog()
            .Build();

        await host.RunAsync();
    }

    public class FileProcessingService : BackgroundService
    {
        private readonly Config _config;
        private readonly FileProcessor _processor;
        private readonly string? _realExeDirectory;

        public FileProcessingService()
        {
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
            _realExeDirectory = Path.GetDirectoryName(exePath);
            _config = LoadConfig(@$"{_realExeDirectory}\appsettings.json");
            ConfigureLogger(_config.LogDirectory, _config.MinimumLogLevel, _config.Seq);
            _processor = new FileProcessor(_config);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield(); //The reason for it is to "release" the Task so that Host.StartAsync can continue.
            Log.Information("File processing service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    string[] files = Directory.GetFiles(_config.WatchDirectory, _config.FileSearchPattern);
                    string[] commands;
                    if (File.Exists(_config.CommandsFile))
                        commands = File.ReadAllLines(_config.CommandsFile);
                    else if (File.Exists(@$"{_realExeDirectory}\{Path.GetFileName(_config.CommandsFile)}"))
                        commands = File.ReadAllLines(@$"{_realExeDirectory}\{Path.GetFileName(_config.CommandsFile)}");
                    else
                        throw new Exception($"commands file not found! Locations tried: '{_config.CommandsFile}', '{@$"{_realExeDirectory}\{Path.GetFileName(_config.CommandsFile)}"}'");
                    
                    List<Task> tasks = new();

                    foreach (var file in files)
                    {
                        tasks.Add(_processor.ProcessFile(file, commands));
                    }

                    await Task.WhenAll(tasks);

                    Log.Information("Waiting {CycleTime} seconds before the next cycle.", _config.CycleTimeSeconds);
                    await Task.Delay(_config.CycleTimeSeconds * 1000, stoppingToken);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in the service loop.");
                }
            }
        }
    }

    static Config LoadConfig(string configPath)
    {
        string json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<Config>(json) ?? throw new Exception("Failed to load configuration.");
    }

    static void ConfigureLogger(string logDirectory, string? MinimumLevel = null, Seq? seq = null)
    {
        Directory.CreateDirectory(logDirectory);

        var loggerConfig = new LoggerConfiguration();

        switch (MinimumLevel?.ToLower())
        {
            case "verbose":
                loggerConfig.MinimumLevel.Verbose();
                break;
            case "debug":
                loggerConfig.MinimumLevel.Debug();
                break;
            case "information":
                loggerConfig.MinimumLevel.Information();
                break;
            case "warning":
                loggerConfig.MinimumLevel.Warning();
                break;
            case "error":
                loggerConfig.MinimumLevel.Error();
                break;
            default:
                loggerConfig.MinimumLevel.Information();
                break;
        }

        if (!string.IsNullOrEmpty(seq?.AppName))
            loggerConfig.Enrich.WithProperty("Application", seq.AppName);
        loggerConfig.WriteTo.Console();
        loggerConfig.WriteTo.File(Path.Combine(logDirectory, "log-.txt"), rollingInterval: RollingInterval.Day);
        if(!string.IsNullOrEmpty(seq?.ServerAddress))
            loggerConfig.WriteTo.Seq(seq.ServerAddress);
        Log.Logger = loggerConfig.CreateLogger();
    }
}
