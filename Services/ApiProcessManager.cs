using System.Diagnostics;
using Microsoft.Extensions.Options;
using SynchrolightTester.Models;

namespace SynchrolightTester.Services;

/// <summary>
/// テスター起動時に SynchrolightAPI を自動起動するホステッドサービス。
/// - 既に :5100 が応答する場合は起動しない（手動起動分を流用）。
/// - 自分で起動したプロセスのみ、テスター終了時に停止する。
/// </summary>
public sealed class ApiProcessManager(
    IOptions<SynchrolightApiOptions> options,
    IHostEnvironment env,
    IHttpClientFactory httpFactory,
    ILogger<ApiProcessManager> logger) : IHostedService
{
    private readonly SynchrolightApiOptions _opt = options.Value;
    private Process? _process;
    private bool _startedByUs;

    public async Task StartAsync(CancellationToken ct)
    {
        var a = _opt.AutoStart;
        if (!a.Enabled)
        {
            logger.LogInformation("SynchrolightAPI 自動起動は無効です（SynchrolightApi:AutoStart:Enabled=false）。");
            return;
        }

        if (await IsReachableAsync(ct))
        {
            logger.LogInformation("SynchrolightAPI は既に起動済みです（{Url}）。自動起動はスキップします。", _opt.BaseUrl);
            return;
        }

        Process? proc;
        try
        {
            proc = StartProcess();
        }
        catch (Exception ex)
        {
            logger.LogError("SynchrolightAPI の自動起動に失敗しました: {Msg}。手動で起動してください。", ex.Message);
            return;
        }

        if (proc == null) return;

        _process = proc;
        _startedByUs = true;

        // 起動完了待ちはバックグラウンドで行い、テスター本体の起動は妨げない。
        _ = Task.Run(() => WaitUntilReadyAsync(a.StartupTimeoutSeconds), CancellationToken.None);
    }

    public Task StopAsync(CancellationToken ct)
    {
        if (_startedByUs && _process is { HasExited: false })
        {
            try
            {
                logger.LogInformation("SynchrolightAPI を停止します（自動起動分）。");
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                logger.LogWarning("SynchrolightAPI の停止に失敗: {Msg}", ex.Message);
            }
        }
        _process?.Dispose();
        _process = null;
        return Task.CompletedTask;
    }

    // ===== 内部 =====

    private Process StartProcess()
    {
        var a = _opt.AutoStart;
        var (fileName, arguments, workDir, mode) = ResolveLaunch(a);

        logger.LogInformation("SynchrolightAPI を自動起動します（{Mode}）: {File} {Args}", mode, fileName, arguments);

        // 出力はリダイレクトせず親コンソールを継承させる（SynchrolightAPI 側のログを正しい文字コードで表示）。
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Exited += (_, _) => logger.LogInformation("SynchrolightAPI プロセスが終了しました（ExitCode={Code}）。", SafeExitCode(proc));

        proc.Start();
        return proc;
    }

    /// <summary>exe 直接起動を優先し、無ければ dotnet run にフォールバック。</summary>
    private (string fileName, string arguments, string workDir, string mode) ResolveLaunch(AutoStartOptions a)
    {
        var projectPath = Path.IsPathRooted(a.ProjectPath)
            ? a.ProjectPath
            : Path.GetFullPath(Path.Combine(env.ContentRootPath, a.ProjectPath));

        // 1) 明示 ExePath
        if (!string.IsNullOrWhiteSpace(a.ExePath) && File.Exists(a.ExePath))
            return (a.ExePath, "", Path.GetDirectoryName(a.ExePath)!, "exe");

        // 2) bin から最新の SynchrolightAPI.Api.exe を探索
        var binDir = Path.Combine(projectPath, "bin");
        if (Directory.Exists(binDir))
        {
            var exe = Directory.EnumerateFiles(binDir, "SynchrolightAPI.Api.exe", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (exe != null)
                return (exe.FullName, "", exe.DirectoryName!, "exe");
        }

        // 3) dotnet run（ソースからビルドして起動。初回は時間がかかる）
        if (!Directory.Exists(projectPath))
            throw new DirectoryNotFoundException($"SynchrolightAPI.Api プロジェクトが見つかりません: {projectPath}");

        return ("dotnet", $"run --project \"{projectPath}\" --no-launch-profile", projectPath, "dotnet run");
    }

    private async Task WaitUntilReadyAsync(int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(5, timeoutSeconds));
        while (DateTime.UtcNow < deadline)
        {
            if (_process is { HasExited: true })
            {
                logger.LogError("SynchrolightAPI プロセスが起動直後に終了しました。ログを確認してください。");
                return;
            }
            if (await IsReachableAsync(CancellationToken.None))
            {
                logger.LogInformation("SynchrolightAPI の起動を確認しました（{Url}）。", _opt.BaseUrl);
                return;
            }
            await Task.Delay(1000);
        }
        logger.LogWarning("SynchrolightAPI の起動確認がタイムアウトしました（{Sec}秒）。", timeoutSeconds);
    }

    private async Task<bool> IsReachableAsync(CancellationToken ct)
    {
        try
        {
            using var client = httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(2);
            var res = await client.GetAsync(
                new Uri(new Uri(_opt.BaseUrl.TrimEnd('/') + "/"), "api/transport/status"), ct);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; } catch { return -1; }
    }
}
