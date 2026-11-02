using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SynchrolightTester.Wpf.Models;
using SynchrolightTester.Wpf.Services;

namespace SynchrolightTester.Wpf.Views;

/// <summary>
/// 行＋列 操作確認（十字）。指定した行ID全体(AA)と列ID全体(A8)を同時に点灯する。
/// API の単一パケット保持制約のため、DemoRunner.CrossAsync が行・列の2パケットを巡回送信して維持する。
/// </summary>
public partial class CrossIdPage : Page
{
    private readonly DemoRunner _runner;
    private readonly NavigationService _nav;
    private readonly ConnectionState _conn;
    private readonly Button[] _colorButtons;
    private Rgb _color = Rgb.Red;

    public CrossIdPage(DemoRunner runner, NavigationService nav, ConnectionState conn)
    {
        InitializeComponent();
        _runner = runner;
        _nav = nav;
        _conn = conn;

        _colorButtons = [BtnBlue, BtnGreen, BtnRed];
        Highlight(BtnRed);   // 既定は赤
    }

    // ===== 色選択 =====
    private void OnColor(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        _color = btn.Name switch
        {
            nameof(BtnBlue) => Rgb.Blue,
            nameof(BtnGreen) => Rgb.Green,
            _ => Rgb.Red,
        };
        Highlight(btn);
    }

    private void Highlight(Button selected)
    {
        foreach (var b in _colorButtons)
            b.BorderBrush = Brushes.Transparent;
        selected.BorderBrush = Brushes.Black;
    }

    // ===== 機能 =====
    private void OnCross(object sender, RoutedEventArgs e)
    {
        int row = Id(RowBox.Text);
        int col = Id(ColBox.Text);
        Run($"十字点灯（行{row}＋列{col}）", () => _runner.CrossAsync(_color, row, col));
    }

    private void OnOff(object sender, RoutedEventArgs e)
        => Run("消灯", () => _runner.OffAsync());

    private async void OnBack(object sender, RoutedEventArgs e)
    {
        try { await _runner.OffAsync(); } catch { /* 戻る際は消灯失敗を無視 */ }
        _nav.ToMenu();
    }

    // ===== 共通 =====
    private static int Id(string text)
        => int.TryParse(text, out var n) ? Math.Clamp(n, 1, 200) : 1;

    private async void Run(string name, Func<Task> action)
    {
        if (!_conn.IsReady)
        {
            RequireConnection();
            return;
        }

        try
        {
            await action();
            AppendLog($"{name} を実行しました。");
            StatusText.Text = _runner.IsRunning ? $"実行中：{_runner.CurrentDemo}" : "待機中（全消灯）";
        }
        catch (SynchrolightApiException ex)
        {
            AppendLog($"[失敗] {name}：{ex.Message}");
        }
        catch (Exception ex)
        {
            AppendLog($"[エラー] {name}：{ex.Message}");
        }
    }

    private void RequireConnection()
    {
        StatusText.Text = "未接続：先に接続してください";
        AppendLog("[未接続] 先に接続する必要があります。上部バーでCOMポートを選択し「接続＋初期化」を実行してください。");
        MessageBox.Show(
            "先に接続する必要があります。\n上部バーでCOMポートを選択し「接続＋初期化」を実行してください。",
            "未接続", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void AppendLog(string message)
    {
        LogList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        if (LogList.Items.Count > 200)
            LogList.Items.RemoveAt(LogList.Items.Count - 1);
    }
}
