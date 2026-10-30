using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SynchrolightTester.Wpf.Models;
using SynchrolightTester.Wpf.Services;

namespace SynchrolightTester.Wpf.Views;

public partial class BlockIdPage : Page
{
    private readonly DemoRunner _runner;
    private readonly NavigationService _nav;
    private readonly ConnectionState _conn;
    private readonly Button[] _colorButtons;
    private Rgb _color = Rgb.Red;

    public BlockIdPage(DemoRunner runner, NavigationService nav, ConnectionState conn)
    {
        InitializeComponent();
        _runner = runner;
        _nav = nav;
        _conn = conn;

        _colorButtons = [BtnBlue, BtnGreen, BtnRed];
        Highlight(BtnRed);   // 既定は赤

        BuildRainbowLegend();
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

    private void BuildRainbowLegend()
    {
        foreach (var (blockNo, name, color) in RainbowPalette.Colors)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
            item.Children.Add(new Rectangle
            {
                Width = 14,
                Height = 14,
                Fill = new SolidColorBrush(Color.FromRgb((byte)color.R, (byte)color.G, (byte)color.B)),
                Stroke = Brushes.Gray,
                StrokeThickness = 0.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            });
            item.Children.Add(new TextBlock { Text = $"{blockNo}:{name}", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            RainbowLegend.Children.Add(item);
        }
    }

    // ===== 機能 =====
    private void OnGroup1(object sender, RoutedEventArgs e)
        => Run("グループ1点灯", () => _runner.GroupAsync(1, _color));

    private void OnGroup2(object sender, RoutedEventArgs e)
        => Run("グループ2点灯", () => _runner.GroupAsync(2, _color));

    /// <summary>送出間隔テストの発行間隔(ms)。スライダーの現在値（0〜10, 1ms刻み）。</summary>
    private int GapMs => (int)Math.Round(GapSlider.Value);

    private void OnRainbow(object sender, RoutedEventArgs e)
        => Run("レインボー点灯(固定)", () => _runner.RainbowAsync(GapMs));

    private void OnRainbowShift(object sender, RoutedEventArgs e)
        => Run("レインボー点灯(シフト)", () => _runner.RainbowShiftAsync(null, GapMs));

    private void OnOff(object sender, RoutedEventArgs e)
        => Run("消灯", () => _runner.OffAsync());

    private async void OnBack(object sender, RoutedEventArgs e)
    {
        try { await _runner.OffAsync(); } catch { /* 戻る際は消灯失敗を無視 */ }
        _nav.ToMenu();
    }

    // ===== 共通 =====
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
            // 発行間隔は実行中は変更不可（停止＝消灯で再び有効）。
            GapSlider.IsEnabled = !_runner.IsRunning;
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
