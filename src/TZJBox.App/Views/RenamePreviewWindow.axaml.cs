using Avalonia.Controls;
using Avalonia.Interactivity;
using TZJBox.Sorting;

namespace TZJBox.App.Views;

public partial class RenamePreviewWindow : Window
{
    public RenamePreviewWindow()
    {
        InitializeComponent();
    }

    public RenamePreviewWindow(IReadOnlyList<RenamePlanItem> changes)
        : this()
    {
        TitleText.Text = $"将按当前顺序修改 {changes.Count} 个文件名";
        ChangeList.ItemsSource = changes;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
