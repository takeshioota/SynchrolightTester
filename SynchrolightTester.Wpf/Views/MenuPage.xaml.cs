using System.Windows;
using System.Windows.Controls;
using SynchrolightTester.Wpf.Services;

namespace SynchrolightTester.Wpf.Views;

public partial class MenuPage : Page
{
    private readonly NavigationService _nav;

    public MenuPage(NavigationService nav)
    {
        InitializeComponent();
        _nav = nav;
    }

    private void OnRowId(object sender, RoutedEventArgs e) => _nav.ToRowId();
    private void OnBlockId(object sender, RoutedEventArgs e) => _nav.ToBlockId();
}
