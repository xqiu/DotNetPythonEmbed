using System;
using System.IO;
using System.Linq;
using DotNetPythonEmbed;
using LibGit2Sharp;

var repoUrl = "https://github.com/xqiu/EdgeTTS-Batch-Audio-Converter.git";
var baseDirectory = AppContext.BaseDirectory;
var repoDirectory = Path.Combine(baseDirectory, "EdgeTTS-Batch-Audio-Converter");
var pythonDirectory = Path.Combine(baseDirectory, "python-runtime");
var pythonEmbedUrl = "https://www.python.org/ftp/python/3.11.6/python-3.11.6-embed-amd64.zip";

try
{
    Console.WriteLine($"Cloning '{repoUrl}' to '{repoDirectory}'.");

    if (!Directory.Exists(repoDirectory) || !Directory.EnumerateFileSystemEntries(repoDirectory).Any())
    {
        Repository.Clone(repoUrl, repoDirectory);
    }
    else
    {
        Console.WriteLine("Repository already exists. Skipping clone.");
    }

    Console.WriteLine("Initializing embedded Python environment.");
    var embedManager = new PythonEmbedManager();
    embedManager.Init(pythonEmbedUrl, pythonDirectory);

    var requirementsPath = Path.Combine(repoDirectory, "requirements.txt");
    if (File.Exists(requirementsPath))
    {
        Console.WriteLine("Installing python requirements.");
        embedManager.InstallRequirement(requirementsPath, pythonDirectory);
    }
    else
    {
        Console.WriteLine("No requirements.txt file found; skipping dependency installation.");
    }

    var scriptPath = Path.Combine(repoDirectory, "EdgeTTS_Batch_Audio_Converter.py");
    Console.WriteLine($"Executing script '{scriptPath}'.");
    embedManager.RunPython(scriptPath, pythonDirectory, string.Empty);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"An error occurred: {ex.Message}");
    Console.Error.WriteLine(ex);
}
