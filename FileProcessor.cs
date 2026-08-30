using System.Diagnostics;
using Serilog;

namespace PipelineRunner
{
    public class FileProcessor(Config config)
    {
        public Task ProcessFile(string file, string[] commands)
        {
            try
            {
                try
                {
                    bool hasWarnings = false;
                    bool hasErrors = false;

                    Log.Information("Processing file: {File}", file);

                    Dictionary<string, string?> outputs = new Dictionary<string, string?>();
                    string? lastOutput = $"\"{file}\""; // Start with input file

                    foreach (string command in commands)
                    {
                        if (string.IsNullOrWhiteSpace(command)) continue;

                        // Replace placeholders {input} and {output}
                        string processedCommand =
                            command.Replace("{input}", $"\"{file}\"").Replace("{output}", lastOutput);
                        // Replace placeholders {programName}output
                        foreach (KeyValuePair<string, string?> item in outputs)
                            processedCommand = processedCommand.Replace($"{{{item.Key}}}", item.Value);

                        Log.Information(processedCommand);

                        int exitCode = RunProcessWithTimeout(processedCommand, out lastOutput);
                        Log.Debug("Command '{Command}' output:\n{output}\n", processedCommand, lastOutput);
                        if (exitCode != 0)
                        {
                            if (config.ContinueOnError)
                            {
                                Log.Warning("Command '{Command}' exited with non zero code!. Exit code: {exitCode}",
                                    processedCommand, exitCode);
                                hasWarnings = true;
                                continue;
                            }

                            Log.Error("Command '{Command}' exited with non zero code!. Exit code: {exitCode}",
                                processedCommand, exitCode);
                            hasErrors = true;
                            break;
                        }

                        lastOutput = lastOutput.Trim();

                        //Line Prefix filter
                        if (!string.IsNullOrEmpty(config.UseLineFilterPrefix))
                        {
                            string[] lines = lastOutput.Split([Environment.NewLine], StringSplitOptions.None);
                            lastOutput =
                                (from line in lines
                                    where line.StartsWith(config.UseLineFilterPrefix)
                                    select line.Substring(config.UseLineFilterPrefix.Length).TrimEnd())
                                .FirstOrDefault();
                        }

                        if (string.IsNullOrWhiteSpace(lastOutput))
                        {
                            if (config.ContinueOnError)
                            {
                                Log.Warning("Command '{Command}' did not return a valid output.", processedCommand);
                                hasWarnings = true;
                                StoreOutputFromCommand(outputs, command, lastOutput);
                                continue;
                            }

                            Log.Error("Command '{Command}' did not return a valid output.", processedCommand);
                            hasErrors = true;
                            break;
                        }

                        if (!lastOutput.Contains('"'))
                            lastOutput = $"\"{lastOutput}\"";

                        StoreOutputFromCommand(outputs, command, lastOutput);
                    }

                    if (hasErrors)
                        Log.Error("Finished processing (With erros, output file may not be created) file: {File}",
                            file);
                    else if (hasWarnings)
                        Log.Warning("Finished processing (With warnings, output file may not be created) file: {File}",
                            file);
                    else
                        Log.Information("Finished processing (Succesfully) file: {File}", file);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error processing file: {File}", file);
                }

                return Task.CompletedTask;
            }
            catch (Exception exception)
            {
                return Task.FromException(exception);
            }
        }

        //Store the output as {programName}output variable in dictionary
        private static void StoreOutputFromCommand(Dictionary<string, string?> dict, string command, string? output)
        {
            string outputsKeyName = Path.GetFileNameWithoutExtension(command.Split(' ', 2)[0].Trim()) + "output";
            if (!dict.ContainsKey(outputsKeyName))
                dict.Add(outputsKeyName, output);
            else
                dict[outputsKeyName] = output;
        }

        private int RunProcessWithTimeout(string commandLine, out string output)
        {
            output = string.Empty;
            string[] parts = commandLine.Split(' ', 2);
            if (parts.Length < 1) return -1;

            string program = parts[0];
            string arguments = parts.Length > 1 ? parts[1] : "";

            using Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = program,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
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
