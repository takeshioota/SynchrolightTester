using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SynchrolightTester.Wpf.Models;

namespace SynchrolightTester.Wpf.Services;

/// <summary>
/// 各機能（連続点灯／連続点滅／奇数／偶数／グループ／レインボー／全消灯）を実行するサービス。
/// 同時に走るデモは常に1つ。新しいデモ開始時に前のデモを自動停止する（＝機能は排他）。
/// 各機能は実行前に A2黒で一旦全消灯してからシーンを開始し、「消灯」操作まで状態を維持する。
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

    // ========================= 公開デモ（行ID） =========================

    /// <summary>連続点灯: ID001 から順次点灯（点灯済みは継続点灯）。count=対象端末数。</summary>
    public Task SequentialOnAsync(Rgb color, int? count = null, int? intervalMs = null)
    {
        int max = EffectiveCount(count);
        int interval = intervalMs ?? _opt.SequentialIntervalMs;
        return StartAsync("連続点灯", async ct =>
        {
            await ClearAsync(ct);
            // AA(開始行=1, 行数=N) を N=1..max で送る＝累積点灯。最終的に 1..max を1パケットで保持。
            for (int n = 1; n <= max; n++)
            {
                ct.ThrowIfCancellationRequested();
                await api.RowsAsync(_opt.Field, 1, n, color, ct);
                await Task.Delay(interval, ct);
            }
            logger.LogInformation("連続点灯 完了: 1..{Max}（保持中）", max);
            // 全点灯状態を保持（停止/他デモまで「実行中」を維持）。
            await Task.Delay(Timeout.Infinite, ct);
        });
    }

    /// <summary>連続点滅: ID001 から1端末ずつ blinkCount 回点滅し、終えたら次へ移動（消灯までスイープをループ）。</summary>
    public Task SequentialBlinkAsync(Rgb color, int? count = null, int? onMs = null, int? offMs = null, int? blinkCount = null)
    {
        int max = EffectiveCount(count);
        int on = onMs ?? _opt.BlinkOnMs;
        int off = offMs ?? _opt.BlinkOffMs;
        int times = Math.Max(1, blinkCount ?? _opt.BlinkCount);
        return StartAsync("連続点滅", async ct =>
        {
            await ClearAsync(ct);
            while (!ct.IsCancellationRequested)
            {
                for (int id = 1; id <= max; id++)
                {
                    ct.ThrowIfCancellationRequested();
                    for (int t = 0; t < times; t++)
                    {
                        await api.RowsAsync(_opt.Field, id, 1, color, ct);      // 点灯
                        await Task.Delay(on, ct);
                        await api.RowsAsync(_opt.Field, id, 1, Rgb.Black, ct);  // 消灯
                        await Task.Delay(off, ct);
                    }
                }
            }
        });
    }

    /// <summary>奇数点灯: 行IDが奇数の端末のみ点灯（1..count）。</summary>
    public Task OddAsync(Rgb color, int? count = null)
        => StartSceneAsync("奇数点灯", color, EnumerateParity(odd: true, EffectiveCount(count)));

    /// <summary>偶数点灯: 行IDが偶数の端末のみ点灯（1..count）。</summary>
    public Task EvenAsync(Rgb color, int? count = null)
        => StartSceneAsync("偶数点灯", color, EnumerateParity(odd: false, EffectiveCount(count)));

    // ========================= 公開デモ（ブロックID） =========================

    /// <summary>グループ点灯: ブロックIDエリア(GroupProgramNo=8) の値=group の端末を点灯。</summary>
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

    /// <summary>
    /// レインボー点灯（レベル1＝固定）: ブロックIDエリア7（レインボー7色用=7進法）の値(1〜7)ごとに固定色で点灯。
    /// 「ブロックID＿レインボー点灯.pptx」(2026-10-17) 準拠。エリア7の値に対応した発光色(パレット)を AC で送る。
    /// 7色(AC)を SynchrolightAPI 側で「1まとまりのバースト」として連続送信・保持する（POST /api/light/blocks →
    /// StartPacketBurstHold）。1パケットずつ巡回送信する旧方式と異なり、全ブロックの更新が1バースト内
    /// （≒数十ms）に収まるため、ブロック（端末）ごとの点灯タイミングのずれが小さい。「消灯」まで保持。
    /// </summary>
    public Task RainbowAsync(int? interPacketGapMs = null)
    {
        int interGap = Math.Max(0, interPacketGapMs ?? _opt.RainbowInterPacketGapMs);
        return StartAsync("レインボー点灯(固定)", async ct =>
        {
            await ClearAsync(ct);
            // 送出レート検証: 7色ACを連結せず interGap(ms) 間隔で順次発行（送信機の追従観察）。
            // 間隔を支配変数にするため concat=false・retransmit=1 で送る。interGap=0 は連続発行(詰め打ち)。
            await api.BlocksBurstAsync(
                BuildRainbowEntries(0), _opt.RainbowBurstGapMs, _opt.RainbowGapTestRetransmit,
                concat: false, interPacketGapMs: interGap, ct: ct);
            // サーバ側がバースト保持するため、こちらはキャンセル（消灯/他デモ）まで「実行中」を維持。
            await Task.Delay(Timeout.Infinite, ct);
        });
    }

    /// <summary>
    /// レインボー点灯（レベル2＝シフト）: レベル1の発光色を一定間隔(既定0.5秒)で順次シフトする。
    /// シフト毎に、現在のシフト量の7色を SynchrolightAPI 側で「1まとまりのバースト」として再発行・保持する。
    /// 各シフトのブロック間の点灯ずれはサーバ側の1バースト時間（≒数十ms）に収まる。「消灯」まで継続。
    /// ※旧「レインボー点滅(0.5秒 on/off)」は資料(レベル2=順次シフト)と異なるため廃止。
    /// </summary>
    public Task RainbowShiftAsync(int? shiftMs = null, int? interPacketGapMs = null)
    {
        int shiftInterval = Math.Max(1, shiftMs ?? _opt.RainbowShiftMs);
        int interGap = Math.Max(0, interPacketGapMs ?? _opt.RainbowInterPacketGapMs);
        int n = RainbowPalette.Colors.Length;
        return StartAsync("レインボー点灯(シフト)", async ct =>
        {
            await ClearAsync(ct);
            int shift = 0;
            while (!ct.IsCancellationRequested)
            {
                // 現在のシフト量の7色を interGap(ms) 間隔で順次発行（送出レート検証）。シフト間隔ごとに割当を1つ進めて再発行。
                await api.BlocksBurstAsync(
                    BuildRainbowEntries(shift), _opt.RainbowBurstGapMs, _opt.RainbowGapTestRetransmit,
                    concat: false, interPacketGapMs: interGap, ct: ct);
                await Task.Delay(shiftInterval, ct);
                shift = (shift + 1) % n;
            }
        });
    }

    /// <summary>
    /// レインボー7色のブロック指定（エリア7, blockNo=1..7, 色）を作る。
    /// shift=0 で「blockNo=k → パレットk番目の色」(固定)。shift を進めると各ブロックの色が1つずつ巡回する。
    /// </summary>
    private IEnumerable<(int progNo, int blockNo, Rgb color)> BuildRainbowEntries(int shift)
    {
        var palette = RainbowPalette.Colors;
        int n = palette.Length;
        return Enumerable.Range(0, n)
            .Select(i => (_opt.RainbowProgramNo, palette[i].BlockNo, palette[(i + shift) % n].Color))
            .ToArray();
    }

    // ========================= 共通 =========================

    /// <summary>消灯: 実行中デモを停止し全消灯（機能終了）。</summary>
    public async Task OffAsync()
    {
        await StopAsync();
        await api.OffAsync();
        CurrentDemo = "idle";
    }

    // ========================= 内部 =========================

    private int EffectiveCount(int? count) => Math.Clamp(count ?? _opt.MaxId, 1, _opt.MaxId);

    private static IEnumerable<int> EnumerateParity(bool odd, int max)
    {
        int start = odd ? 1 : 2;
        for (int id = start; id <= max; id += 2)
            yield return id;
    }

    /// <summary>
    /// 行IDの集合（奇数/偶数など非連続な複数行）を点灯する静的シーン。
    /// 非連続なので 1 パケットにまとめられず、AA を行ごとに送る＝APIの単一保持では最後の行しか残らない。
    /// 全対象行をローテーション送信し続けて全点灯を維持する。
    /// </summary>
    private Task StartSceneAsync(string name, Rgb color, IEnumerable<int> ids)
    {
        var sends = ids
            .Select(id => (Func<CancellationToken, Task>)(c => api.RowsAsync(_opt.Field, id, 1, color, c)))
            .ToArray();
        return StartAsync(name, ct => RunRotatingSceneAsync(ct, sends));
    }

    /// <summary>
    /// 複数対象シーンを「巡回連続送信」で保持する。各シーン前に A2黒で一旦全消灯し、以後キャンセルまで
    /// 全対象を RotateIntervalMs 間隔で順次送り続ける。SynchrolightAPI が 1 パケットしか連続保持できない
    /// 制約下でも、各端末がセルフモード突入(約1秒)前に再点灯されるため全点灯状態を維持できる。
    /// </summary>
    private async Task RunRotatingSceneAsync(CancellationToken ct, IReadOnlyList<Func<CancellationToken, Task>> sends)
    {
        await ClearAsync(ct);
        if (sends.Count == 0)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return;
        }

        int interval = Math.Max(1, _opt.RotateIntervalMs);
        while (!ct.IsCancellationRequested)
        {
            foreach (var send in sends)
            {
                ct.ThrowIfCancellationRequested();
                await send(ct);
                await Task.Delay(interval, ct);
            }
        }
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
