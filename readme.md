# **PipelineRunner**

## **Overview**
This application automates file processing by scanning a specified directory for files matching a pattern and executing a sequence of external programs on each file. The execution sequence is defined in a separate `commands.txt` file. The application runs in cycles, logging all activity and handling timeouts.

## **Configuration (appsettings.json)**
All configuration settings are stored in `appsettings.json`.

### **General Settings**
| Parameter                 | Type    | Description |
|---------------------------|---------|-------------|
| `WatchDirectory`          | string  | **(required)** Path to the directory to be monitored for new files. |
| `FileSearchPattern`       | string  | **(required)** Search pattern for files (e.g., `"*.pdf"`). |
| `CommandsFile`            | string  | **(required)** Path to the file containing the list of commands to execute. |
| `CycleTimeSeconds`        | int     | **(required)** Delay (in seconds) between each cycle of file processing. |
| `ProcessTimeoutSeconds`   | int     | **(required)** Maximum time allowed for each external process to complete |
| `ContinueOnError`         | boolean | (Optional) If `true`, the application continues running the next program even if an error occurs. Default: `false`. |

### **Logging Configuration**
| Parameter           | Type    | Description |
|---------------------|---------|-------------|
| `LogDirectory`      | string  | (Optional) Directory where logs will be saved. Default: `logs`. The folder is created if it doesn't exist. |
| `MinimumLogLevel`   | string  | (Optional) Minimum log level (supported values:`verbose`, `debug`, `information`, `warning`, `error`) Default: `information`. |

### **Output Handling**
| Parameter              | Type    | Description |
|------------------------|---------|-------------|
| `UseLineFilterPrefix`  | string  | (Optional) If set, the program searches for a line with the specified prefix in the command output and uses it as input for the next command (without the prefix). Useful when handling debug outputs. |

## **Commands File (commands.txt)**
The `commands.txt` file defines the list of programs to execute for each file. Each command should be on a new line.

### **Syntax:**
```
<program> {input}
<program> {output}
```
- `{input}` refers to the original file being processed.
- `{output}` refers to the output from the previous command.
- If `UseLineFilterPrefix` is set, the application will use the prefixed line as `{output}` (without the prefix itself).

- *Alternatively, `{<program>output}` can be used to reference the latest output from a specified program. For example, when using `pdfToImage.exe`, the corresponding output can be accessed with `{pdfToImageoutput}`. **Note:** This reference is case-sensitive.*

### **Example `commands.txt`**
```
pdfToImage.exe {input}
imageCompressor.exe {output}
cloudUploader.exe {output}
imageCompressorFileLog {imageCompressoroutput}
```
This will:
1. Convert a PDF to an image.
2. Compress the image.
3. Upload the compressed image to the cloud.
4. Log the compressed image.

## **How It Works**
1. The application scans `WatchDirectory` for files matching `FileSearchPattern`.
2. It reads the list of commands from `commands.txt`.
3. For each file:
   - Executes the first command, replacing `{input}` with the file name.
   - Captures the output (optionally filtering with `UseLineFilterPrefix`).
   - Passes the output to the next command as `{output}`.
   - If `ContinueOnError` is `false`, execution stops on the first failure.
4. Logs all activity.
5. Waits for `CycleTimeSeconds` before starting the next cycle.

## **Logging**
- Logs are stored in `LogDirectory`.
- Includes process execution details, errors, and timeouts.

## **Error Handling**
- If a process times out, it is terminated and logged.
- If `ContinueOnError` is `true`, the next command continues executing.
- If `ContinueOnError` is `false`, execution stops on failure.