using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TZJBox.Sorting;

namespace TZJBox.App.ViewModels;

public sealed partial class StrategyChoice : ObservableObject
{
    public StrategyChoice(SortStrategy strategy, string title, string description)
    {
        Strategy = strategy;
        Title = title;
        Description = description;
        SelectCommand = new RelayCommand(() => Select?.Invoke());
    }

    public SortStrategy Strategy { get; }

    public string Title { get; }

    public string Description { get; }

    public IRelayCommand SelectCommand { get; }

    public Action? Select { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    public void SetSelected(bool selected)
    {
        if (IsSelected == selected)
            return;
        IsSelected = selected;
    }
}
