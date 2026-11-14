using SunsetMP.ViewModels;

namespace SunsetMP.Pages;

public partial class JellyfinPage : ContentPage
{
    readonly JellyfinViewModel _vm;

    public JellyfinPage(JellyfinViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        Tree.ViewModel = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.EnsureRestoredAsync();
    }
}
