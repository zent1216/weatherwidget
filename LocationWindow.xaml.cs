using System.Windows;
using System.Windows.Input;

namespace WeatherWidget;

public partial class LocationWindow : Window
{
    public Place? Selected { get; private set; }

    public LocationWindow(string current)
    {
        InitializeComponent();
        CurrentText.Text = $"현재 지역: {current}";
        Loaded += (_, _) => QueryBox.Focus();
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        var q = QueryBox.Text.Trim();
        if (q == "") return;
        SearchButton.IsEnabled = false;
        ResultList.ItemsSource = null;
        StatusText.Text = "검색 중…";
        StatusText.Visibility = Visibility.Visible;
        try
        {
            var results = await WeatherService.SearchAsync(q);
            ResultList.ItemsSource = results;
            if (results.Count > 0)
            {
                StatusText.Visibility = Visibility.Collapsed;
                ResultList.SelectedIndex = 0;
            }
            else StatusText.Text = "검색 결과가 없습니다. 다른 이름으로 검색해 보세요.";
        }
        catch
        {
            StatusText.Text = "검색 실패. 인터넷 연결을 확인하세요.";
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Ok_Click(sender, e);

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ResultList.SelectedItem is not Place p) return;
        Selected = p;
        DialogResult = true;
    }
}
