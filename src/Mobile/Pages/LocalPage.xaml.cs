using SunsetMP.ViewModels;

namespace SunsetMP.Pages;

public partial class LocalPage : ContentPage
{
    readonly LocalLibraryViewModel _vm;

    public LocalPage(LocalLibraryViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        Tree.ViewModel = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.EnsureLoadedAsync();
    }
}
