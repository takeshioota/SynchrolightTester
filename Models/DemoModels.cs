namespace SynchrolightTester.Models;

/// <summary>RGB色（SynchrolightAPI の {r,g,b} にそのままシリアライズされる）。</summary>
public readonly record struct Rgb(int R, int G, int B)
{
    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb Red = new(255, 0, 0);
    public static readonly Rgb Green = new(0, 255, 0);
    public static readonly Rgb Blue = new(0, 0, 255);
}

// ===== 接続パネル用リクエスト（ブラウザ → テスター） =====
public record ConnectRequest(string[] Ports, int? Channel, int? Power);

// ===== デモ用リクエスト（ブラウザ → テスター） =====
public record ColorRequest(Rgb Color);
public record SequentialOnRequest(Rgb Color, int? IntervalMs);
public record SequentialBlinkRequest(Rgb Color, int? OnMs, int? GapMs);
public record GroupRequest(int Group, Rgb Color);
public record BlinkRequest(Rgb Color, int? OnMs, int? OffMs);

/// <summary>appsettings.json "Demo" セクション。</summary>
public class DemoOptions
{
    /// <summary>場次(field)。ID書込み済端末は 0x01。</summary>
    public int Field { get; set; } = 1;

    /// <summary>行IDの最大値（端末台数）。</summary>
    public int MaxId { get; set; } = 200;

    /// <summary>② 連続点灯の点灯間隔(ms)。</summary>
    public int SequentialIntervalMs { get; set; } = 500;

    /// <summary>③ 連続点滅: 1端末あたりの点灯時間(ms)。</summary>
    public int BlinkOnMs { get; set; } = 500;

    /// <summary>③ 連続点滅: 次端末へ移るまでの消灯ギャップ(ms)。</summary>
    public int BlinkGapMs { get; set; } = 0;

    /// <summary>点滅ボタン: 全体点灯時間(ms)。</summary>
    public int GlobalBlinkOnMs { get; set; } = 500;

    /// <summary>点滅ボタン: 全体消灯時間(ms)。</summary>
    public int GlobalBlinkOffMs { get; set; } = 500;

    /// <summary>静的シーン(奇数/偶数/グループ/レインボー)の再送間隔(ms)。0=再送しない（端末の保持に任せる）。</summary>
    public int RefreshIntervalMs { get; set; } = 0;

    /// <summary>連続送信時のパケット間ディレイ(ms)。送信機の処理余裕。</summary>
    public int InterPacketDelayMs { get; set; } = 5;

    /// <summary>⑥⑦ グループ制御のブロックIDエリア（プログラム番号）。デモプロ仕様=8。</summary>
    public int GroupProgramNo { get; set; } = 8;

    /// <summary>⑧ レインボーのブロックIDエリア（プログラム番号）。デモプロ仕様=6。</summary>
    public int RainbowProgramNo { get; set; } = 6;
}

/// <summary>SynchrolightAPI 接続設定。</summary>
public class SynchrolightApiOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5100";

    /// <summary>テスター起動時に SynchrolightAPI を自動起動する設定。</summary>
    public AutoStartOptions AutoStart { get; set; } = new();
}

/// <summary>SynchrolightAPI の自動起動設定。</summary>
public class AutoStartOptions
{
    /// <summary>自動起動を有効にするか。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>直接起動する実行ファイル（任意）。未指定なら ProjectPath/bin から自動探索。</summary>
    public string? ExePath { get; set; }

    /// <summary>SynchrolightAPI.Api プロジェクトのパス（相対可。ContentRoot 基準で解決）。exe が無い場合は dotnet run で起動。</summary>
    public string ProjectPath { get; set; } = @"..\SynchrolightAPI\SynchrolightAPI.Api";

    /// <summary>起動完了（応答可能）を待つ最大秒数。</summary>
    public int StartupTimeoutSeconds { get; set; } = 90;
}

/// <summary>⑧ レインボー: ブロックIDエリア6 の値(1〜7)に対応する固定7色（7進法割当）。</summary>
public static class RainbowPalette
{
    // デモプロ/pptx: 001青, 002赤, 003黄, 004緑, 005オレンジ, 006水色, 007紫（= blockNo 1..7）
    public static readonly (int BlockNo, string Name, Rgb Color)[] Colors =
    [
        (1, "青",     new Rgb(0, 0, 255)),
        (2, "赤",     new Rgb(255, 0, 0)),
        (3, "黄",     new Rgb(255, 255, 0)),
        (4, "緑",     new Rgb(0, 255, 0)),
        (5, "オレンジ", new Rgb(255, 165, 0)),
        (6, "水色",   new Rgb(0, 255, 255)),
        (7, "紫",     new Rgb(128, 0, 128)),
    ];
}
