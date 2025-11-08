using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using DotNetPythonEmbed;
using Xunit;

namespace DotNetPythonEmbed.Tests;

public sealed class PythonEmbedManagerTests : IDisposable
{
    private readonly List<string> _temporaryDirectories = new();

    [Fact]
    public void Init_ThrowsWhenPythonEmbedUrlMissing()
    {
        var manager = new PythonEmbedManager();
        var exception = Assert.Throws<ArgumentException>(() => manager.Init(string.Empty, CreateTempDirectory()));
        Assert.Equal("pythonEmbedUrl", exception.ParamName);
    }

    [Fact]
    public void Init_ThrowsWhenPythonDirectoryMissing()
    {
        var manager = new PythonEmbedManager();
        var exception = Assert.Throws<ArgumentException>(() => manager.Init("http://example.com/python.zip", string.Empty));
        Assert.Equal("pythonDir", exception.ParamName);
    }

    [Fact]
    public void Init_DownloadsAndBootstrapsPythonWhenMissing()
    {
        var pythonDir = CreateTempDirectory();
        EnsureGetPipExists();
        var manager = new RecordingPythonEmbedManager();

        manager.Init("http://example.com/python.zip", pythonDir);

        Assert.True(manager.DownloadFileCalled);
        Assert.True(manager.ExtractZipCalled);
        Assert.True(File.Exists(Path.Combine(pythonDir, "python.exe")));
        Assert.Contains(manager.RunProcessCalls, call => call.Arguments.Contains("get-pip.py", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(manager.RunProcessCalls, call => call.Arguments == "-m venv venv");
    }

    [Fact]
    public void Init_SkipsDownloadWhenPythonAlreadyExists()
    {
        var pythonDir = CreateTempDirectory();
        File.WriteAllText(Path.Combine(pythonDir, "python.exe"), string.Empty);
        var manager = new RecordingPythonEmbedManager();

        manager.Init("http://example.com/python.zip", pythonDir);

        Assert.False(manager.DownloadFileCalled);
        Assert.False(manager.ExtractZipCalled);
        Assert.Empty(manager.RunProcessCalls);
    }

    [Fact]
    public void UnpackPythonCodes_ThrowsWhenArchiveMissing()
    {
        var manager = new PythonEmbedManager();
        var exception = Assert.Throws<FileNotFoundException>(() => manager.UnpackPythonCodes(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip"), CreateTempDirectory()));
        Assert.Contains("archive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnpackPythonCodes_ExtractsArchive()
    {
        var manager = new PythonEmbedManager();
        var workingDirectory = CreateTempDirectory();
        var contentDirectory = Path.Combine(workingDirectory, "content");
        Directory.CreateDirectory(contentDirectory);
        var scriptPath = Path.Combine(contentDirectory, "script.py");
        File.WriteAllText(scriptPath, "print('hi')");

        var archivePath = Path.Combine(workingDirectory, "scripts.zip");
        ZipFile.CreateFromDirectory(contentDirectory, archivePath);

        var destination = Path.Combine(workingDirectory, "extracted");
        manager.UnpackPythonCodes(archivePath, destination);

        Assert.True(File.Exists(Path.Combine(destination, "script.py")));
    }

    [Fact]
    public void InstallRequirement_ThrowsWhenRequirementMissing()
    {
        var manager = new PythonEmbedManager();
        var pythonDir = CreateTempDirectory();
        var exception = Assert.Throws<FileNotFoundException>(() => manager.InstallRequirement(Path.Combine(pythonDir, "requirements.txt"), pythonDir));
        Assert.Contains("requirement", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallRequirement_InvokesPipInVirtualEnvironment()
    {
        var manager = new RecordingPythonEmbedManager();
        var pythonDir = CreateTempDirectory();
        var requirementPath = Path.Combine(pythonDir, "requirements.txt");
        File.WriteAllText(requirementPath, "requests==2.0.0");

        manager.InstallRequirement(requirementPath, pythonDir);

        var call = Assert.Single(manager.RunProcessCalls);
        Assert.Equal(Path.Combine(pythonDir, "venv", "bin", "python"), call.FileName);
        Assert.Contains("pip install", call.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(pythonDir, call.WorkingDirectory);
    }

    [Fact]
    public void RunPython_ThrowsWhenScriptMissing()
    {
        var manager = new PythonEmbedManager();
        var pythonDir = CreateTempDirectory();
        var exception = Assert.Throws<FileNotFoundException>(() => manager.RunPython(Path.Combine(pythonDir, "script.py"), pythonDir, null!));
        Assert.Contains("script", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunPython_ExecutesScriptThroughVirtualEnvironment()
    {
        var manager = new RecordingPythonEmbedManager();
        var pythonDir = CreateTempDirectory();
        var scriptDirectory = Path.Combine(pythonDir, "scripts");
        Directory.CreateDirectory(scriptDirectory);
        var scriptPath = Path.Combine(scriptDirectory, "script.py");
        File.WriteAllText(scriptPath, "print('test')");

        manager.RunPython(scriptPath, pythonDir, "--flag value");

        var call = Assert.Single(manager.RunProcessCalls);
        Assert.Equal(Path.Combine(pythonDir, "venv", "bin", "python"), call.FileName);
        Assert.Contains("script.py", call.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--flag value", call.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(scriptDirectory, call.WorkingDirectory);
    }

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DotNetPythonEmbedTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _temporaryDirectories.Add(directory);
        return directory;
    }

    private static void EnsureGetPipExists()
    {
        var getPipPath = Path.Combine(AppContext.BaseDirectory, "get-pip.py");
        if (!File.Exists(getPipPath))
        {
            File.WriteAllText(getPipPath, "print('placeholder')");
        }
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
        public List<(string FileName, string Arguments, string WorkingDirectory)> RunProcessCalls { get; } = new();

        protected override void DownloadFile(string url, string destination)
        {
            DownloadFileCalled = true;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, string.Empty);
        }

        protected override void ExtractZip(string sourceZip, string destination)
        {
            ExtractZipCalled = true;
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "python.exe"), string.Empty);
        }

        protected override string GetVirtualEnvironmentPythonExecutable(string pythonDir)
        {
            var path = Path.Combine(pythonDir, "venv", "bin", "python");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path))
            {
                File.WriteAllText(path, string.Empty);
            }

            return path;
        }

        protected override void RunProcess(string fileName, string arguments, string workingDirectory)
        {
            RunProcessCalls.Add((fileName, arguments, workingDirectory));
        }
    }
}
