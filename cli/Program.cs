using System.CommandLine;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

var serverOption = new Option<string>("--server", () => "http://localhost:5000", "Server URL");
var apiKeyOption = new Option<string?>("--api-key", "API key for authenticated servers (or set UIAT_API_KEY env var)");
var projectOption = new Option<string?>("--project", "Override project name");
var branchOption = new Option<string?>("--branch", "Override branch name");

var rootCommand = new RootCommand("UIAutomation Test Result CLI — upload, browse, and manage test sessions");

// --- upload command ---
var uploadCommand = new Command("upload", "Upload test session zip(s) to the server");
var pathArg = new Argument<string>("path", "Path to a .zip file or directory of .zip files");
uploadCommand.AddArgument(pathArg);
uploadCommand.AddOption(serverOption);
uploadCommand.AddOption(apiKeyOption);
uploadCommand.AddOption(projectOption);
uploadCommand.AddOption(branchOption);
uploadCommand.SetHandler(async (string path, string server, string? apiKey, string? project, string? branch) =>
{
    apiKey ??= Environment.GetEnvironmentVariable("UIAT_API_KEY");
    var client = CreateClient(apiKey);
    var files = GetZipFiles(path);

    if (files.Count == 0)
    {
        Console.Error.WriteLine($"No .zip files found at: {path}");
        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine($"Uploading {files.Count} file(s) to {server}...");
    var uploaded = 0;
    foreach (var file in files)
    {
        try
        {
            var url = $"{server.TrimEnd('/')}/api/sessions/upload";
            using var content = new MultipartFormDataContent();
            var fileBytes = await File.ReadAllBytesAsync(file);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            content.Add(fileContent, "file", Path.GetFileName(file));

            var response = await client.PostAsync(url, content);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(body);
                var testName = doc.RootElement.GetProperty("testName").GetString();
                var result = doc.RootElement.GetProperty("result").GetString();
                var id = doc.RootElement.GetProperty("id").GetString();
                Console.WriteLine($"  OK  {Path.GetFileName(file)} -> {testName} ({result}) [{id}]");
                uploaded++;
            }
            else
            {
                Console.Error.WriteLine($"  FAIL  {Path.GetFileName(file)} -> {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  ERROR  {Path.GetFileName(file)} -> {ex.Message}");
        }
    }
    Console.WriteLine($"\nUploaded {uploaded}/{files.Count} session(s).");
}, pathArg, serverOption, apiKeyOption, projectOption, branchOption);

// --- watch command ---
var watchCommand = new Command("watch", "Watch a directory for new .zip files and auto-upload");
var watchDirArg = new Argument<string>("directory", "Directory to watch");
watchCommand.AddArgument(watchDirArg);
watchCommand.AddOption(serverOption);
watchCommand.AddOption(apiKeyOption);
watchCommand.AddOption(projectOption);
watchCommand.SetHandler(async (string directory, string server, string? apiKey, string? project) =>
{
    apiKey ??= Environment.GetEnvironmentVariable("UIAT_API_KEY");
    var client = CreateClient(apiKey);

    if (!Directory.Exists(directory))
    {
        Console.Error.WriteLine($"Directory not found: {directory}");
        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine($"Watching {directory} for new .zip files...");
    Console.WriteLine($"Server: {server}");
    Console.WriteLine("Press Ctrl+C to stop.");

    using var watcher = new FileSystemWatcher(directory, "*.zip");
    watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime;
    watcher.Created += async (sender, e) =>
    {
        // Wait for file to finish writing
        await Task.Delay(1000);
        for (int retry = 0; retry < 10; retry++)
        {
            try
            {
                using var fs = File.Open(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.None);
                break;
            }
            catch
            {
                await Task.Delay(500);
            }
        }

        try
        {
            var url = $"{server.TrimEnd('/')}/api/sessions/upload";
            using var content = new MultipartFormDataContent();
            var fileBytes = await File.ReadAllBytesAsync(e.FullPath);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            content.Add(fileContent, "file", e.Name!);

            var response = await client.PostAsync(url, content);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(body);
                var testName = doc.RootElement.GetProperty("testName").GetString();
                Console.WriteLine($"  [UPLOADED] {e.Name} -> {testName}");
            }
            else
            {
                Console.Error.WriteLine($"  [FAILED] {e.Name} -> {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [ERROR] {e.Name} -> {ex.Message}");
        }
    };

    watcher.EnableRaisingEvents = true;
    await Task.Delay(Timeout.Infinite);
}, watchDirArg, serverOption, apiKeyOption, projectOption);

// --- list command ---
var listCommand = new Command("list", "List recent test sessions");
var limitOption = new Option<int>("--limit", () => 20, "Number of sessions to show");
var resultOption = new Option<string?>("--result", "Filter by result (pass/fail/warn)");
listCommand.AddOption(serverOption);
listCommand.AddOption(limitOption);
listCommand.AddOption(projectOption);
listCommand.AddOption(resultOption);
listCommand.SetHandler(async (string server, int limit, string? project, string? result) =>
{
    var client = CreateClient(null);
    var url = $"{server.TrimEnd('/')}/api/sessions?pageSize={limit}";
    if (!string.IsNullOrEmpty(project)) url += $"&project={Uri.EscapeDataString(project)}";
    if (!string.IsNullOrEmpty(result)) url += $"&result={Uri.EscapeDataString(result)}";

    try
    {
        var response = await client.GetStringAsync(url);
        var doc = JsonDocument.Parse(response);
        var items = doc.RootElement.GetProperty("items");
        var total = doc.RootElement.GetProperty("total").GetInt32();

        Console.WriteLine($"Showing {items.GetArrayLength()} of {total} session(s):\n");
        Console.WriteLine($"{"ID",-34} {"Result",-6} {"Test Name",-40} {"Project",-20} {"Branch",-15} {"Time"}");
        Console.WriteLine(new string('-', 130));

        foreach (var item in items.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString()?[..8] ?? "";
            var res = item.GetProperty("result").GetString() ?? "";
            var name = Truncate(item.GetProperty("testName").GetString() ?? "", 38);
            var proj = Truncate(GetStr(item, "project"), 18);
            var branch = Truncate(GetStr(item, "branch"), 13);
            var time = GetStr(item, "startTime");

            var color = res switch { "pass" => "\x1b[32m", "fail" => "\x1b[31m", "warn" => "\x1b[33m", _ => "" };
            Console.WriteLine($"{id,-34} {color}{res,-6}\x1b[0m {name,-40} {proj,-20} {branch,-15} {time}");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Environment.ExitCode = 1;
    }
}, serverOption, limitOption, projectOption, resultOption);

// --- open command ---
var openCommand = new Command("open", "Open a test session in the browser");
var idArg = new Argument<string>("id", "Session ID (or prefix)");
openCommand.AddArgument(idArg);
openCommand.AddOption(serverOption);
openCommand.SetHandler((string id, string server) =>
{
    var url = $"{server.TrimEnd('/')}/session/{id}";
    Console.WriteLine($"Opening: {url}");
    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch
    {
        Console.WriteLine($"Could not open browser. Visit: {url}");
    }
}, idArg, serverOption);

rootCommand.AddCommand(uploadCommand);
rootCommand.AddCommand(watchCommand);
rootCommand.AddCommand(listCommand);
rootCommand.AddCommand(openCommand);

return await rootCommand.InvokeAsync(args);

// --- Helpers ---

static HttpClient CreateClient(string? apiKey)
{
    var client = new HttpClient();
    client.Timeout = TimeSpan.FromMinutes(5);
    if (!string.IsNullOrEmpty(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    return client;
}

static List<string> GetZipFiles(string path)
{
    if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        return [path];
    if (Directory.Exists(path))
        return Directory.GetFiles(path, "*.zip").OrderBy(f => f).ToList();
    // Try glob pattern
    var dir = Path.GetDirectoryName(path) ?? ".";
    var pattern = Path.GetFileName(path);
    if (Directory.Exists(dir))
        return Directory.GetFiles(dir, pattern).Where(f => f.EndsWith(".zip")).OrderBy(f => f).ToList();
    return [];
}

static string Truncate(string s, int max) =>
    s.Length <= max ? s : s[..(max - 2)] + "..";

static string GetStr(JsonElement el, string prop) =>
    el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
