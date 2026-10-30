using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SynchrolightTester.Wpf.Models;

namespace SynchrolightTester.Wpf.Services;

/// <summary>
/// SynchrolightAPI(REST, 既定 http://localhost:5100) を呼び出す薄いクライアント。
/// </summary>
public class SynchrolightApiClient(
    HttpClient http,
    IOptions<SynchrolightApiOptions> options,
    ILogger<SynchrolightApiClient> logger)
{
    private readonly SynchrolightApiOptions _opt = options.Value;

    /// <summary>接続先 SynchrolightAPI のベースURL（未起動時の案内メッセージ等に使用）。</summary>
    public string BaseUrl => _opt.BaseUrl;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private Uri Url(string path) => new(new Uri(_opt.BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

    // ===== Transport =====
    public async Task<string[]> ScanPortsAsync(CancellationToken ct = default)
    {
        var api = await GetAsync("api/transport/scan", ct);
        var ports = api.Data.ValueKind == JsonValueKind.Object && api.Data.TryGetProperty("ports", out var p)
            ? p.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToArray()
            : [];
        return ports;
    }

    public Task<ApiResult> ConnectAsync(string[] portNames, CancellationToken ct = default)
        => PostAsync("api/transport/connect", new { portNames }, ct);

    public async Task<JsonElement> StatusAsync(CancellationToken ct = default)
    {
        var api = await GetAsync("api/transport/status", ct);
        return api.Data;
    }

    /// <summary>
    /// 指定ポートが現在「接続済み」かを状態APIで確認する。
    /// 既に開いているポートへ connect すると排他エラー(Access denied)になるが、
    /// ポート自体は開いているため、接続可否はこの実状態で判定する。判定不能時は false。
    /// </summary>
    public async Task<bool> IsPortConnectedAsync(string port, CancellationToken ct = default)
    {
        try
        {
            var data = await StatusAsync(ct);
            if (data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("ports", out var ports)
                && ports.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in ports.EnumerateArray())
                {
                    var name = p.TryGetProperty("name", out var n) ? n.GetString() : null;
                    bool connected = p.TryGetProperty("isConnected", out var c) && c.ValueKind == JsonValueKind.True;
                    if (connected && string.Equals(name, port, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch (SynchrolightApiException)
        {
            // 状態取得に失敗したら判定不能 → false（呼び出し側で接続を試みる）。
        }
        return false;
    }

    /// <summary>現在接続済みのポート数を状態APIから取得する（取得不能時は0）。</summary>
    public async Task<int> ConnectedPortCountAsync(CancellationToken ct = default)
    {
        try
        {
            var data = await StatusAsync(ct);
            if (data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("connectedPorts", out var c)
                && c.ValueKind == JsonValueKind.Number)
            {
                return c.GetInt32();
            }
        }
        catch (SynchrolightApiException)
        {
            // 取得失敗時は0扱い。
        }
        return 0;
    }

    /// <summary>SynchrolightAPI が到達可能か軽量確認（未起動時は SynchrolightApiException）。</summary>
    public async Task PingAsync(CancellationToken ct = default)
        => await GetAsync("api/transport/status", ct);

    // ===== Transmitter =====
    public Task<ApiResult> InitTransmitterAsync(int channel, int power, CancellationToken ct = default)
        => PostAsync("api/transmitter/init", new { channel, power }, ct);

    // ===== Light =====

    /// <summary>AA: 複数行同色制御（行ID単位の点灯）。field/startRow/rowLen/color。</summary>
    public Task<ApiResult> RowsAsync(int field, int startRow, int rowLen, Rgb color, CancellationToken ct = default)
        => PostAsync("api/light/rows", new { field, startRow, rowLen, color = C(color) }, ct);

    /// <summary>
    /// AC: ブロック制御（ブロックIDエリア=progNo の値=blockNo の端末を点灯）。プロトコル 3.10。
    /// レインボー(エリア6)は blockNo=1..7 を各固定色で送出して「area6 値ごとの固定色」を作る。
    /// </summary>
    public Task<ApiResult> BlockAsync(int progNo, int blockNo, Rgb color, CancellationToken ct = default)
        => PostAsync("api/light/block", new { progNo, blockNo, color = C(color) }, ct);

    /// <summary>
    /// AC 複数ブロックを「バースト保持」する（レインボー7色などを1まとまりで送出・保持）。
    /// 7色を1パケットずつ巡回送信する方式より、全ブロックの更新が1バースト内（≒数十ms）に収まり、
    /// ブロック（端末）ごとの点灯/色替えタイミングのずれを小さくできる。burstGapMs 未指定時はAPI既定。
    /// </summary>
    public Task<ApiResult> BlocksBurstAsync(
        IEnumerable<(int progNo, int blockNo, Rgb color)> entries,
        int? burstGapMs = null,
        int? retransmit = null,
        bool? concat = null,
        int? interPacketGapMs = null,
        CancellationToken ct = default)
        => PostAsync("api/light/blocks",
            new
            {
                entries = entries.Select(e => new { progNo = e.progNo, blockNo = e.blockNo, color = C(e.color) }).ToArray(),
                burstGapMs,
                retransmit,
                concat,
                interPacketGapMs
            }, ct);

    /// <summary>A2: 全体一括色。</summary>
    public Task<ApiResult> GlobalAsync(int field, Rgb color, CancellationToken ct = default)
        => PostAsync("api/light/global", new { color = C(color), field }, ct);

    /// <summary>A2: 全消灯。</summary>
    public Task<ApiResult> OffAsync(CancellationToken ct = default)
        => PostAsync("api/light/off", new { }, ct);

    // ===== 内部 =====
    private static object C(Rgb c) => new { r = c.R, g = c.G, b = c.B };

    private async Task<ApiResult> PostAsync(string path, object body, CancellationToken ct)
    {
        try
        {
            var res = await http.PostAsJsonAsync(Url(path), body, Json, ct);
            var api = await ReadAsync(res, ct);
            return new ApiResult(api.Success, api.Message ?? api.Error ?? "");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not SynchrolightApiException)
        {
            logger.LogWarning(ex, "SynchrolightAPI 呼び出し失敗: {Path}", path);
            throw new SynchrolightApiException($"SynchrolightAPI({_opt.BaseUrl}) 呼び出しに失敗しました: {ex.Message}", ex);
        }
    }

    private async Task<ApiEnvelope> GetAsync(string path, CancellationToken ct)
    {
        try
        {
            var res = await http.GetAsync(Url(path), ct);
            return await ReadAsync(res, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not SynchrolightApiException)
        {
            logger.LogWarning(ex, "SynchrolightAPI 取得失敗: {Path}", path);
            throw new SynchrolightApiException($"SynchrolightAPI({_opt.BaseUrl}) 呼び出しに失敗しました: {ex.Message}", ex);
        }
    }

    private static async Task<ApiEnvelope> ReadAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new SynchrolightApiException($"SynchrolightAPI がエラー応答 ({(int)res.StatusCode}): {text}");

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            bool success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
            string? message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            string? error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            JsonElement data = root.TryGetProperty("data", out var d) ? d.Clone() : default;
            return new ApiEnvelope(success, message, error, data);
        }
        catch (JsonException)
        {
            return new ApiEnvelope(res.IsSuccessStatusCode, text, null, default);
        }
    }

    private readonly record struct ApiEnvelope(bool Success, string? Message, string? Error, JsonElement Data);
}

public readonly record struct ApiResult(bool Success, string Message);

public class SynchrolightApiException : Exception
{
    public SynchrolightApiException(string message, Exception? inner = null) : base(message, inner) { }
}
