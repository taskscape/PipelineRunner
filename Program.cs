using System.Text.Json;
using PipelineRunner;
using Serilog;

class Program
{
    static async Task Main()
    {
        Config config = LoadConfig();
        ConfigureLogger(config.LogDirectory, config.MinimumLogLevel, config.Seq);

        Log.Information("File processing service started.");

        while (true)
        {
            try
            {
                string[] files = Directory.GetFiles(config.WatchDirectory, config.FileSearchPattern);
                FileProcessor processor = new(config);
                List<Task> tasks = [];
                foreach (var file in files)
                {
                    tasks.Add(processor.ProcessFile(file));
                }

                await Task.WhenAll(tasks);

                Log.Information("Waiting {CycleTime} seconds before the next cycle.", config.CycleTimeSeconds);
                await Task.Delay(config.CycleTimeSeconds * 1000);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in the main application loop.");
            }
        }
    }

    static Config LoadConfig()
    {
        string json = File.ReadAllText("appsettings.json");
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
