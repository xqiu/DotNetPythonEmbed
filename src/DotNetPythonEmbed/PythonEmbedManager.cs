using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;

namespace DotNetPythonEmbed;

/// <summary>
/// Provides helper methods to bootstrap and interact with an embedded Python installation.
/// </summary>
public class PythonEmbedManager
{
    /// <summary>
    /// Ensures the embedded Python runtime and virtual environment exist at the specified location.
    /// If the runtime is missing it is downloaded, unpacked, bootstrapped with pip and a virtual
    /// environment named <c>venv</c> is created.
    /// </summary>
    /// <param name="pythonEmbedUrl">URL to the embedded Python zip file.</param>
    /// <param name="pythonDir">Destination directory for the Python runtime.</param>
    /// <exception cref="ArgumentException">Thrown when parameters are null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when get-pip.py cannot be located.</exception>
    public void Init(string pythonEmbedUrl, string pythonDir)
    {
        if (string.IsNullOrWhiteSpace(pythonEmbedUrl))
        {
            throw new ArgumentException("The Python embed URL must be provided.", nameof(pythonEmbedUrl));
        }

        if (string.IsNullOrWhiteSpace(pythonDir))
        {
            throw new ArgumentException("The Python directory must be provided.", nameof(pythonDir));
        }

        const string getPipUrl = "https://bootstrap.pypa.io/get-pip.py";

        var pythonExecutable = Path.Combine(pythonDir, "python.exe");
        if (File.Exists(pythonExecutable))
        {
            return;
        }

        Directory.CreateDirectory(pythonDir);

        var temporaryZip = Path.Combine(Path.GetTempPath(), $"python-embed-{Guid.NewGuid():N}.zip");
        try
        {
            DownloadFile(pythonEmbedUrl, temporaryZip);
            ExtractZip(temporaryZip, pythonDir);

            var destinationGetPip = Path.Combine(pythonDir, "get-pip.py");
            DownloadFile(getPipUrl, destinationGetPip);

            RunProcess(pythonExecutable, $"\"{destinationGetPip}\"", pythonDir);
            RunProcess(pythonExecutable, "-m venv venv", pythonDir);
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
    /// Extracts the provided zip archive into the specified Python directory.
    /// </summary>
    /// <param name="zipPath">Path to the archive containing python code.</param>
    /// <param name="pythonDir">Destination directory where the archive should be unpacked.</param>
    public void UnpackPythonCodes(string zipPath, string pythonDir)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
        {
            throw new ArgumentException("The archive path must be provided.", nameof(zipPath));
        }

        if (string.IsNullOrWhiteSpace(pythonDir))
        {
            throw new ArgumentException("The Python directory must be provided.", nameof(pythonDir));
        }

        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("The archive containing python code was not found.", zipPath);
        }

        Directory.CreateDirectory(pythonDir);
        ExtractZip(zipPath, pythonDir);
    }

    /// <summary>
    /// Installs the python requirements contained in the provided file into the virtual environment.
    /// </summary>
    /// <param name="requirementPath">Path to a requirements.txt style file.</param>
    /// <param name="pythonDir">Directory where the python runtime and virtual environment reside.</param>
    public void InstallRequirement(string requirementPath, string pythonDir)
    {
        if (string.IsNullOrWhiteSpace(requirementPath))
        {
            throw new ArgumentException("The requirement path must be provided.", nameof(requirementPath));
        }

        if (string.IsNullOrWhiteSpace(pythonDir))
        {
            throw new ArgumentException("The Python directory must be provided.", nameof(pythonDir));
        }

        if (!File.Exists(requirementPath))
        {
            throw new FileNotFoundException("The requirement file was not found.", requirementPath);
        }

        var venvPython = GetVirtualEnvironmentPythonExecutable(pythonDir);
        RunProcess(venvPython, $"-m pip install -r \"{Path.GetFullPath(requirementPath)}\"", pythonDir);
    }

    /// <summary>
    /// Executes a python script with the provided parameters inside the virtual environment.
    /// </summary>
    /// <param name="pythonFilePath">Path to the python script to execute.</param>
    /// <param name="pythonDir">Directory where the python runtime and virtual environment reside.</param>
    /// <param name="parameters">Optional parameters passed to the python script.</param>
    public void RunPython(string pythonFilePath, string pythonDir, string parameters)
    {
        if (string.IsNullOrWhiteSpace(pythonFilePath))
        {
            throw new ArgumentException("The python file path must be provided.", nameof(pythonFilePath));
        }

        if (string.IsNullOrWhiteSpace(pythonDir))
        {
            throw new ArgumentException("The Python directory must be provided.", nameof(pythonDir));
        }

        if (!File.Exists(pythonFilePath))
        {
            throw new FileNotFoundException("The python script to execute was not found.", pythonFilePath);
        }

        var venvPython = GetVirtualEnvironmentPythonExecutable(pythonDir);
        var arguments = $"\"{Path.GetFullPath(pythonFilePath)}\"";
        if (!string.IsNullOrWhiteSpace(parameters))
        {
            arguments = $"{arguments} {parameters}";
        }

        RunProcess(venvPython, arguments, Path.GetDirectoryName(Path.GetFullPath(pythonFilePath)) ?? pythonDir);
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

    protected virtual string GetVirtualEnvironmentPythonExecutable(string pythonDir)
    {
        var windowsPath = Path.Combine(pythonDir, "venv", "Scripts", "python.exe");
        if (File.Exists(windowsPath))
        {
            return windowsPath;
        }

        var unixPath = Path.Combine(pythonDir, "venv", "bin", "python");
        if (File.Exists(unixPath))
        {
            return unixPath;
        }

        throw new FileNotFoundException("The virtual environment python executable could not be located.", windowsPath);
    }

    protected virtual void RunProcess(string fileName, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit();

        var stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Process '{fileName}' exited with code {process.ExitCode}.\n{stderr}");
        }

        // Ensure asynchronous reads are observed in case of success as well.
        _ = stdoutTask.GetAwaiter().GetResult();
    }
}
