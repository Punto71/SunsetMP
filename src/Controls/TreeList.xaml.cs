using SunsetMP.Models;
using SunsetMP.ViewModels;

namespace SunsetMP.Controls;

public partial class TreeList : ContentView
{
    TreeViewModelBase? _vm;

    public TreeList() => InitializeComponent();

    public TreeViewModelBase? ViewModel
    {
        get => _vm;
        set
        {
            if (_vm != null) _vm.VisibleReplaced -= OnVisibleReplaced;
            _vm = value;
            if (_vm != null) _vm.VisibleReplaced += OnVisibleReplaced;
            List.ItemsSource = _vm?.Visible;
        }
    }

    void OnVisibleReplaced() => List.ItemsSource = _vm?.Visible;

    async void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if (_vm != null && (sender as BindableObject)?.BindingContext is TreeNode n)
            await _vm.NodeTappedAsync(n);
    }

    async void OnMenuClicked(object? sender, EventArgs e)
    {
        if (_vm != null && (sender as BindableObject)?.BindingContext is TreeNode n)
            await _vm.MenuAsync(n);
    }
}
