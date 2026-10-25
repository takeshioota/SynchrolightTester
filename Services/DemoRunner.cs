using Microsoft.Extensions.Options;
using SynchrolightTester.Models;

namespace SynchrolightTester.Services;

/// <summary>
/// ①〜⑧ + 点滅 + 全消灯 のデモ動作を実行するサービス。
/// 同時に走るデモは常に1つ。新しいデモ開始時に前のデモを自動停止する。
/// SynchrolightAPI の hold は「最後のパケットのみ連続送信」のため、
/// 複数ターゲットの静的シーン(奇数/偶数/グループ/レインボー)は本サービスが
/// 順次送信・（必要なら）定期再送してシーンを成立させる。
/// </summary>
public sealed class DemoRunner(
    SynchrolightApiClient api,
    IOptions<DemoOptions> options,
    ILogger<DemoRunner> logger) : IAsyncDisposable
{
    private readonly DemoOptions _opt = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _task;

    public string CurrentDemo { get; private set; } = "idle";

    /// <summary>デモが実行中（バックグラウンドタスクが生存）か。</summary>
    public bool IsRunning => _task is { IsCompleted: false };

    // ========================= 公開デモ =========================

    /// <summary>② 連続点灯: ID001 から順次点灯（点灯済みは継続点灯）。</summary>
    public Task SequentialOnAsync(Rgb color, int? intervalMs)
    {
        int interval = intervalMs ?? _opt.SequentialIntervalMs;
        return StartAsync("連続点灯", async ct =>
        {
            await ClearAsync(ct);
            // AA(開始行=1, 行数=N) を N=1..Max で送る＝累積点灯。最終的に 1..Max を1パケットで保持。
            for (int n = 1; n <= _opt.MaxId; n++)
            {
                ct.ThrowIfCancellationRequested();
                await api.RowsAsync(_opt.Field, 1, n, color, ct);
                await Task.Delay(interval, ct);
            }
            logger.LogInformation("連続点灯 完了: 1..{Max}（保持中）", _opt.MaxId);
            // 全点灯状態を保持（停止/他デモまで「実行中」を維持）。
            await Task.Delay(Timeout.Infinite, ct);
        });
    }

    /// <summary>③ 連続点滅: ID001 から1端末ずつ点滅し、消灯したら次へ移動（ループ）。</summary>
    public Task SequentialBlinkAsync(Rgb color, int? onMs, int? gapMs)
    {
        int on = onMs ?? _opt.BlinkOnMs;
        int gap = gapMs ?? _opt.BlinkGapMs;
        return StartAsync("連続点滅", async ct =>
        {
            await ClearAsync(ct);
            while (!ct.IsCancellationRequested)
            {
                for (int id = 1; id <= _opt.MaxId; id++)
                {
                    ct.ThrowIfCancellationRequested();
                    await api.RowsAsync(_opt.Field, id, 1, color, ct);   // 点灯
                    await Task.Delay(on, ct);
                    await api.RowsAsync(_opt.Field, id, 1, Rgb.Black, ct); // 消灯して次へ
                    if (gap > 0) await Task.Delay(gap, ct);
                }
            }
        });
    }

    /// <summary>④ 奇数点灯: 行IDが奇数の端末のみ点灯。</summary>
    public Task OddAsync(Rgb color)
        => StartSceneAsync("奇数点灯", color, EnumerateParity(odd: true));

    /// <summary>⑤ 偶数点灯: 行IDが偶数の端末のみ点灯。</summary>
    public Task EvenAsync(Rgb color)
        => StartSceneAsync("偶数点灯", color, EnumerateParity(odd: false));

    /// <summary>⑥⑦ グループ点灯: ブロックIDエリア(GroupProgramNo=8) の値=group の端末を点灯。</summary>
    public Task GroupAsync(int group, Rgb color)
    {
        return StartAsync($"グループ{group}点灯", async ct =>
        {
            await RunSceneAsync(ct, async c =>
            {
                await ClearAsync(c);
                await api.BlockAsync(_opt.GroupProgramNo, group, color, c);
            });
        });
    }

    /// <summary>⑧ レインボー点灯: ブロックIDエリア(RainbowProgramNo=6) の値1〜7に固定7色を割当てて点灯。</summary>
    public Task RainbowAsync()
    {
        return StartAsync("レインボー点灯", async ct =>
        {
            await RunSceneAsync(ct, async c =>
            {
                await ClearAsync(c);
                foreach (var (blockNo, _, color) in RainbowPalette.Colors)
                {
                    c.ThrowIfCancellationRequested();
                    await api.BlockAsync(_opt.RainbowProgramNo, blockNo, color, c);
                    if (_opt.InterPacketDelayMs > 0) await Task.Delay(_opt.InterPacketDelayMs, c);
                }
            });
        });
    }

    /// <summary>点滅: 選択色で全体を点滅（点灯/消灯の繰り返し）。</summary>
    public Task GlobalBlinkAsync(Rgb color, int? onMs, int? offMs)
    {
        int on = onMs ?? _opt.GlobalBlinkOnMs;
        int off = offMs ?? _opt.GlobalBlinkOffMs;
        return StartAsync("点滅", async ct =>
        {
            while (!ct.IsCancellationRequested)
            {
                await api.GlobalAsync(_opt.Field, color, ct);
                await Task.Delay(on, ct);
                await api.OffAsync(ct);
                await Task.Delay(off, ct);
            }
        });
    }

    /// <summary>全消灯: 実行中デモを停止し全消灯。</summary>
    public async Task OffAsync()
    {
        await StopAsync();
        await api.OffAsync();
        CurrentDemo = "idle";
    }

    // ========================= 内部 =========================

    private IEnumerable<int> EnumerateParity(bool odd)
    {
        int start = odd ? 1 : 2;
        for (int id = start; id <= _opt.MaxId; id += 2)
            yield return id;
    }

    /// <summary>行IDの集合を点灯する静的シーン。必要なら定期再送。</summary>
    private Task StartSceneAsync(string name, Rgb color, IEnumerable<int> ids)
    {
        var idList = ids.ToArray();
        return StartAsync(name, async ct =>
        {
            await RunSceneAsync(ct, async c =>
            {
                await ClearAsync(c);
                foreach (var id in idList)
                {
                    c.ThrowIfCancellationRequested();
                    await api.RowsAsync(_opt.Field, id, 1, color, c);
                    if (_opt.InterPacketDelayMs > 0) await Task.Delay(_opt.InterPacketDelayMs, c);
                }
            });
        });
    }

    /// <summary>シーンを1回送信し、RefreshIntervalMs&gt;0 なら定期再送、0 ならキャンセルまで保持。</summary>
    private async Task RunSceneAsync(CancellationToken ct, Func<CancellationToken, Task> sendScene)
    {
        await sendScene(ct);
        if (_opt.RefreshIntervalMs > 0)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_opt.RefreshIntervalMs, ct);
                await sendScene(ct);
            }
        }
        else
        {
            // 端末は最後の色を保持するため再送不要。キャンセルされるまでシーンを「実行中」として保持。
            await Task.Delay(Timeout.Infinite, ct);
        }
    }

    /// <summary>全体を一旦消灯（反対側/前シーンを確実に消す）。</summary>
    private Task ClearAsync(CancellationToken ct) => api.GlobalAsync(0x00, Rgb.Black, ct);

    private async Task StartAsync(string name, Func<CancellationToken, Task> body)
    {
        // 先に到達性を確認（SynchrolightAPI 未起動なら例外を呼び出し元へ返し、実行中デモは止めない）。
        await api.PingAsync();

        await _gate.WaitAsync();
        try
        {
            await StopInternalAsync();
            var cts = new CancellationTokenSource();
            _cts = cts;
            CurrentDemo = name;
            logger.LogInformation("デモ開始: {Name}", name);
            _task = Task.Run(async () =>
            {
                try { await body(cts.Token); }
                catch (OperationCanceledException) { }
                catch (SynchrolightApiException ex) { logger.LogError("デモ '{Name}' 失敗: {Msg}", name, ex.Message); }
                catch (Exception ex) { logger.LogError(ex, "デモ '{Name}' 異常終了", name); }
            }, CancellationToken.None);
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopInternalAsync();
            CurrentDemo = "idle";
        }
        finally { _gate.Release(); }
    }

    private async Task StopInternalAsync()
    {
        var cts = _cts;
        var task = _task;
        _cts = null;
        _task = null;
        if (cts == null) return;

        try { cts.Cancel(); } catch { /* ignore */ }
        if (task != null)
        {
            try { await task; } catch { /* already logged */ }
        }
        cts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _gate.Dispose();
    }
}
