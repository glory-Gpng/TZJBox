using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TZJBox.App.Views;
using TZJBox.Sorting;

namespace TZJBox.App.Services;

public sealed class WindowDialogs(Func<Window> owner) : IUserDialogs
{
    public async Task<string?> PickFolderAsync()
    {
        var folders = await owner().StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择图片所在文件夹",
            AllowMultiple = false
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<bool> ConfirmRenameAsync(IReadOnlyList<RenamePlanItem> changes)
    {
        var window = new RenamePreviewWindow(changes);
        return await window.ShowDialog<bool>(owner());
    }

    public async Task AlertAsync(string title, string message)
    {
        var button = new Button
        {
            Content = "知道了",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            IsDefault = true,
            Classes = { "primary" }
        };
        var window = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            MinHeight = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#121417")),
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.Parse("#F4F1EA"))
                    },
                    button
                }
            }
        };
        button.Click += (_, _) => window.Close();
        await window.ShowDialog(owner());
    }
}
