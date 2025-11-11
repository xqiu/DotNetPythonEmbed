using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DotNetPythonEmbed;
using LibGit2Sharp;

var repoUrl = "https://github.com/xqiu/chatterbox.git";
var baseDirectory = AppContext.BaseDirectory;
var repoDirectory = Path.Combine(baseDirectory, "chatterbox");
var pythonDirectory = Path.Combine(baseDirectory, "python-runtime");
Process? pythonProcess = null;

var cts = new CancellationTokenSource();

// 1) Handle Ctrl+C to allow graceful cleanup and let finally run
Console.CancelKeyPress += (s, e) =>
{
    Console.WriteLine("CTRL+C received. Cancelling...");
    e.Cancel = true;                 // <- IMPORTANT: prevent immediate termination
    cts.Cancel();                    // signal cancellation to our code
    TryKillPython(pythonProcess);    // best-effort early kill
};

// 2) Handle normal process exit (e.g., SIGTERM) as a backstop
AppDomain.CurrentDomain.ProcessExit += (s, e) =>
{
    Console.WriteLine("ProcessExit fired. Best-effort cleanup...");
    TryKillPython(pythonProcess);
};

// 3) Optional: Unhandled exception backstop
AppDomain.CurrentDomain.UnhandledException += (s, e) =>
{
    Console.Error.WriteLine($"Unhandled exception: {e.ExceptionObject}");
    TryKillPython(pythonProcess);
};

try
{
    Console.WriteLine($"Cloning '{repoUrl}' to '{repoDirectory}'.");

    if (!Directory.Exists(repoDirectory) || !Directory.EnumerateFileSystemEntries(repoDirectory).Any())
    {
        Repository.Clone(repoUrl, repoDirectory);
    }
    else
    {
        Console.WriteLine("Repository already exists. pull changes.");
        // pull the latest changes
        using (var repo = new Repository(repoDirectory))
        {
            // Define pull options
            var pullOptions = new PullOptions
            {
                FetchOptions = new FetchOptions(), // no CredentialsProvider,
                MergeOptions = new MergeOptions
                {
                    FastForwardStrategy = FastForwardStrategy.Default,
                    FileConflictStrategy = CheckoutFileConflictStrategy.Theirs
                }
            };

            // Signature for the merge commit (if needed)
            var signature = new Signature("pull-bot", "pull-bot@example.com", DateTimeOffset.Now);
            // Perform the pull
            try
            {
                Commands.Pull(
                    repo,
                    signature,
                    pullOptions);

                Console.WriteLine("Pull successful!");
            }
            catch (LibGit2SharpException ex)
            {
                Console.WriteLine($"Pull failed: {ex.Message}");
                // Common cases:
                // - Conflicts: ex is UserCancelledException or MergeConflictsException
                // - Non-fast-forward: you may need to handle it manually
            }
        }
    }

    Console.WriteLine("Initializing embedded Python environment.");
    var embedManager = new PythonEmbedManager(pythonDirectory);
    await embedManager.InitPythonEnvironment(onOutput, onError);

    //auto detect cuda and install torch accordingly
    await embedManager.InstallTorchWithCudaAsync("2.6.0", "cu126", onOutput, onError);

    //install requirement by pip install -e .
    await embedManager.InstallRequirementInEditorMode(repoDirectory, onOutput, onError);

    //additional install gradio
    await embedManager.InstallPackagesAsync(new List<string>() { "gradio" }, null, onOutput, onError);

    var scriptPath = Path.Combine(repoDirectory, "gradio_vc_batch_notk.py");
    var targetDir = Path.Combine(AppContext.BaseDirectory, "target");
    var parameters = $"--target_dir {targetDir}";
    Console.WriteLine($"Executing script '{scriptPath} {parameters}'.");

    var onProcessStarted = (Process process) =>
    {
        Console.WriteLine($"Python process started with PID: {process.Id}");
        pythonProcess = process;

        // If cancellation was already requested (e.g., user spammed Ctrl+C), kill immediately.
        if (cts.IsCancellationRequested) TryKillPython(pythonProcess);

    };
    await embedManager.RunPython(scriptPath, parameters, repoDirectory, onOutput, onError, onProcessStarted);

    //embedManager.RemovePythonEnvironment(onOutput, onError);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Operation cancelled.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"An error occurred: {ex.Message}");
    Console.Error.WriteLine(ex);
}
finally
{
    // 5) This will run on Ctrl+C because we set e.Cancel = true.
    TryKillPython(pythonProcess);
}

void onOutput(string data)
{
    if (!string.IsNullOrWhiteSpace(data))
    {
        Console.WriteLine(data);
    }
}

void onError(string data)
{
    if (!string.IsNullOrWhiteSpace(data))
    {
        Console.Error.WriteLine(data);
    }
}

static void TryKillPython(Process? p)
{
    try
    {
        if (p != null && !p.HasExited)
        {
            Console.WriteLine("Killing Python process...");
            p.Kill(entireProcessTree: true);
            p.WaitForExit(5000);
        }
    }
    catch { /* swallow best-effort cleanup */ }
    finally
    {
        try { p?.Dispose(); } catch { }
    }
}
