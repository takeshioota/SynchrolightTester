using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SynchrolightTester.Models;

namespace SynchrolightTester.Services;

/// <summary>
/// SynchrolightAPI(REST, 既定 http://localhost:5100) を呼び出す薄いクライアント。
/// ブラウザからのCORSを避けるため、通信はすべてテスターのサーバ側(本クラス)で行う。
/// </summary>
public class SynchrolightApiClient(
    HttpClient http,
    IOptions<SynchrolightApiOptions> options,
    ILogger<SynchrolightApiClient> logger)
{
    private readonly SynchrolightApiOptions _opt = options.Value;

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

    /// <summary>AC: ブロック制御（ブロックIDエリア=progNo の値=blockNo の端末を点灯）。</summary>
    public Task<ApiResult> BlockAsync(int progNo, int blockNo, Rgb color, CancellationToken ct = default)
        => PostAsync("api/light/block", new { progNo, blockNo, color = C(color) }, ct);

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
