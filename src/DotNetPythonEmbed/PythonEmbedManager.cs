using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace DotNetPythonEmbed;

/// <summary>
/// Provides helper methods to bootstrap and interact with an embedded Python installation.
/// </summary>
public class PythonEmbedManager
{
    private string PythonDir { get; set; }

    /// <summary>
    /// Python Embed Url, default to https://www.python.org/ftp/python/3.11.6/python-3.11.6-embed-amd64.zip, can be overridden to use a different version, before calling Init()
    /// </summary>
    public string PythonEmbedUrl { get; set; } = "https://www.python.org/ftp/python/3.11.6/python-3.11.6-embed-amd64.zip";

    public PythonEmbedManager(string pythonDir)
    {
        if (string.IsNullOrWhiteSpace(pythonDir))
        {
            throw new ArgumentException("The Python directory must be provided.", nameof(pythonDir));
        }
        PythonDir = pythonDir;
    }

    /// <summary>
    /// Ensures the embedded Python runtime and virtual environment exist at the specified location.
    /// If the runtime is missing it is downloaded, unpacked, bootstrapped with pip and a virtual
    /// environment named <c>venv</c> is created.
    /// </summary>
    /// <param name="onOutput">Output callback.</param>
    /// <param name="onError">Error callback.</param>
    /// <exception cref="ArgumentException">Thrown when parameters are null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when get-pip.py cannot be located.</exception>
    /// <returns>0 if initialization is successful, otherwise an error code.</returns>
    public async Task<int> InitPythonEnvironment(Action<string> onOutput, Action<string> onError)
    {
        const string getPipUrl = "https://bootstrap.pypa.io/get-pip.py";

        var pythonExecutable = Path.Combine(PythonDir, "python.exe");
        if (File.Exists(pythonExecutable))
        {
            return 0;
        }

        Directory.CreateDirectory(PythonDir);

        var temporaryZip = Path.Combine(Path.GetTempPath(), $"python-embed-{Guid.NewGuid():N}.zip");
        try
        {
            DownloadFile(PythonEmbedUrl, temporaryZip);
            ExtractZip(temporaryZip, PythonDir);

            var destinationGetPip = Path.Combine(PythonDir, "get-pip.py");
            DownloadFile(getPipUrl, destinationGetPip);

            var result = await RunProcess(pythonExecutable, $"\"{destinationGetPip}\"", null, null, onOutput, onError);
            if(result != 0)
            {
                return result;
            }

            // remove *._pth file to uncomment #import site
            var pthFiles = Directory.GetFiles(PythonDir, "python*._pth", SearchOption.TopDirectoryOnly);
            foreach (var pthFile in pthFiles)
            {
                var lines = File.ReadAllLines(pthFile);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("#import site", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = "import site";
                    }
                }
                File.WriteAllLines(pthFile, lines);
            }

            result = await RunProcess(pythonExecutable, "-m pip install virtualenv", null, null, onOutput, onError);
            if (result != 0)
            {
                return result;
            }

            result = await RunProcess(pythonExecutable, "-m virtualenv venv", null, null, onOutput, onError);
            return result;

        }
        catch (Exception)
        {
            // Cleanup partially created pythonDir on failure
            if (Directory.Exists(PythonDir))
            {
                Directory.Delete(PythonDir, recursive: true);
            }
            throw;
        }
        finally
        {
            if (File.Exists(temporaryZip))
            {
                File.Delete(temporaryZip);
            }
        }
    }

    /// <summary>
    /// Completely remove the python environment we installed from init
    /// </summary>
    /// <param name="onOutput">Output callback.</param>
    /// <param name="onError">Error callback.</param>
    /// <returns>0 if remove is successful, otherwise an error code.</returns>
    public int RemovePythonEnvironment(Action<string> onOutput, Action<string> onError)
    {
        // delete PythonDir
        try
        {
            if (Directory.Exists(PythonDir))
            {
                Directory.Delete(PythonDir, recursive: true);
                onOutput?.Invoke($"Deleted Python environment at {PythonDir}");
            }
            else
            {
                onOutput?.Invoke($"Python environment at {PythonDir} does not exist.");
            }
            return 0;
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Error deleting Python environment at {PythonDir}: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Installs the python requirements contained in the provided file into the virtual environment.
    /// </summary>
    /// <param name="requirementPath">Path to a requirements.txt style file.</param>
    /// <param name="onOutput">Output callback.</param>
    /// <param name="onError">Error callback.</param>
    /// <returns>0 if initialization is successful, otherwise an error code.</returns>
    public async Task<int> InstallRequirement(string requirementPath, Action<string> onOutput, Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(requirementPath))
        {
            throw new ArgumentException("The requirement path must be provided.", nameof(requirementPath));
        }

        if (!File.Exists(requirementPath))
        {
            throw new FileNotFoundException("The requirement file was not found.", requirementPath);
        }

        var venvPython = GetVirtualEnvironmentPythonExecutable();
        return await RunProcess(venvPython, $"-m pip install -r \"{Path.GetFullPath(requirementPath)}\"", null, GetEnvironmentVars(), onOutput, onError);
    }

    /// <summary>
    /// Installs the provided packages inside the embedded virtual environment.
    /// </summary>
    /// <param name="packages">Collection of package arguments to pass to pip.</param>
    /// <param name="extraIndexUrl">Optional extra index URL that will be appended using <c>--index-url</c>.</param>
    /// <param name="onOutput">Output callback.</param>
    /// <param name="onError">Error callback.</param>
    /// <returns>0 if installation succeeds, otherwise a non-zero exit code from pip.</returns>
    public async Task<int> InstallPackagesAsync(IEnumerable<string> packages, string? extraIndexUrl, Action<string> onOutput, Action<string> onError)
    {
        if (packages is null)
        {
            throw new ArgumentNullException(nameof(packages));
        }

        var packageList = packages.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
        if (packageList.Count == 0)
        {
            throw new ArgumentException("At least one package must be provided.", nameof(packages));
        }

        var argumentsBuilder = new StringBuilder("-m pip install");
        foreach (var package in packageList)
        {
            argumentsBuilder.Append(' ');
            argumentsBuilder.Append(QuoteArgument(package));
        }

        if (!string.IsNullOrWhiteSpace(extraIndexUrl))
        {
            argumentsBuilder.Append(" --index-url ");
            argumentsBuilder.Append(QuoteArgument(extraIndexUrl!));
        }

        var venvPython = GetVirtualEnvironmentPythonExecutable();
        return await RunProcess(venvPython, argumentsBuilder.ToString(), null, GetEnvironmentVars(), onOutput, onError);
    }

    /// <summary>
    /// Installs the CUDA-enabled PyTorch stack that matches the machine's CUDA version.
    /// </summary>
    /// <param name="torchVersion">Optional PyTorch version to install (e.g. "2.6.0").</param>
    /// <param name="cudaOverride">Optional CUDA tag or version override (e.g. "cu126" or "12.6").</param>
    /// <param name="onOutput">Output callback.</param>
    /// <param name="onError">Error callback.</param>
    /// <returns>0 if installation succeeds, otherwise a non-zero exit code.</returns>
    public async Task<int> InstallTorchWithCudaAsync(string? torchVersion, string? cudaOverride, Action<string> onOutput, Action<string> onError)
    {
        var cudaTag = NormalizeCudaTag(cudaOverride);
        if (string.IsNullOrEmpty(cudaTag))
        {
            cudaTag = await DetectCudaTag(onOutput, onError);
        }

        if (string.IsNullOrEmpty(cudaTag))
        {
            onError?.Invoke("Unable to detect the CUDA version for PyTorch installation.");
            return -1;
        }

        var packages = BuildTorchPackageList(torchVersion, cudaTag);

        var indexUrl = $"https://download.pytorch.org/whl/{cudaTag}";
        return await InstallPackagesAsync(packages, indexUrl, onOutput, onError);
    }

    /// <summary>
    /// Runs a Python script with the specified parameters.
    /// </summary>
    /// <param name="pythonScript">Path to the Python script to execute.</param>
    /// <param name="parameters">Command-line parameters to pass to the script.</param>
    /// <param name="workingDirectory">Working directory for the script execution.</param>
    /// <param name="onOutput">Output callback.</param>
    /// <param name="onError">Error callback.</param>
    /// <returns>0 if initialization is successful, otherwise an error code.</returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="FileNotFoundException"></exception>
    public async Task<int> RunPython(string pythonScript, string parameters, string workingDirectory, Action<string> onOutput, Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(pythonScript))
        {
            throw new ArgumentException("The Python script path must be provided.", nameof(pythonScript));
        }

        if (!File.Exists(pythonScript))
        {
            throw new FileNotFoundException("The Python script to execute was not found.", pythonScript);
        }

        var venvPython = GetVirtualEnvironmentPythonExecutable();
        var arguments = $"\"{Path.GetFullPath(pythonScript)}\"";
        if (!string.IsNullOrWhiteSpace(parameters))
        {
            arguments = $"{arguments} {parameters}";
        }
        // Run the process with the adjusted environment variables
        return await RunProcess(venvPython, arguments, workingDirectory, GetEnvironmentVars(), onOutput, onError);
    }

    protected virtual void DownloadFile(string url, string destination)
    {
        using var httpClient = new HttpClient();
        using var response = httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        using var httpStream = response.Content.ReadAsStream();
        using var fileStream = File.Create(destination);
        httpStream.CopyTo(fileStream);
    }

    protected virtual void ExtractZip(string sourceZip, string destination)
    {
        if (Directory.Exists(destination))
        {
            ZipFile.ExtractToDirectory(sourceZip, destination, overwriteFiles: true);
        }
        else
        {
            ZipFile.ExtractToDirectory(sourceZip, destination);
        }
    }

    protected virtual string GetVirtualEnvironmentPythonExecutable()
    {
        var windowsPath = Path.Combine(PythonDir, "venv", "Scripts", "python.exe");
        if (File.Exists(windowsPath))
        {
            return windowsPath;
        }

        var unixPath = Path.Combine(PythonDir, "venv", "bin", "python");
        if (File.Exists(unixPath))
        {
            return unixPath;
        }

        throw new FileNotFoundException("The virtual environment python executable could not be located.", windowsPath);
    }

    protected Dictionary<string, string> GetEnvironmentVars()
    {
        // Prepare the environment variables as if the venv was activated
        var venvEnvVars = new Dictionary<string, string>
        {
            { "VIRTUAL_ENV", Path.Combine(PythonDir, "venv") }, // Set VIRTUAL_ENV
            { "PATH", Path.Combine(PythonDir, "venv", "Scripts") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH") }, // Modify PATH to include venv\Scripts
            { "PYTHONHOME", PythonDir }, // Optional: Set PYTHONHOME to the pythonDir
        };
        return venvEnvVars;
    }

    protected virtual async Task<int> RunProcess(string fileName, string arguments, string? workingDirectory, Dictionary<string, string>? environmentVariables, Action<string> onOutput, Action<string> onError)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? PythonDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (environmentVariables != null)
        {
            // Set environment variables for the process if provided
            foreach (var envVar in environmentVariables)
            {
                startInfo.EnvironmentVariables[envVar.Key] = envVar.Value;
            }
        }

        Console.WriteLine($"Executing process in {PythonDir}> {fileName} {arguments}");

        using var process = new Process { StartInfo = startInfo };

        // Subscribe to the output and error streams to capture them in real-time
        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                onOutput?.Invoke(e.Data);  // Call the output handler
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                onError?.Invoke(e.Data);  // Call the error handler
            }
        };

        process.Start();

        // Start async reading the output and error streams
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();  // Use async wait to avoid blocking the UI

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync();
            Console.WriteLine($"Process '{fileName}' exited with code {process.ExitCode}.\n{stderr}");
        }

        return process.ExitCode;
    }

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        if (!argument.Any(char.IsWhiteSpace) && !argument.Contains('"'))
        {
            return argument;
        }

        return $"\"{argument.Replace("\"", "\\\"")}\"";
    }

    private static string? NormalizeCudaTag(string? cudaOverride)
    {
        if (string.IsNullOrWhiteSpace(cudaOverride))
        {
            return null;
        }

        var trimmed = cudaOverride.Trim().ToLowerInvariant();
        if (trimmed.StartsWith("cu", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var versionMatch = Regex.Match(trimmed, "^(?<major>\\d+)(?:\\.(?<minor>\\d+))?");
        if (versionMatch.Success)
        {
            var major = versionMatch.Groups["major"].Value;
            var minor = versionMatch.Groups["minor"].Success ? versionMatch.Groups["minor"].Value : "0";
            return $"cu{major}{minor}";
        }

        return trimmed;
    }

    private async Task<string?> DetectCudaTag(Action<string> onOutput, Action<string> onError)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await errorTask;
                if (!string.IsNullOrWhiteSpace(error))
                {
                    onError?.Invoke(error.Trim());
                }
                return null;
            }

            var output = await outputTask;
            var match = Regex.Match(output, @"CUDA Version:\s*(?<major>\d+)\.(?<minor>\d+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var major = match.Groups["major"].Value;
                var minor = match.Groups["minor"].Value;
                return $"cu{major}{minor}";
            }

            var envCuda = Environment.GetEnvironmentVariable("CUDA_VERSION");
            if (!string.IsNullOrWhiteSpace(envCuda))
            {
                return NormalizeCudaTag(envCuda);
            }
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Failed to detect CUDA version: {ex.Message}");
        }

        return null;
    }

    private static IReadOnlyList<string> BuildTorchPackageList(string? torchVersion, string cudaTag)
    {
        if (string.IsNullOrWhiteSpace(torchVersion))
        {
            return new[]
            {
                "torch",
                "torchvision",
                "torchaudio"
            };
        }

        var trimmed = torchVersion.Trim();
        if (trimmed.Length == 0)
        {
            return new[]
            {
                "torch",
                "torchvision",
                "torchaudio"
            };
        }

        var torchSpecifier = BuildTorchSpecifier(trimmed, cudaTag);
        return new[]
        {
            torchSpecifier,
            "torchvision",
            "torchaudio"
        };
    }

    private static string BuildTorchSpecifier(string versionInput, string cudaTag)
    {
        if (versionInput.Contains('+', StringComparison.Ordinal))
        {
            if (versionInput.Contains("torch", StringComparison.OrdinalIgnoreCase))
            {
                return versionInput;
            }

            return versionInput.StartsWith("torch", StringComparison.OrdinalIgnoreCase)
                ? versionInput
                : $"torch=={versionInput}";
        }

        if (versionInput.StartsWith("torch", StringComparison.OrdinalIgnoreCase))
        {
            if (!versionInput.Contains("==", StringComparison.Ordinal))
            {
                return versionInput;
            }

            var parts = versionInput.Split("==", 2, StringSplitOptions.None);
            if (parts.Length == 2 && !parts[1].Contains('+', StringComparison.Ordinal))
            {
                return $"{parts[0]}=={parts[1]}+{cudaTag}";
            }

            return versionInput;
        }

        return $"torch=={versionInput}+{cudaTag}";
    }
}
