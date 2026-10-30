namespace SynchrolightTester.Wpf.Models;

/// <summary>RGB色（SynchrolightAPI の {r,g,b} にそのままシリアライズされる）。</summary>
public readonly record struct Rgb(int R, int G, int B)
{
    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb Red = new(255, 0, 0);
    public static readonly Rgb Green = new(0, 255, 0);
    public static readonly Rgb Blue = new(0, 0, 255);
}

/// <summary>appsettings.json "Demo" セクション。</summary>
public class DemoOptions
{
    /// <summary>場次(field)。ID書込み済端末は 0x01。</summary>
    public int Field { get; set; } = 1;

    /// <summary>行IDの最大値（端末台数の上限）。</summary>
    public int MaxId { get; set; } = 200;

    /// <summary>連続点灯の点灯間隔(ms)。</summary>
    public int SequentialIntervalMs { get; set; } = 500;

    /// <summary>連続点滅: 1点滅あたりの点灯時間(ms)。</summary>
    public int BlinkOnMs { get; set; } = 500;

    /// <summary>連続点滅: 1点滅あたりの消灯時間(ms)。</summary>
    public int BlinkOffMs { get; set; } = 500;

    /// <summary>連続点滅: 1端末あたりの点滅回数（新仕様=2回）。</summary>
    public int BlinkCount { get; set; } = 2;

    /// <summary>静的シーン(奇数/偶数/グループ/レインボー)の再送間隔(ms)。0=再送しない（端末の保持に任せる）。</summary>
    public int RefreshIntervalMs { get; set; } = 0;

    /// <summary>連続送信時のパケット間ディレイ(ms)。送信機の処理余裕。</summary>
    public int InterPacketDelayMs { get; set; } = 5;

    /// <summary>
    /// 複数対象シーン（レインボー7色・奇数/偶数点灯）のローテーション送信間隔(ms)。
    /// SynchrolightAPI は 1 パケットしか連続保持できない（新規 StartPacketHold が前保持をフラッシュ）ため、
    /// 全対象を本間隔で巡回送信し続け、各端末をセルフモード突入(約1秒)前に再点灯させて全点灯を維持する。
    /// 1周期(対象数×本値)が約1秒を十分下回るよう設定すること（10台スコープなら 7色×40ms≒280ms で安全）。
    /// </summary>
    public int RotateIntervalMs { get; set; } = 40;

    /// <summary>グループ制御のブロックIDエリア（プログラム番号）。デモプロ仕様=8。</summary>
    public int GroupProgramNo { get; set; } = 8;

    /// <summary>
    /// レインボー制御のブロックIDエリア（プログラム番号）。
    /// 最新資料「ブロックID＿レインボー点灯.pptx」(2026-10-17)=エリア7。各端末は area7 の値(1〜7, 7進法)に
    /// 割当てられた色で点灯。AC(block) を blockNo=1..7 で送出する。
    /// ※旧「配置図1005訂.xlsx デモプロ sheet」ではエリア6（レインボー7色用）。端末への書込みエリアと一致必須。
    /// </summary>
    public int RainbowProgramNo { get; set; } = 7;

    /// <summary>レインボー レベル2（シフト）の色シフト間隔(ms)。資料「一定間隔：0.5秒程度」=既定500。</summary>
    public int RainbowShiftMs { get; set; } = 500;

    /// <summary>
    /// レインボー(ブロック)バーストの再送回数。各フレームを back-to-back で複数回送って RF 取りこぼし
    /// （点灯しない端末）を抑える。concat(7色を1書込み)なので再送してもブロック間のずれは増えない。既定5。
    /// </summary>
    public int RainbowRetransmit { get; set; } = 5;

    /// <summary>レインボー(ブロック)バーストのキープアライブ間隔(ms)。既定50。</summary>
    public int RainbowBurstGapMs { get; set; } = 50;

    /// <summary>
    /// レインボー送出レート検証モードの「ACパケット発行間隔(ms)」初期値。0〜10ms をUIスライダーで可変にし、
    /// 送信機(FA/FB)がどこまで詰めて送っても追従できるかを観察する。0=間を空けず連続発行。
    /// </summary>
    public int RainbowInterPacketGapMs { get; set; } = 3;

    /// <summary>
    /// レインボー送出レート検証モードの再送回数。間隔(interGap)を支配変数にするため既定1
    /// （再送を増やすと1パケットの送出時間が延び、発行間隔の違いが見えにくくなる）。
    /// </summary>
    public int RainbowGapTestRetransmit { get; set; } = 1;
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

    /// <summary>
    /// SynchrolightAPI.Api プロジェクトのパス（相対可）。
    /// ContentRoot 基準で解決し、見つからなければ ContentRoot から上位ディレクトリを辿って探索する。
    /// exe が無い場合は dotnet run で起動。
    /// </summary>
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
