# DotNetPythonEmbed

[![NuGet](https://img.shields.io/nuget/v/DotNetPythonEmbed.svg)](https://www.nuget.org/packages/DotNetPythonEmbed/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A .NET 8 library that provides utilities for managing an embedded Python distribution and virtual environments within .NET applications. This package enables seamless integration of Python scripts and packages into your .NET projects without requiring users to have Python installed on their system.

## Features

- 🐍 **Automatic Python Distribution Management** - Downloads and extracts an embedded Python distribution on first use
- 📦 **Virtual Environment Support** - Creates and manages isolated Python virtual environments
- 🔧 **Pip Integration** - Installs Python packages from requirements.txt files
- 🚀 **Script Execution** - Run Python scripts with full environment isolation
- 🔄 **Cross-Platform Ready** - Supports both Windows and Unix-based systems
- 🧹 **Cleanup Utilities** - Remove Python environments when no longer needed

## Installation

Install the package via NuGet:

```bash
dotnet add package DotNetPythonEmbed
```

Or via the Package Manager Console:

```powershell
Install-Package DotNetPythonEmbed
```

## Quick Start

```csharp
using DotNetPythonEmbed;

// Define the directory where Python will be installed
var pythonDirectory = Path.Combine(AppContext.BaseDirectory, "python-runtime");

// Create the manager
var embedManager = new PythonEmbedManager(pythonDirectory);

// Initialize the Python environment (downloads Python if not present)
await embedManager.InitPythonEnvironment(
    onOutput: Console.WriteLine,
    onError: Console.Error.WriteLine
);

// Install dependencies from requirements.txt
var requirementsPath = "path/to/requirements.txt";
if (File.Exists(requirementsPath))
{
    await embedManager.InstallRequirement(requirementsPath, Console.WriteLine, Console.Error.WriteLine);
}

// Run a Python script
var scriptPath = "path/to/script.py";
await embedManager.RunPython(scriptPath, "--arg1 value1", null, Console.WriteLine, Console.Error.WriteLine);
```

## Usage

### 1. Initialize the Python Environment

```csharp
var pythonDir = Path.Combine(AppContext.BaseDirectory, "python-runtime");
var manager = new PythonEmbedManager(pythonDir);

// Optional: Override the default Python version
manager.PythonEmbedUrl = "https://www.python.org/ftp/python/3.13.9/python-3.13.9-embed-amd64.zip";

// Initialize (downloads and sets up Python + pip + virtualenv)
await manager.InitPythonEnvironment(
    onOutput: msg => Console.WriteLine(msg),
    onError: err => Console.Error.WriteLine(err)
);
```

The `InitPythonEnvironment` method:
- Checks if Python is already installed at the specified directory
- If not, downloads the embedded Python distribution
- Extracts the distribution
- Installs pip
- Creates a virtual environment named `venv`

### 2. Install Python Packages

```csharp
var requirementsPath = Path.Combine(repoDirectory, "requirements.txt");

await manager.InstallRequirement(
requirementsPath, 
    onOutput: Console.WriteLine,
    onError: Console.Error.WriteLine
);
```

The `InstallRequirement` method uses pip within the virtual environment to install packages specified in a requirements.txt file.

### 3. Run Python Scripts

```csharp
var scriptPath = Path.Combine(projectDirectory, "script.py");
var workingDirectory = projectDirectory;
var scriptArguments = "--input data.txt --output results.json";

int exitCode = await manager.RunPython(
    pythonScript: scriptPath,
    parameters: scriptArguments,
    workingDirectory: workingDirectory,
    onOutput: Console.WriteLine,
    onError: Console.Error.WriteLine
);

if (exitCode != 0)
{
    Console.WriteLine($"Script failed with exit code: {exitCode}");
}
```

### 4. Clean Up

```csharp
// Remove the Python environment if needed
manager.RemovePythonEnvironment(
    onOutput: Console.WriteLine,
    onError: Console.Error.WriteLine
);
```

## Complete Example

Here's a complete example that clones a GitHub repository and runs a Python script from it:

```csharp
using DotNetPythonEmbed;

var repoUrl = "https://github.com/username/python-project.git";
var baseDirectory = AppContext.BaseDirectory;
var repoDirectory = Path.Combine(baseDirectory, "python-project");
var pythonDirectory = Path.Combine(baseDirectory, "python-runtime");

try
{
 // Clone the repository (using LibGit2Sharp or similar)
    Console.WriteLine($"Cloning '{repoUrl}' to '{repoDirectory}'.");
    if (!Directory.Exists(repoDirectory))
    {
   // Repository.Clone(repoUrl, repoDirectory);
 }

    // Initialize Python environment
    Console.WriteLine("Initializing embedded Python environment.");
    var embedManager = new PythonEmbedManager(pythonDirectory);
  await embedManager.InitPythonEnvironment(OnOutput, OnError);

    // Install requirements
    var requirementsPath = Path.Combine(repoDirectory, "requirements.txt");
    if (File.Exists(requirementsPath))
    {
  Console.WriteLine("Installing Python requirements.");
        await embedManager.InstallRequirement(requirementsPath, OnOutput, OnError);
    }

 // Run the script
    var scriptPath = Path.Combine(repoDirectory, "main.py");
    Console.WriteLine($"Executing script '{scriptPath}'.");
    int exitCode = await embedManager.RunPython(
        scriptPath, 
        string.Empty, 
     repoDirectory, 
        OnOutput, 
        OnError
    );

    Console.WriteLine($"Script completed with exit code: {exitCode}");

    // Optional: Clean up
    embedManager.RemovePythonEnvironment(OnOutput, OnError);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"An error occurred: {ex.Message}");
}

void OnOutput(string data)
{
    if (!string.IsNullOrWhiteSpace(data))
        Console.WriteLine(data);
}

void OnError(string data)
{
    if (!string.IsNullOrWhiteSpace(data))
        Console.Error.WriteLine(data);
}
```

## API Reference

### PythonEmbedManager Constructor

```csharp
public PythonEmbedManager(string pythonDir)
```

Creates a new instance of the PythonEmbedManager.

**Parameters:**
- `pythonDir`: The directory where Python will be installed and managed.

**Throws:**
- `ArgumentException`: If `pythonDir` is null or empty.

### Properties

#### PythonEmbedUrl

```csharp
public string PythonEmbedUrl { get; set; }
```

Gets or sets the URL to download the embedded Python distribution. Default is Python 3.11.6 for Windows AMD64.

### Methods

#### InitPythonEnvironment

```csharp
public async Task<int> InitPythonEnvironment(Action<string> onOutput, Action<string> onError)
```

Initializes the Python environment by downloading, extracting, and setting up Python with pip and virtualenv.

**Returns:** `0` on success, otherwise an error code.

#### InstallRequirement

```csharp
public async Task<int> InstallRequirement(string requirementPath, Action<string> onOutput, Action<string> onError)
```

Installs Python packages from a requirements.txt file.

**Parameters:**
- `requirementPath`: Path to the requirements.txt file.
- `onOutput`: Callback for standard output.
- `onError`: Callback for error output.

**Returns:** `0` on success, otherwise an error code.

**Throws:**
- `ArgumentException`: If `requirementPath` is null or empty.
- `FileNotFoundException`: If the requirements file doesn't exist.

#### RunPython

```csharp
public async Task<int> RunPython(string pythonScript, string parameters, string workingDirectory, Action<string> onOutput, Action<string> onError)
```

Executes a Python script with optional parameters.

**Parameters:**
- `pythonScript`: Path to the Python script to execute.
- `parameters`: Command-line arguments to pass to the script.
- `workingDirectory`: Working directory for script execution (defaults to Python directory).
- `onOutput`: Callback for standard output.
- `onError`: Callback for error output.

**Returns:** `0` on success, otherwise an error code.

**Throws:**
- `ArgumentException`: If `pythonScript` is null or empty.
- `FileNotFoundException`: If the script file doesn't exist.

#### RemovePythonEnvironment

```csharp
public int RemovePythonEnvironment(Action<string> onOutput, Action<string> onError)
```

Completely removes the Python environment from disk.

**Returns:** `0` on success, `-1` on failure.

## Requirements

- .NET 8.0 or later
- Internet connection (for initial Python download)
- Disk space for Python distribution (~50MB) and packages

## Platform Support

- ✅ Windows (x64)
- ✅ Linux (with appropriate Python embed URL)
- ✅ macOS (with appropriate Python embed URL)

**Note:** The default `PythonEmbedUrl` points to a Windows AMD64 distribution. For other platforms, set the `PythonEmbedUrl` property to an appropriate Python embeddable distribution for your target platform.

## How It Works

1. **Download**: On first run, the embedded Python distribution is downloaded from the official Python FTP server.
2. **Extract**: The distribution is extracted to the specified directory.
3. **Bootstrap**: `get-pip.py` is downloaded and executed to install pip.
4. **Virtual Environment**: A virtual environment (`venv`) is created using `virtualenv`.
5. **Isolation**: All subsequent operations (package installation, script execution) occur within this isolated environment.

## Use Cases

- 📊 **Data Processing**: Run Python data analysis scripts from .NET applications
- 🤖 **ML Integration**: Execute machine learning models built with Python frameworks
- 🔧 **Scripting**: Leverage Python libraries for specific tasks within .NET apps
- 🎨 **Content Generation**: Use Python tools (like edge-tts, image processing) in .NET workflows
- 🧪 **Testing**: Run Python-based test scripts or validation tools

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgments

- Python Software Foundation for the embedded Python distributions
- The .NET community for feedback and contributions

## Support

If you encounter any issues or have questions:
- 🐛 [Report a bug](https://github.com/xqiu/DotNetPythonEmbed/issues)
- 💡 [Request a feature](https://github.com/xqiu/DotNetPythonEmbed/issues)
- 📖 [View documentation](https://github.com/xqiu/DotNetPythonEmbed)

---

Made with ❤️ by the DotNetPythonEmbed Contributors
