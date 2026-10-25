using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SynchrolightTester.Wpf.Views;

namespace SynchrolightTester.Wpf.Services;

/// <summary>
/// MainWindow の Frame を使った画面遷移サービス。
/// 各 Page は DI から都度解決する（遷移ごとに新しい状態で開く）。
/// </summary>
public sealed class NavigationService(IServiceProvider sp)
{
    /// <summary>MainWindow 初期化時にセットされる遷移先 Frame。</summary>
    public Frame? Frame { get; set; }

    public void ToMenu() => Navigate<MenuPage>();
    public void ToRowId() => Navigate<RowIdPage>();
    public void ToBlockId() => Navigate<BlockIdPage>();

    private void Navigate<T>() where T : Page => Frame?.Navigate(sp.GetRequiredService<T>());
}
