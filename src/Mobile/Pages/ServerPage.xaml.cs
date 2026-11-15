using SunsetMP.ViewModels;

namespace SunsetMP.Pages;

public partial class ServerPage : ContentPage
{
    readonly ServerViewModel _vm;

    public ServerPage(ServerViewModel vm)
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
