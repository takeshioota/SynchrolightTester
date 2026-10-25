using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Configuration;
using SynchrolightTester.Wpf.Services;

namespace SynchrolightTester.Wpf;

public partial class MainWindow : Window
{
    private readonly SynchrolightApiClient _api;
    private readonly IConfiguration _config;
    private readonly ConnectionState _conn;
    private readonly ApiProcessManager _apiMgr;

    /// <summary>アセンブリバージョン（csproj の &lt;Version&gt; 由来）を "1.0.0" 形式で返す。</summary>
    private static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "1.0.0";

    public MainWindow(NavigationService nav, SynchrolightApiClient api, IConfiguration config, ConnectionState conn, ApiProcessManager apiMgr)
    {
        InitializeComponent();
        _api = api;
        _config = config;
        _conn = conn;
        _apiMgr = apiMgr;

        VersionText.Text = $"Ver.：{AppVersion}";

        nav.Frame = MainFrame;
        nav.ToMenu();

        // 起動時に SynchrolightAPI の疎通を確認。未起動なら案内し、起動済みならポート取得する。
        Loaded += async (_, _) =>
        {
            if (await EnsureApiUpAsync(showDialog: true))
                await ScanAsync();
        };
    }

    /// <summary>
    /// 「API起動」ボタン。SynchrolightAPI を起動（起動済みなら流用）し、成功したらポート取得する。
    /// 自動起動はしない運用のため、ユーザーが明示的に起動する入口。
    /// </summary>
    private async void OnStartApi(object sender, RoutedEventArgs e)
    {
        BtnStartApi.IsEnabled = false;
        SetStatus(false, "SynchrolightAPI 起動中…");
        try
        {
            var (ok, msg) = await _apiMgr.StartApiAsync(ownProcess: false);
            SetStatus(ok, msg);
            if (ok)
                await ScanAsync();
        }
        catch (Exception ex)
        {
            SetStatus(false, $"API起動エラー: {ex.Message}");
        }
        finally
        {
            BtnStartApi.IsEnabled = true;
        }
    }

    /// <summary>
    /// SynchrolightAPI(:5100) が応答するか確認する。応答しなければ状態表示を更新し、
    /// showDialog=true のときは「API起動ボタンで起動してください」と案内する。
    /// 本テスターは API を自動起動しないため、先に API を起動しておく必要がある。
    /// </summary>
    private async Task<bool> EnsureApiUpAsync(bool showDialog)
    {
        try
        {
            await _api.PingAsync();
            return true;
        }
        catch (SynchrolightApiException)
        {
            SetStatus(false, "SynchrolightAPI 未起動：「API起動」を押してください");
            if (showDialog)
            {
                MessageBox.Show(
                    $"SynchrolightAPI（{_api.BaseUrl}）が応答しません。\n\n" +
                    "上部バー左の「API起動」ボタンで起動するか、先に SynchrolightAPI を起動してから操作してください。",
                    "SynchrolightAPI 未起動", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }
    }

    private async void OnScan(object sender, RoutedEventArgs e)
    {
        if (await EnsureApiUpAsync(showDialog: true))
            await ScanAsync();
    }

    private async Task ScanAsync()
    {
        try
        {
            var ports = await _api.ScanPortsAsync();
            var selected = PortsCombo.SelectedItem as string;
            PortsCombo.ItemsSource = ports;
            if (selected is not null && ports.Contains(selected))
                PortsCombo.SelectedItem = selected;
            else if (ports.Length > 0)
                PortsCombo.SelectedIndex = 0;

            SetStatus(false, ports.Length > 0 ? $"{ports.Length}ポート検出" : "ポートが見つかりません");
        }
        catch (SynchrolightApiException ex)
        {
            SetStatus(false, $"取得失敗: {ex.Message}");
        }
    }

    private async void OnConnect(object sender, RoutedEventArgs e)
    {
        // 2.4G送信機がCH344のどのCOMポートに繋がっているかは機体により不定のため、
        // 検出した全ポートへ一括接続する（参照プロジェクト SynchronizedLights と同方式）。
        // 単一ポート選択だと送信機が別ポートにある場合に全く反応しない問題を避ける。
        // 既接続ポートの再オープンは Access denied になるが他ポートは接続され、接続可否は
        // 実際の接続数で判定するため二度押しでも未接続状態に固まらない。
        if (!await EnsureApiUpAsync(showDialog: true)) return;

        BtnConnect.IsEnabled = false;
        try
        {
            var ports = await _api.ScanPortsAsync();
            if (ports.Length == 0)
            {
                _conn.IsReady = false;
                SetStatus(false, "接続可能なCOMポートがありません");
                return;
            }

            await _api.ConnectAsync(ports);
            int channel = _config.GetValue("Transmitter:Channel", 4);
            int power = _config.GetValue("Transmitter:Power", 0);
            var init = await _api.InitTransmitterAsync(channel, power);

            int connected = await _api.ConnectedPortCountAsync();
            bool ok = connected > 0 && init.Success;
            _conn.IsReady = ok;
            SetStatus(ok, ok ? $"接続OK（{connected}ポート一括）" : "接続できませんでした");
        }
        catch (SynchrolightApiException ex)
        {
            _conn.IsReady = false;
            SetStatus(false, $"接続失敗: {ex.Message}");
        }
        finally
        {
            BtnConnect.IsEnabled = true;
        }
    }

    private void SetStatus(bool connected, string text)
    {
        ConnStatus.Text = text;
        ConnDot.Fill = connected ? Brushes.LimeGreen : new SolidColorBrush(Color.FromRgb(0x9A, 0xA5, 0xB1));
    }
}
