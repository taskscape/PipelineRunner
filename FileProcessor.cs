using System.Diagnostics;
using Serilog;

namespace PipelineRunner
{
    public class FileProcessor
    {
        private readonly Config config;
        public FileProcessor(Config config)
        {
            this.config = config;
        }

        public async Task ProcessFile(string file)
        {
            try
            {
                Log.Information("Processing file: {File}", file);

                Dictionary<string, string?> outputs = new Dictionary<string, string?>();
                string? lastOutput = $"\"{file}\""; // Start with input file
                var commands = File.ReadAllLines(config.CommandsFile);

                foreach (var command in commands)
                {
                    if (string.IsNullOrWhiteSpace(command)) continue;
                    
                    // Replace placeholders {input} and {output}
                    string processedCommand = command.Replace("{input}", $"\"{file}\"").Replace("{output}", lastOutput);
                    // Replace placeholders {programName}output
                    foreach (var item in outputs)
                        processedCommand = processedCommand.Replace($"{{{item.Key}}}", item.Value);

                    Log.Information(processedCommand);

                    int exitCode = RunProcessWithTimeout(processedCommand, out lastOutput);
                    if (exitCode != 0)
                    {
                        if (config.ContinueOnError)
                        {
                            Log.Warning("Command '{Command}' exited with non zero code!. Exit code: {exitCode}", processedCommand, exitCode);
                            continue;
                        }
                        Log.Error("Command '{Command}' exited with non zero code!. Exit code: {exitCode}", processedCommand, exitCode);
                        break;
                    }
                    lastOutput = lastOutput.Trim();

                    Log.Debug("Command '{Command}' output:\n{output}\n", processedCommand, lastOutput);

                    //Line Prefix filter
                    if(!string.IsNullOrEmpty(config.UseLineFilterPrefix))
                    {
                        string[] lines = lastOutput.Split([Environment.NewLine], StringSplitOptions.None);
                        lastOutput = null;
                        foreach (string line in lines)
                        {
                            if (line.StartsWith(config.UseLineFilterPrefix))
                            {
                                lastOutput = line.Substring(config.UseLineFilterPrefix.Length).TrimEnd();
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(lastOutput))
                    {
                        if (config.ContinueOnError)
                        {
                            Log.Warning("Command '{Command}' did not return a valid output.", processedCommand);
                            StoreOutputFromCommand(outputs, command, lastOutput);
                            continue;
                        }
                        Log.Error("Command '{Command}' did not return a valid output.", processedCommand);
                        break;
                    }

                    if (!lastOutput.Contains('"'))
                        lastOutput = $"\"{lastOutput}\"";

                    StoreOutputFromCommand(outputs, command, lastOutput);
                }

                Log.Information("Finished processing: {File}", file);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing file: {File}", file);
            }
        }

        //Store the output as {programName}output variable in dictionary
        public static void StoreOutputFromCommand(Dictionary<string, string?> dict, string command, string? output)
        {
            string outputsKeyName = Path.GetFileNameWithoutExtension(command.Split(' ', 2)[0].Trim()) + "output";
            if (!dict.ContainsKey(outputsKeyName))
                dict.Add(outputsKeyName, output);
            else
                dict[outputsKeyName] = output;
        }

        int RunProcessWithTimeout(string commandLine, out string output)
        {
            output = string.Empty;
            string[] parts = commandLine.Split(' ', 2);
            if (parts.Length < 1) return -1;

            string program = parts[0];
            string arguments = parts.Length > 1 ? parts[1] : "";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = program,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            try
            {
                process.Start();
                if (!process.WaitForExit(config.ProcessTimeoutSeconds * 1000))
                {
                    process.Kill();
                    return process.ExitCode;
                }
                output = process.StandardOutput.ReadToEnd();
                return process.ExitCode;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error executing {Program} with arguments {Arguments}", program, arguments);
                throw;
            }
        }
    }
}
