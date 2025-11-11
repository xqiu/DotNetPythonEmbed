using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using DotNetPythonEmbed;
using Xunit;

namespace DotNetPythonEmbed.Tests;

public sealed class PythonEmbedManagerTests : IDisposable
{
    private readonly List<string> _temporaryDirectories = new();

    [Fact]
    public void Constructor_ThrowsWhenPythonDirectoryMissing()
    {
        var exception = Assert.Throws<ArgumentException>(() => new PythonEmbedManager(string.Empty));
        Assert.Equal("pythonDir", exception.ParamName);
    }

    [Fact]
    public async Task InitPythonEnvironment_DownloadsAndBootstrapsPythonWhenMissing()
    {
        var pythonDir = CreateTempDirectory();
        var manager = new RecordingPythonEmbedManager(pythonDir);

        await manager.InitPythonEnvironment(_ => { }, _ => { });

        Assert.True(manager.DownloadFileCalled);
        Assert.True(manager.ExtractZipCalled);
        Assert.True(File.Exists(Path.Combine(pythonDir, "python.exe")));
        Assert.Contains(manager.DownloadFileCalls, call => call.Url == "https://bootstrap.pypa.io/get-pip.py");
        Assert.Contains(manager.RunProcessCalls, call => call.Arguments.Contains("get-pip.py", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(manager.RunProcessCalls, call => call.Arguments.Contains("-m virtualenv venv", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InitPythonEnvironment_SkipsDownloadWhenPythonAlreadyExists()
    {
        var pythonDir = CreateTempDirectory();
        File.WriteAllText(Path.Combine(pythonDir, "python.exe"), string.Empty);
        var manager = new RecordingPythonEmbedManager(pythonDir);

        await manager.InitPythonEnvironment(_ => { }, _ => { });

        Assert.False(manager.DownloadFileCalled);
        Assert.False(manager.ExtractZipCalled);
        Assert.Empty(manager.RunProcessCalls);
    }

    [Fact]
    public async Task InstallRequirement_ThrowsWhenRequirementMissing()
    {
        var manager = new PythonEmbedManager(CreateTempDirectory());
        var pythonDir = CreateTempDirectory();
        await Assert.ThrowsAsync<FileNotFoundException>(() => manager.InstallRequirement(Path.Combine(pythonDir, "requirements.txt"), _ => { }, _ => { }));
    }

    [Fact]
    public async Task InstallPackagesAsync_ThrowsWhenPackagesNull()
    {
        var manager = new RecordingPythonEmbedManager(CreateTempDirectory());
        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.InstallPackagesAsync(null!, null, _ => { }, _ => { }));
    }

    [Fact]
    public async Task InstallPackagesAsync_ThrowsWhenPackagesEmpty()
    {
        var manager = new RecordingPythonEmbedManager(CreateTempDirectory());
        await Assert.ThrowsAsync<ArgumentException>(() => manager.InstallPackagesAsync(new[] { "  ", string.Empty }, null, _ => { }, _ => { }));
    }

    [Fact]
    public async Task InstallPackagesAsync_InvokesPipWithPackagesAndIndex()
    {
        var manager = new RecordingPythonEmbedManager(CreateTempDirectory());

        await manager.InstallPackagesAsync(new[] { "torch", "custom package" }, "https://example.com/simple", _ => { }, _ => { });

        var call = Assert.Single(manager.RunProcessCalls);
        Assert.Contains("-m pip install", call.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("torch", call.Arguments, StringComparison.Ordinal);
        Assert.Contains("\"custom package\"", call.Arguments, StringComparison.Ordinal);
        Assert.Contains("--index-url", call.Arguments, StringComparison.Ordinal);
        Assert.Contains("https://example.com/simple", call.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallRequirement_InvokesPipInVirtualEnvironment()
    {
        var manager = new RecordingPythonEmbedManager(CreateTempDirectory());
        var pythonDir = manager.GetPythonDir();
        var requirementPath = Path.Combine(pythonDir, "requirements.txt");
        File.WriteAllText(requirementPath, "requests==2.0.0");

        await manager.InstallRequirement(requirementPath, _ => { }, _ => { });

        var call = Assert.Single(manager.RunProcessCalls);
        Assert.Contains("venv", call.FileName);
        Assert.Contains("python", call.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pip install", call.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(call.EnvironmentVariables);
        Assert.Equal(Path.Combine(manager.GetPythonDir(), "venv"), Assert.Contains("VIRTUAL_ENV", call.EnvironmentVariables!));
    }

    [Fact]
    public async Task RunPython_ThrowsWhenScriptMissing()
    {
        var manager = new PythonEmbedManager(CreateTempDirectory());
        var pythonDir = CreateTempDirectory();
        await Assert.ThrowsAsync<FileNotFoundException>(() => manager.RunPython(Path.Combine(pythonDir, "script.py"), null!, null!, _ => { }, _ => { }, _ => { }));
    }

    [Fact]
    public async Task RunPython_ExecutesScriptThroughVirtualEnvironment()
    {
        var pythonDir = CreateTempDirectory();
        var manager = new RecordingPythonEmbedManager(pythonDir);
        var scriptDirectory = Path.Combine(pythonDir, "scripts");
        Directory.CreateDirectory(scriptDirectory);
        var scriptPath = Path.Combine(scriptDirectory, "script.py");
        File.WriteAllText(scriptPath, "print('test')");

        await manager.RunPython(scriptPath, "--flag value", null, _ => { }, _ => { }, _ => { });

        var call = Assert.Single(manager.RunProcessCalls);
        Assert.Contains("venv", call.FileName);
        Assert.Contains("python", call.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("script.py", call.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--flag value", call.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(call.EnvironmentVariables);
        Assert.Equal(Path.Combine(manager.GetPythonDir(), "venv"), Assert.Contains("VIRTUAL_ENV", call.EnvironmentVariables!));
    }

    [Fact]
    public async Task InstallTorchWithCudaAsync_UsesProvidedCudaOverride()
    {
        var manager = new RecordingPythonEmbedManager(CreateTempDirectory());

        var result = await manager.InstallTorchWithCudaAsync("2.5.1", "cu126", _ => { }, _ => { });

        Assert.Equal(0, result);

        var call = Assert.Single(manager.RunProcessCalls);
        Assert.Contains("--index-url", call.Arguments, StringComparison.Ordinal);
        Assert.Contains("download.pytorch.org/whl/cu126", call.Arguments, StringComparison.Ordinal);
        Assert.Contains("torch==2.5.1+cu126", call.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallTorchWithCudaAsync_ReturnsErrorWhenCudaCannotBeDetected()
    {
        var manager = new RecordingPythonEmbedManager(CreateTempDirectory());
        var errors = new List<string>();

        var result = await manager.InstallTorchWithCudaAsync(null, null, _ => { }, errors.Add);

        Assert.Equal(-1, result);
        Assert.NotEmpty(errors);
        Assert.Empty(manager.RunProcessCalls);
    }

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DotNetPythonEmbedTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _temporaryDirectories.Add(directory);
        return directory;
    }

    public void Dispose()
    {
        foreach (var directory in _temporaryDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup failures in tests.
            }
        }
    }

    private sealed class RecordingPythonEmbedManager : PythonEmbedManager
    {
        public bool DownloadFileCalled { get; private set; }
        public bool ExtractZipCalled { get; private set; }
        public List<(string Url, string Destination)> DownloadFileCalls { get; } = new();
        public List<(string FileName, string Arguments, string? WorkingDirectory, Dictionary<string, string>? EnvironmentVariables)> RunProcessCalls { get; } = new();

        public RecordingPythonEmbedManager(string pythonDir) : base(pythonDir)
        {
        }

        public string GetPythonDir() => typeof(PythonEmbedManager).GetProperty("PythonDir", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(this) as string ?? string.Empty;

        protected override void DownloadFile(string url, string destination)
        {
            DownloadFileCalled = true;
            DownloadFileCalls.Add((url, destination));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, string.Empty);
        }

        protected override void ExtractZip(string sourceZip, string destination)
        {
            ExtractZipCalled = true;
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "python.exe"), string.Empty);
        }

        protected override string GetVirtualEnvironmentPythonExecutable()
        {
            var pythonDir = GetPythonDir();
            var path = Path.Combine(pythonDir, "venv", "bin", "python");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path))
            {
                File.WriteAllText(path, string.Empty);
            }

            return path;
        }

        protected override async Task<int> RunProcess(string fileName, string arguments, string? workingDirectory, Dictionary<string, string>? environmentVariables, Action<string> onOutput, Action<string> onError, Action<Process> onProcessStarted)
        {
            RunProcessCalls.Add((fileName, arguments, workingDirectory, environmentVariables is null ? null : new Dictionary<string, string>(environmentVariables)));
            onProcessStarted?.Invoke(new Process());
            return await Task.FromResult(0);
        }
    }
}
