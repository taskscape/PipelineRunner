# PipelineRunner

PipelineRunner is a Windows worker that repeatedly finds files matching a pattern and runs an ordered command pipeline for each file. It is suitable for unattended conversion, enrichment, compression, upload, or similar file-processing workflows.

It can run interactively for validation or as a Windows service for continuous operation. Each run writes structured events to local rolling log files and, when configured, to Seq.

## Before you begin

- For a framework-dependent deployment, install the .NET 10 runtime on the target computer. A self-contained publish does not need a separately installed runtime.
- Put PipelineRunner, `appsettings.json`, and `commands.txt` in the same deployment folder. The application reads `appsettings.json` from its executable directory.
- Ensure the account that runs PipelineRunner can read the watch directory and command tools, write the log directory, and access any output locations. It also needs network access to Seq when Seq logging is enabled.
- Commands are re-run for every matching file on every cycle. A successful pipeline should therefore move, rename, delete, or otherwise make its input no longer match the configured search pattern.

## Quick start

1. Build the application from the repository root:

   ```powershell
   dotnet build .\PipelineRunner.sln --configuration Release
   ```

2. Edit `appsettings.json` and `commands.txt` as described below.

3. Test it interactively before installing it as a service:

   ```powershell
   dotnet run --project .\PipelineRunner.csproj --configuration Release
   ```

   Use `Ctrl+C` to stop the interactive run. Check the log folder for startup, processing, and error events.

## Configuration

All settings are stored in `appsettings.json`. Paths may be absolute. `LogDirectory` may also be relative; a relative log path is resolved from the executable directory, which makes it safe to use when running as a Windows service.

```json
{
  "WatchDirectory": "D:\\Incoming",
  "FileSearchPattern": "*.pdf",
  "CommandsFile": "commands.txt",
  "UseLineFilterPrefix": "RET-OUTPUT: ",
  "CycleTimeSeconds": 300,
  "LogDirectory": "logs",
  "ProcessTimeoutSeconds": 300,
  "MinimumLogLevel": "information",
  "ContinueOnError": true,
  "Seq": {
    "ServerAddress": "https://seq.example.internal",
    "AppName": "PipelineRunner",
    "ApiKey": null
  }
}
```

| Setting | Required | Description |
|---|---:|---|
| `WatchDirectory` | Yes | Directory scanned at the start of each cycle. |
| `FileSearchPattern` | Yes | `Directory.GetFiles` search pattern, for example `*.pdf`. |
| `CommandsFile` | Yes | Command-pipeline file. An absolute path is used directly; otherwise the runner also looks for its file name next to the executable. |
| `CycleTimeSeconds` | Yes | Delay after a completed scan before the next scan. Use a positive value. |
| `ProcessTimeoutSeconds` | No | Maximum duration of each external command before PipelineRunner terminates it. Defaults to 900 seconds (15 minutes); use a positive value. |
| `ContinueOnError` | No | `false` stops the current file's pipeline after a failing command or empty result. `true` records a warning and continues with later commands. Defaults to `true`. |
| `UseLineFilterPrefix` | No | If specified, only the first command-output line beginning with this exact prefix becomes the next `{output}` value; the prefix is removed. |
| `LogDirectory` | No | Local log folder. Defaults to `logs`; relative values are relative to the executable directory. |
| `MinimumLogLevel` | No | One of `verbose`, `debug`, `information`, `warning`, or `error`. Defaults to `information`. |
| `Seq.ServerAddress` | No | Seq ingestion endpoint. Omit or leave empty to disable Seq while retaining local file logging. |
| `Seq.AppName` | No | Value written as the `Application` property on log events. |
| `Seq.ApiKey` | No | Seq ingestion API key. It is passed to the Seq sink and is never written to PipelineRunner logs. |

### Seq API keys

To enable authenticated Seq ingestion, set both `Seq.ServerAddress` and `Seq.ApiKey`:

```json
"Seq": {
  "ServerAddress": "https://seq.example.internal",
  "AppName": "PipelineRunner",
  "ApiKey": "replace-with-a-Seq-ingestion-key"
}
```

The API key is stored in plain text in this configuration format. Do not commit a production configuration containing a real key. Restrict the deployment folder and `appsettings.json` with NTFS permissions to the service account and administrators, and restart the service after changing the key or Seq address.

## Command pipelines

`commands.txt` contains one command per line. Empty lines are ignored. A command's first space-separated token is the executable; the rest of the line is passed as its arguments. Consequently, executable paths containing spaces are not currently supported; install or invoke tools through a path without spaces.

Available placeholders are:

| Placeholder | Meaning |
|---|---|
| `{input}` | The original matching file, quoted. |
| `{output}` | The previous command's processed standard output. |
| `{programNameoutput}` | The most recent output from a named command. `programName` is the command executable's file name without its extension and is case-sensitive. |

Example:

```text
pdfToImage.exe {input}
imageCompressor.exe {output}
cloudUploader.exe {output}
imageCompressorFileLog.exe {imageCompressoroutput}
```

For every matching file, PipelineRunner starts the first command with `{input}`, captures its standard output, and passes that output to the next command. It processes matching files concurrently, so command tools and output paths must tolerate concurrent execution. A command that returns a non-zero exit code, times out, or produces no usable output is logged.

## Logging and troubleshooting

- Local logs are always written as daily rolling `log-*.txt` files in `LogDirectory`; the latest 31 files are retained.
- If `Seq.ServerAddress` is configured, the same structured events are also sent to Seq.
- Startup and host lifecycle events use the same Serilog configuration as file-processing events.
- Every successful scan reports the watch directory, search pattern, and number of matching files. A zero-match cycle explicitly reports that no files will be processed; matched files are logged individually as processing begins.
- A normal stop cancels the current wait and is logged as a service shutdown, not an error. A real service-loop error is logged and retried after one hour. Inspect the local log first if the service remains running but no files are processed.

Common checks:

1. Confirm `WatchDirectory` exists and that the service account can read it.
2. Confirm the command executable and all input/output paths are reachable by the service account, not only by your interactive user.
3. Check the most recent `log-*.txt` file for the command, exit code, exception, or timeout.
4. If Seq is configured, verify its URL, firewall/proxy access, and ingestion API key. Local file logging continues independently of successful Seq delivery.

## Publish and deploy

The recommended service package is the repository's `PipelineRunner-win-x64` publish profile. It creates a self-contained Windows x64 single-file executable and deliberately excludes `appsettings.json` and `commands.txt` so operational configuration and commands are not distributed in the package:

```powershell
dotnet publish .\PipelineRunner.csproj -p:PublishProfile=PipelineRunner-win-x64
```

The output folder is `bin\Release\net10.0\win-x64\publish`. Before starting the service, place the approved `appsettings.json` and `commands.txt` beside the deployed EXE; PipelineRunner will not start without them. The profile deliberately disables trimming and ReadyToRun: the small startup benefit does not justify the larger or less predictable deployment for a long-running Generic Host and Serilog service.

To build a framework-dependent Windows deployment instead:

```powershell
dotnet publish .\PipelineRunner.csproj --configuration Release --runtime win-x64 --self-contained false --output .\publish
```

Copy the complete contents of `publish` to a permanent deployment directory, for example `C:\Program Files\PipelineRunner`. Make sure that directory contains `PipelineRunner.exe`, `appsettings.json`, and `commands.txt`. Edit the copied configuration before starting the service.

For a target that cannot have the .NET runtime installed, publish self-contained instead:

```powershell
dotnet publish .\PipelineRunner.csproj --configuration Release --runtime win-x64 --self-contained true --output .\publish
```

## Install as a Windows service

Run the following in an **elevated PowerShell** after deploying to `C:\Program Files\PipelineRunner`. Choose a service name that is unique on the machine.

```powershell
New-Service -Name "PipelineRunner" -BinaryPathName '"C:\Program Files\PipelineRunner\PipelineRunner.exe"' -DisplayName "Pipeline Runner" -Description "Runs configured file-processing command pipelines." -StartupType Automatic
sc.exe failure "PipelineRunner" reset= 86400 actions= restart/60000/restart/60000/restart/60000
Start-Service -Name "PipelineRunner"
Get-Service -Name "PipelineRunner"
```

Expected result: `Get-Service` reports `Running`, a local log file appears under the configured `LogDirectory`, and events appear in Seq if it is configured and reachable.

The service initially runs as `LocalSystem`. If the watch folder, command tools, network shares, or Seq access require a specific identity, open **Services** (`services.msc`), open **Pipeline Runner** properties, select **Log On**, set the approved service account, then restart the service. Grant that account only the required read, write, and network permissions.

### Update a deployed service

1. Stop the service: `Stop-Service -Name "PipelineRunner"`.
2. Replace the deployed files, preserving or deliberately updating `appsettings.json` and `commands.txt`.
3. Start the service: `Start-Service -Name "PipelineRunner"`.
4. Confirm the service status and check the newest local log file.

## Uninstall the Windows service

Run these commands in an **elevated PowerShell**:

```powershell
Stop-Service -Name "PipelineRunner" -ErrorAction SilentlyContinue
sc.exe delete "PipelineRunner"
```

`sc.exe delete` removes the service registration; Windows may keep it marked for deletion until all Services consoles and handles are closed. Verify that it is gone with:

```powershell
Get-Service -Name "PipelineRunner"
```

After the service is deleted, retain or archive `appsettings.json` and the local log files if they are needed for audit or troubleshooting. Then remove the deployment directory manually when it is no longer required. Deleting the service does not delete files, configuration, or logs.

## Security and operating guidance

- Treat `commands.txt` as executable operational configuration. Limit write permission to trusted administrators.
- Apply the same least-privilege permissions to the watch folder, command tools, output locations, log folder, and `appsettings.json`.
- Do not put production Seq API keys in source control or command-line arguments.
- Test changed pipelines interactively with representative non-production files before restarting the production service.
