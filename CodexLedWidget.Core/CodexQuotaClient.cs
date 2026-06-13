using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace CodexLedWidget.Core;

public sealed class CodexQuotaClient
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(12);

    public async Task<QuotaSnapshot> GetQuotaAsync(CancellationToken cancellationToken = default)
    {
        string response = await RequestRateLimitsAsync(cancellationToken).ConfigureAwait(false);
        return QuotaSnapshotParser.ParseRateLimitsResponse(response);
    }

    private static async Task<string> RequestRateLimitsAsync(CancellationToken cancellationToken)
    {
        CodexCommand command = CodexCommandResolver.Resolve();
        using Process process = StartCodexProcess(command);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultTimeout);

        StringBuilder stderr = new();
        Task stderrTask = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                string? line = await process.StandardError.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    stderr.AppendLine(line);
                }
            }
        }, CancellationToken.None);

        try
        {
            await SendAsync(process, 1, "initialize", new
            {
                clientInfo = new
                {
                    name = "codex-led-widget-native",
                    title = "Codex LED Widget",
                    version = "0.1.0"
                },
                capabilities = (object?)null
            }, timeout.Token).ConfigureAwait(false);
            await ReadResultAsync(process, 1, timeout.Token).ConfigureAwait(false);

            await SendAsync(process, 2, "account/rateLimits/read", null, timeout.Token).ConfigureAwait(false);
            string result = await ReadResultAsync(process, 2, timeout.Token).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("读取 Codex 额度超时。");
        }
        catch (Exception ex) when (stderr.Length > 0)
        {
            throw new InvalidOperationException(stderr.ToString().Trim(), ex);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            try
            {
                await stderrTask.ConfigureAwait(false);
            }
            catch
            {
                // The process may be killed while stderr is being drained.
            }
        }
    }

    private static Process StartCodexProcess(CodexCommand command)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = command.FileName,
            Arguments = command.Arguments,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Codex CLI。");
    }

    private static Task SendAsync(Process process, int id, string method, object? parameters, CancellationToken cancellationToken)
    {
        object payload = parameters is null
            ? new { id, method }
            : new { id, method, @params = parameters };
        string line = JsonSerializer.Serialize(payload);
        return process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
    }

    private static async Task<string> ReadResultAsync(Process process, int id, CancellationToken cancellationToken)
    {
        while (!process.StandardOutput.EndOfStream)
        {
            string? line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out JsonElement idElement) || idElement.GetInt32() != id)
            {
                continue;
            }

            if (root.TryGetProperty("error", out JsonElement errorElement))
            {
                string message = errorElement.TryGetProperty("message", out JsonElement messageElement)
                    ? messageElement.GetString() ?? errorElement.ToString()
                    : errorElement.ToString();
                throw new InvalidOperationException(message);
            }

            if (root.TryGetProperty("result", out JsonElement resultElement))
            {
                return resultElement.GetRawText();
            }
        }

        throw new InvalidOperationException("Codex app-server 没有返回额度结果。");
    }
}

public sealed record CodexCommand(string FileName, string Arguments);

public static class CodexCommandResolver
{
    public static CodexCommand Resolve()
    {
        string[] candidates = BuildCandidates();
        string? executable = candidates.FirstOrDefault(File.Exists);

        if (executable is null)
        {
            throw new FileNotFoundException("未找到 Codex CLI。请确认 codex 已安装并能在终端运行。");
        }

        string extension = Path.GetExtension(executable);
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            return new CodexCommand("cmd.exe", $"/d /s /c \"\"{executable}\" app-server --listen stdio://\"");
        }

        return new CodexCommand(executable, "app-server --listen stdio://");
    }

    private static string[] BuildCandidates()
    {
        List<string> candidates = [];
        string? explicitPath = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            candidates.Add(explicitPath);
        }

        string? localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, "OpenAI", "Codex", "bin", "codex.exe"));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string? home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                candidates.Add(Path.Combine(home, ".local", "bin", "codex"));
                candidates.Add(Path.Combine(home, ".codex", "packages", "standalone", "current", "bin", "codex"));
            }

            candidates.Add("/opt/homebrew/bin/codex");
            candidates.Add("/usr/local/bin/codex");
        }

        candidates.AddRange(FindOnPath());
        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .OrderByDescending(path => Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            .ThenBy(path => path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> FindOnPath()
    {
        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            yield break;
        }

        string[] names = ["codex.exe", "codex.cmd", "codex"];
        foreach (string directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string name in names)
            {
                yield return Path.Combine(directory, name);
            }
        }
    }
}
