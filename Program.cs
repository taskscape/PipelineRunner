using System.Diagnostics;
using System.Text.Json;
using PipelineRunner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

class Program
{
    public static async Task Main()
    {
        try
        {
            Config config = LoadConfig(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
            ConfigureLogger(config.LogDirectory, config.MinimumLogLevel, config.Seq);

            using IHost host = Host.CreateDefaultBuilder()
                .UseWindowsService() // Enables Windows Service functionality
                .UseSerilog()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(config);
                    services.AddHostedService<FileProcessingService>();
                })
                .Build();

            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Pipeline runner terminated unexpectedly.");
            Environment.ExitCode = 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    public class FileProcessingService : BackgroundService
    {
        private readonly Config _config;
        private readonly FileProcessor _processor;
        private readonly string? _realExeDirectory;

        public FileProcessingService(Config config)
        {
            _realExeDirectory = AppContext.BaseDirectory;
            _config = config;
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
                    Log.Information(
                        "Found {FileCount} file(s) matching {FileSearchPattern} in {WatchDirectory}.",
                        files.Length,
                        _config.FileSearchPattern,
                        _config.WatchDirectory);

                    string[] commands;
                    if (File.Exists(_config.CommandsFile))
                        commands = await File.ReadAllLinesAsync(_config.CommandsFile, stoppingToken);
                    else if (File.Exists(@$"{_realExeDirectory}\{Path.GetFileName(_config.CommandsFile)}"))
                        commands = await File.ReadAllLinesAsync(@$"{_realExeDirectory}\{Path.GetFileName(_config.CommandsFile)}", stoppingToken);
                    else
                        throw new Exception($"commands file not found! Locations tried: '{_config.CommandsFile}', '{@$"{_realExeDirectory}\{Path.GetFileName(_config.CommandsFile)}"}'");
                    
                    if (files.Length == 0)
                    {
                        Log.Information("No files found. No processing will be performed this cycle.");
                    }
                    else
                    {
                        Log.Information("Starting processing for {FileCount} file(s).", files.Length);
                    }

                    List<Task> tasks = files.Select(file => _processor.ProcessFile(file, commands)).ToList();

                    await Task.WhenAll(tasks);

                    Log.Information("Waiting {CycleTime} seconds before the next cycle.", _config.CycleTimeSeconds);
                    await Task.Delay(_config.CycleTimeSeconds * 1000, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    Log.Information("File processing service is stopping.");
                    break;
                }
                catch (Exception ex)
                {
                    const int retryDelaySeconds = 60 * 60;
                    Log.Error(ex, "Error in the service loop. Retrying in {RetryDelaySeconds} seconds.", retryDelaySeconds);
                    await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), stoppingToken);
                }
            }
        }
    }

    private static Config LoadConfig(string configPath)
    {
        string json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<Config>(json) ?? throw new Exception("Failed to load configuration.");
    }

    private static void ConfigureLogger(string logDirectory, string? minimumLevel = null, Seq? seq = null)
    {
        string localLogDirectory = Path.IsPathRooted(logDirectory)
            ? logDirectory
            : Path.Combine(AppContext.BaseDirectory, logDirectory);
        Directory.CreateDirectory(localLogDirectory);

        LoggerConfiguration loggerConfig = new LoggerConfiguration()
            .Enrich.FromLogContext();

        switch (minimumLevel?.ToLower())
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
        loggerConfig.WriteTo.File(
            Path.Combine(localLogDirectory, "log-.txt"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 31);

        if (!string.IsNullOrWhiteSpace(seq?.ServerAddress))
        {
            string? apiKey = string.IsNullOrWhiteSpace(seq.ApiKey) ? null : seq.ApiKey;
            loggerConfig.WriteTo.Seq(seq.ServerAddress, apiKey: apiKey);
        }

        Log.Logger = loggerConfig.CreateLogger();
    }
}
