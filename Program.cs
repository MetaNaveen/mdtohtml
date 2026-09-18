using System.Diagnostics;
using System.Text.RegularExpressions;

string inputDir = Directory.GetCurrentDirectory();
string outputDir = Directory.GetCurrentDirectory();

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-i":
        case "--input":
            if (i + 1 < args.Length) inputDir = args[++i];
            break;
        case "-o":
        case "--output":
            if (i + 1 < args.Length) outputDir = args[++i];
            break;
        case "-h":
        case "--help":
            PrintUsage();
            return 0;
    }
}

inputDir = Path.GetFullPath(inputDir);
outputDir = Path.GetFullPath(outputDir);

if (!Directory.Exists(inputDir))
{
    Console.Error.WriteLine($"Error: input directory not found: {inputDir}");
    return 1;
}

Directory.CreateDirectory(outputDir);

var mdFiles = Directory.GetFiles(inputDir, "*.md", SearchOption.AllDirectories);
if (mdFiles.Length == 0)
{
    Console.WriteLine($"No .md files found in {inputDir}");
    return 0;
}

// Verify the 'marked' CLI is available before doing any work.
if (!IsMarkedAvailable(out string markedVersion))
{
    Console.Error.WriteLine("Error: the 'marked' CLI is not installed or not on PATH.");
    Console.Error.WriteLine("Install it globally with npm:");
    Console.Error.WriteLine();
    Console.Error.WriteLine("    npm install -g marked");
    Console.Error.WriteLine();
    Console.Error.WriteLine("(Node.js must also be installed: https://nodejs.org)");
    return 1;
}

Console.WriteLine($"Using marked v{markedVersion}");

// Matches href/src attributes and markdown links ending with .md (optionally with #anchor or ?query).
var linkRegex = new Regex(
    @"(?<prefix>(?:href|src)\s*=\s*[""']|\]\()(?<path>[^""'()\s#?]+)\.md(?<suffix>(?:[#?][^""'()\s]*)?)",
    RegexOptions.IgnoreCase);

int converted = 0;
foreach (var mdFile in mdFiles)
{
    string relative = Path.GetRelativePath(inputDir, mdFile);
    string htmlRelative = Path.ChangeExtension(relative, ".html");
    string htmlPath = Path.Combine(outputDir, htmlRelative);
    Directory.CreateDirectory(Path.GetDirectoryName(htmlPath)!);

    if (!RunMarked(mdFile, htmlPath, out string error))
    {
        Console.Error.WriteLine($"Failed to convert {relative}: {error}");
        continue;
    }

    // Rewrite .md hyperlinks to .html in the generated file.
    string html = File.ReadAllText(htmlPath);
    string updated = linkRegex.Replace(html, m =>
        $"{m.Groups["prefix"].Value}{m.Groups["path"].Value}.html{m.Groups["suffix"].Value}");
    if (!ReferenceEquals(updated, html) && updated != html)
        File.WriteAllText(htmlPath, updated);

    converted++;
    Console.WriteLine($"Converted: {relative} -> {htmlRelative}");
}

Console.WriteLine($"Done. {converted}/{mdFiles.Length} file(s) converted to {outputDir}");
return 0;

static bool RunMarked(string inputFile, string outputFile, out string error)
{
    error = string.Empty;

    // Preferred path: invoke the marked CLI script directly with node. This avoids the npm .exe
    // shim, which misbehaves when spawned with redirected streams from install paths with spaces.
    string? node = ResolveExecutable("node");
    string? markedScript = FindMarkedScript();

    try
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (node is not null && markedScript is not null)
        {
            psi.FileName = node;
            psi.ArgumentList.Add(markedScript);
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(inputFile);
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(outputFile);
        }
        else
        {
            string? marked = ResolveExecutable("marked");
            if (marked is null)
            {
                error = "Could not find 'marked' on PATH. Install it with: npm install -g marked";
                return false;
            }

            if (OperatingSystem.IsWindows())
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = $"/s /c \"\"{marked}\" -i \"{inputFile}\" -o \"{outputFile}\"\"";
            }
            else
            {
                psi.FileName = marked;
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(inputFile);
                psi.ArgumentList.Add("-o");
                psi.ArgumentList.Add(outputFile);
            }
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            error = "Could not start marked process.";
            return false;
        }

        string stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            error = string.IsNullOrWhiteSpace(stdErr) ? $"marked exited with code {process.ExitCode}" : stdErr.Trim();
            return false;
        }

        return true;
    }
    catch (Exception ex)
    {
        error = ex.Message;
        return false;
    }
}

// Checks that the 'marked' CLI can be invoked and returns its version.
static bool IsMarkedAvailable(out string version)
{
    version = string.Empty;

    string? node = ResolveExecutable("node");
    string? markedScript = FindMarkedScript();

    try
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (node is not null && markedScript is not null)
        {
            psi.FileName = node;
            psi.ArgumentList.Add(markedScript);
            psi.ArgumentList.Add("--version");
        }
        else
        {
            string? marked = ResolveExecutable("marked");
            if (marked is null)
                return false;

            if (OperatingSystem.IsWindows())
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = $"/s /c \"\"{marked}\" --version\"";
            }
            else
            {
                psi.FileName = marked;
                psi.ArgumentList.Add("--version");
            }
        }

        using var process = Process.Start(psi);
        if (process is null)
            return false;

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            return false;

        version = output.Trim();
        return true;
    }
    catch
    {
        return false;
    }
}

// Locates marked's CLI script (bin/marked.js) so it can be run directly with node.
static string? FindMarkedScript()
{
    // 1. If 'marked' on PATH already resolves to a .js file, use it.
    string? resolved = ResolveExecutable("marked");
    if (resolved is not null && resolved.EndsWith(".js", StringComparison.OrdinalIgnoreCase) && File.Exists(resolved))
        return resolved;

    // 2. node_modules sitting next to the marked shim (typical npm-on-Windows layout).
    if (resolved is not null)
    {
        string? shimDir = Path.GetDirectoryName(resolved);
        if (shimDir is not null)
        {
            string candidate = Path.Combine(shimDir, "node_modules", "marked", "bin", "marked.js");
            if (File.Exists(candidate)) return candidate;
        }
    }

    // 3. Ask npm for the global modules root.
    string? globalRoot = QueryNpmGlobalRoot();
    if (globalRoot is not null)
    {
        string candidate = Path.Combine(globalRoot, "marked", "bin", "marked.js");
        if (File.Exists(candidate)) return candidate;
    }

    return null;
}

static string? QueryNpmGlobalRoot()
{
    try
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            psi.FileName = "cmd.exe";
            psi.Arguments = "/s /c \"npm root -g\"";
        }
        else
        {
            psi.FileName = "npm";
            psi.ArgumentList.Add("root");
            psi.ArgumentList.Add("-g");
        }

        using var process = Process.Start(psi);
        if (process is null) return null;

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0) return null;

        string dir = output.Trim();
        return Directory.Exists(dir) ? dir : null;
    }
    catch
    {
        return null;
    }
}

// Resolves an executable's full path by scanning PATH and PATHEXT (handles paths containing spaces).
static string? ResolveExecutable(string name)
{
    var extensions = OperatingSystem.IsWindows()
        ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
        : new[] { string.Empty };

    foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                 .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
        string dirTrimmed = dir.Trim().Trim('"');
        if (dirTrimmed.Length == 0) continue;

        foreach (var ext in extensions)
        {
            string candidate = Path.Combine(dirTrimmed, name + ext);
            if (File.Exists(candidate))
                return candidate;
        }
    }

    return null;
}

static void PrintUsage()
{
    Console.WriteLine("mdtohtml - convert Markdown files to HTML using the 'marked' CLI.");
    Console.WriteLine();
    Console.WriteLine("Usage: mdtohtml.exe -i <input-dir> -o <output-dir>");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -i, --input   Input directory containing .md files (default: current directory).");
    Console.WriteLine("  -o, --output  Output directory for generated .html files (default: current directory).");
    Console.WriteLine("  -h, --help    Show this help message.");
}
