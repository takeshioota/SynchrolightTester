namespace SynchrolightTester.Wpf.Services;

/// <summary>
/// 「COMポート接続＋送信機初期化」が完了しているかを画面間で共有するフラグ。
/// MainWindow の「接続＋初期化」が成功したら <see cref="IsReady"/>=true、失敗/未接続なら false。
/// 行ID／ブロックID 画面はコマンド実行前にこれを確認する。
/// </summary>
public sealed class ConnectionState
{
    /// <summary>COMポート接続＋送信機初期化が完了しているか。</summary>
    public bool IsReady { get; set; }
}
