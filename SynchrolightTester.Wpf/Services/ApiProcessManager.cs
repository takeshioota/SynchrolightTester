using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SynchrolightTester.Wpf.Models;

namespace SynchrolightTester.Wpf.Services;

/// <summary>
/// テスター起動時に SynchrolightAPI を自動起動するホステッドサービス。
/// - 既に :5100 が応答する場合は起動しない（手動起動分を流用）。
/// - 自分で起動したプロセスのみ、テスター終了時に停止する。
/// </summary>
public sealed class ApiProcessManager(
    IOptions<SynchrolightApiOptions> options,
    IHostEnvironment env,
    HttpClient http,
    ILogger<ApiProcessManager> logger) : IHostedService
{
    private readonly SynchrolightApiOptions _opt = options.Value;
    private Process? _process;
    private bool _startedByUs;

    public Task StartAsync(CancellationToken ct)
    {
        // 既定では自動起動しない（画面の「API起動」ボタン、または手動で先に API を起動する運用）。
        // SynchrolightApi:AutoStart:Enabled=true のときのみ、従来どおり起動時に自動起動する。
        if (!_opt.AutoStart.Enabled)
        {
            logger.LogInformation("SynchrolightAPI 自動起動は無効です。画面の「API起動」ボタン、または手動で SynchrolightAPI を起動してください。");
            return Task.CompletedTask;
        }

        // テスター本体の起動を妨げないようバックグラウンドで起動する（自動起動分は終了時に停止）。
        _ = Task.Run(() => StartApiAsync(ownProcess: true, CancellationToken.None));
        return Task.CompletedTask;
    }

    /// <summary>
    /// SynchrolightAPI を起動して応答可能になるまで待つ（画面の「API起動」ボタン／自動起動の共通処理）。
    /// 既に :5100 が応答していれば流用する。
    /// </summary>
    /// <param name="ownProcess">
    /// true=テスターが所有し終了時に停止する（自動起動）。false=独立プロセスとして残す（ボタン起動。
    /// テスターを閉じても API は動き続け、次回起動時に流用できる）。
    /// </param>
    /// <returns>(起動/到達できたか, 画面表示用メッセージ)</returns>
    public async Task<(bool Ok, string Message)> StartApiAsync(bool ownProcess, CancellationToken ct = default)
    {
        if (await IsReachableAsync(ct))
        {
            logger.LogInformation("SynchrolightAPI は既に起動しています（{Url}）。起動をスキップします。", _opt.BaseUrl);
            return (true, $"SynchrolightAPI は既に起動しています（{_opt.BaseUrl}）。");
        }

        try
        {
            _process = StartProcess();
        }
        catch (Exception ex)
        {
            logger.LogError("SynchrolightAPI の起動に失敗しました: {Msg}", ex.Message);
            return (false, $"SynchrolightAPI の起動に失敗しました: {ex.Message}");
        }

        _startedByUs = ownProcess;

        var ok = await WaitUntilReadyAsync(_opt.AutoStart.StartupTimeoutSeconds, ct);
        return ok
            ? (true, $"SynchrolightAPI を起動しました（{_opt.BaseUrl}）。")
            : (false, "SynchrolightAPI の起動を確認できませんでした（タイムアウト）。コンソール/ログを確認してください。");
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
        // 1) 明示 ExePath（相対パスは ContentRoot=実行フォルダ 基準で解決。配布ZIPの ..\API\ 構成に対応）
        if (!string.IsNullOrWhiteSpace(a.ExePath))
        {
            var exePath = Path.IsPathRooted(a.ExePath)
                ? a.ExePath
                : Path.GetFullPath(Path.Combine(env.ContentRootPath, a.ExePath));
            if (File.Exists(exePath))
                return (exePath, "", Path.GetDirectoryName(exePath)!, "exe");
        }

        var projectPath = ResolveProjectPath(a.ProjectPath)
            ?? throw new DirectoryNotFoundException(
                $"SynchrolightAPI.Api プロジェクトが見つかりません（ProjectPath='{a.ProjectPath}'）。");

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
        return ("dotnet", $"run --project \"{projectPath}\" --no-launch-profile", projectPath, "dotnet run");
    }

    /// <summary>
    /// ProjectPath を解決する。
    /// (1) 絶対パスで存在 → それ / (2) ContentRoot 基準で存在 → それ /
    /// (3) ContentRoot から上位ディレクトリを辿り、相対パス末尾（例 SynchrolightAPI\SynchrolightAPI.Api）が見つかればそれ。
    /// WPF は bin 配下が ContentRoot になるため、(3) でソースツリー上の API プロジェクトを探索する。
    /// </summary>
    private string? ResolveProjectPath(string configured)
    {
        if (Path.IsPathRooted(configured) && Directory.Exists(configured))
            return configured;

        var rel = Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));
        if (Directory.Exists(rel))
            return rel;

        var tail = configured.TrimStart('.', '\\', '/');
        for (var dir = new DirectoryInfo(env.ContentRootPath); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, tail);
            if (Directory.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private async Task<bool> WaitUntilReadyAsync(int timeoutSeconds, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(5, timeoutSeconds));
        while (DateTime.UtcNow < deadline)
        {
            if (_process is { HasExited: true })
            {
                logger.LogError("SynchrolightAPI プロセスが起動直後に終了しました。ログを確認してください。");
                return false;
            }
            if (await IsReachableAsync(ct))
            {
                logger.LogInformation("SynchrolightAPI の起動を確認しました（{Url}）。", _opt.BaseUrl);
                return true;
            }
            await Task.Delay(1000, ct);
        }
        logger.LogWarning("SynchrolightAPI の起動確認がタイムアウトしました（{Sec}秒）。", timeoutSeconds);
        return false;
    }

    private async Task<bool> IsReachableAsync(CancellationToken ct)
    {
        try
        {
            // 共有 HttpClient（タイムアウト長め）を使うため、到達性チェックは2秒で打ち切る。
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var res = await http.GetAsync(
                new Uri(new Uri(_opt.BaseUrl.TrimEnd('/') + "/"), "api/transport/status"), cts.Token);
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
