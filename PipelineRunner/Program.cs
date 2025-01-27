using System.Diagnostics;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;

class Program
{
    static void Main(string[] args)
    {
        // Load configuration
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();

        var settings = new Settings();
        config.GetSection("Settings").Bind(settings);

        Console.WriteLine($"Starting file processor...");
        Console.WriteLine($"Monitoring directory: {settings.WatchDirectory}");
        Console.WriteLine($"File extension filter: {settings.FileExtension}");
        Console.WriteLine($"Scan interval: {settings.IntervalMinutes} minutes");

        while (true)
        {
            ProcessFiles(settings);
            Thread.Sleep(settings.IntervalMinutes * 60 * 1000); // Convert minutes to milliseconds
        }
    }

    static void ProcessFiles(Settings settings)
    {
        try
        {
            var files = Directory.GetFiles(settings.WatchDirectory, $"*.{settings.FileExtension}")
                               .Where(f => !IsFileLocked(new FileInfo(f)));

            var pipelineCommands = ParsePipeline(settings.Pipeline);

            foreach (var file in files)
            {
                Console.WriteLine($"Processing file: {Path.GetFileName(file)}");
                ExecutePipeline(file, pipelineCommands);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing files: {ex.Message}");
        }
    }

    static List<PipelineCommand> ParsePipeline(string pipelineString)
    {
        var commands = new List<PipelineCommand>();
        var parts = pipelineString.Split('|').Select(p => p.Trim()).ToList();

        foreach (var part in parts)
        {
            var spaceIdx = part.IndexOf(' ');
            if (spaceIdx == -1)
            {
                commands.Add(new PipelineCommand
                {
                    Executable = part,
                    Arguments = string.Empty
                });
            }
            else
            {
                commands.Add(new PipelineCommand
                {
                    Executable = part.Substring(0, spaceIdx),
                    Arguments = part.Substring(spaceIdx + 1)
                });
            }
        }

        return commands;
    }

    static void ExecutePipeline(string inputFile, List<PipelineCommand> commands)
    {
        Process lastProcess = null;
        var processes = new List<Process>();

        try
        {
            for (int i = 0; i < commands.Count; i++)
            {
                var command = commands[i];
                var isFirst = i == 0;
                var isLast = i == commands.Count - 1;

                var startInfo = new ProcessStartInfo
                {
                    FileName = command.Executable,
                    Arguments = string.Format(command.Arguments, inputFile),
                    UseShellExecute = false,
                    RedirectStandardInput = !isFirst,
                    RedirectStandardOutput = !isLast,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var process = new Process { StartInfo = startInfo };

                processes.Add(process);
                process.Start();

                // Connect to previous process output if not the first process
                if (!isFirst && lastProcess != null)
                {
                    lastProcess.StandardOutput.BaseStream.CopyToAsync(process.StandardInput.BaseStream);
                    lastProcess.StandardOutput.Close();
                }

                // If this is the first process, we need to read the input file
                if (isFirst)
                {
                    using (var fileStream = File.OpenRead(inputFile))
                    {
                        fileStream.CopyToAsync(process.StandardInput.BaseStream);
                    }
                    process.StandardInput.Close();
                }

                // Handle stderr for each process
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"Error from {command.Executable}: {e.Data}");
                    }
                };
                process.BeginErrorReadLine();

                lastProcess = process;
            }

            // Wait for all processes to complete
            foreach (var process in processes)
            {
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new Exception($"Process {process.StartInfo.FileName} failed with exit code {process.ExitCode}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in pipeline execution: {ex.Message}");
            // Cleanup any running processes
            foreach (var process in processes)
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch { }
                }
            }
        }
        finally
        {
            // Dispose all processes
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}

public class Settings
{
    public string WatchDirectory { get; set; }
    public string FileExtension { get; set; }
    public int IntervalMinutes { get; set; }
    public ApplicationConfig[] Applications { get; set; }
}

public class ApplicationConfig
{
    public string Path { get; set; }
    public string Arguments { get; set; }
}

/*

// Simple pipeline
"Pipeline": "app1.exe | app2.exe | app3.exe"

// Pipeline with arguments
"Pipeline": "app1.exe --input {0} | app2.exe --process | app3.exe --output {0}.processed"

// Pipeline with quoted paths (use escaped quotes in JSON)
"Pipeline": "\"C:\\Program Files\\App1\\app1.exe\" --input {0} | \"C:\\Program Files\\App2\\app2.exe\" --process"

{
  "Settings": {
    "WatchDirectory": "C:\\PathToWatch",
    "FileExtension": "txt",
    "IntervalMinutes": 5,
    "Pipeline": "C:\\Path\\To\\App1.exe --input {0} | C:\\Path\\To\\App2.exe --process | C:\\Path\\To\\App3.exe --output {0}.processed"
  }
}
*
