using SynchrolightTester.Models;
using SynchrolightTester.Services;

var builder = WebApplication.CreateBuilder(args);

// 設定バインド
builder.Services.Configure<SynchrolightApiOptions>(builder.Configuration.GetSection("SynchrolightApi"));
builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection("Demo"));

// SynchrolightAPI クライアント（REST, 既定 http://localhost:5100）
builder.Services.AddHttpClient<SynchrolightApiClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
});

// デモ実行サービス（単一アクティブデモ）
builder.Services.AddSingleton<DemoRunner>();

// SynchrolightAPI 自動起動（テスター起動時に :5100 を立ち上げ、終了時に停止）
builder.Services.AddHostedService<ApiProcessManager>();

var app = builder.Build();

app.UseDefaultFiles();   // / → index.html
app.UseStaticFiles();    // wwwroot

// ===================== 接続パネル API =====================
var conn = app.MapGroup("/api/conn");

conn.MapGet("/scan", (SynchrolightApiClient api, CancellationToken ct)
    => Guarded(async () => Results.Ok(new { ok = true, ports = await api.ScanPortsAsync(ct) })));

conn.MapPost("/connect", (ConnectRequest req, SynchrolightApiClient api, IConfiguration cfg, CancellationToken ct)
    => Guarded(async () =>
    {
        if (req.Ports is null || req.Ports.Length == 0)
            return Results.BadRequest(new { ok = false, message = "接続するCOMポートを選択してください。" });

        var connectRes = await api.ConnectAsync(req.Ports, ct);

        int channel = req.Channel ?? cfg.GetValue("Transmitter:Channel", 4);
        int power = req.Power ?? cfg.GetValue("Transmitter:Power", 0);
        var initRes = await api.InitTransmitterAsync(channel, power, ct);

        return Results.Ok(new
        {
            ok = connectRes.Success && initRes.Success,
            message = $"{connectRes.Message} / {initRes.Message}"
        });
    }));

conn.MapGet("/status", (SynchrolightApiClient api, CancellationToken ct)
    => Guarded(async () => Results.Ok(new { ok = true, status = await api.StatusAsync(ct) })));

// ===================== デモ API =====================
var demo = app.MapGroup("/api/demo");

// ① 色指定はクライアント側の状態管理のみ（このボタンだけでは光らない）。サーバ側エンドポイントは不要。

// ② 連続点灯
demo.MapPost("/sequential-on", (SequentialOnRequest req, DemoRunner runner)
    => Guarded(async () => { await runner.SequentialOnAsync(req.Color, req.IntervalMs); return Ok("連続点灯"); }));

// ③ 連続点滅
demo.MapPost("/sequential-blink", (SequentialBlinkRequest req, DemoRunner runner)
    => Guarded(async () => { await runner.SequentialBlinkAsync(req.Color, req.OnMs, req.GapMs); return Ok("連続点滅"); }));

// ④ 奇数点灯
demo.MapPost("/odd", (ColorRequest req, DemoRunner runner)
    => Guarded(async () => { await runner.OddAsync(req.Color); return Ok("奇数点灯"); }));

// ⑤ 偶数点灯
demo.MapPost("/even", (ColorRequest req, DemoRunner runner)
    => Guarded(async () => { await runner.EvenAsync(req.Color); return Ok("偶数点灯"); }));

// ⑥⑦ グループ点灯
demo.MapPost("/group", (GroupRequest req, DemoRunner runner)
    => Guarded(async () =>
    {
        if (req.Group is not (1 or 2))
            return Results.BadRequest(new { ok = false, message = "group は 1 または 2 を指定してください。" });
        await runner.GroupAsync(req.Group, req.Color);
        return Ok($"グループ{req.Group}点灯");
    }));

// ⑧ レインボー点灯
demo.MapPost("/rainbow", (DemoRunner runner)
    => Guarded(async () => { await runner.RainbowAsync(); return Ok("レインボー点灯"); }));

// 点滅（全体）
demo.MapPost("/blink", (BlinkRequest req, DemoRunner runner)
    => Guarded(async () => { await runner.GlobalBlinkAsync(req.Color, req.OnMs, req.OffMs); return Ok("点滅"); }));

// 全消灯
demo.MapPost("/off", (DemoRunner runner)
    => Guarded(async () => { await runner.OffAsync(); return Ok("全消灯"); }));

// 状態
demo.MapGet("/status", (DemoRunner runner)
    => Results.Ok(new { ok = true, current = runner.CurrentDemo, running = runner.IsRunning }));

app.Run();

// ===================== ヘルパ =====================
static IResult Ok(string name) => Results.Ok(new { ok = true, message = $"{name} を実行しました。", current = name });

static async Task<IResult> Guarded(Func<Task<IResult>> action)
{
    try
    {
        return await action();
    }
    catch (SynchrolightApiException ex)
    {
        // SynchrolightAPI 未起動/未接続など
        return Results.Json(new { ok = false, message = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, message = $"エラー: {ex.Message}" }, statusCode: StatusCodes.Status500InternalServerError);
    }
}
