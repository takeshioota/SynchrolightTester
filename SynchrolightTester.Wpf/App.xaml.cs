using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SynchrolightTester.Wpf.Models;
using SynchrolightTester.Wpf.Services;
using SynchrolightTester.Wpf.Views;

namespace SynchrolightTester.Wpf;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            // WinExe は bin 配下が実行ディレクトリ。appsettings.json は出力コピーされるためここを ContentRoot にする。
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Services.Configure<SynchrolightApiOptions>(builder.Configuration.GetSection("SynchrolightApi"));
        builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection("Demo"));

        // IHttpClientFactory は使わず、タイムアウト長めの HttpClient を単一共有。
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        builder.Services.AddSingleton<SynchrolightApiClient>();
        builder.Services.AddSingleton<DemoRunner>();
        // ApiProcessManager は「API起動」ボタンからも使うため単一インスタンスを共有（HostedService 兼 注入可能）。
        builder.Services.AddSingleton<ApiProcessManager>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ApiProcessManager>());

        builder.Services.AddSingleton<ConnectionState>();
        builder.Services.AddSingleton<NavigationService>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddTransient<MenuPage>();
        builder.Services.AddTransient<RowIdPage>();
        builder.Services.AddTransient<BlockIdPage>();

        _host = builder.Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            await _host.StartAsync();
            _host.Services.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"起動に失敗しました: {ex.Message}", "SynchrolightTester",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            // DemoRunner は IAsyncDisposable のため同期 Dispose ではなく DisposeAsync で破棄する。
            if (_host is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else
                _host.Dispose();
        }
        base.OnExit(e);
    }
}
