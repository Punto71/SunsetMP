using SunsetMP.Services;

namespace SunsetMP.Controls;

/// <summary>Поле поиска в стиле плашек приложения. Text — двусторонняя привязка.</summary>
public partial class SearchBox : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(SearchBox), "",
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((SearchBox)b).OnTextChanged((string?)n ?? ""));

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(SearchBox), "Поиск",
        propertyChanged: (b, _, n) => ((SearchBox)b).Input.Placeholder = (string?)n);

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public SearchBox()
    {
        InitializeComponent();
        SwipeNavigation.Block(this);
        Input.Placeholder = Placeholder;
        Input.TextChanged += (_, e) =>
        {
            var t = e.NewTextValue ?? "";
            if (Text != t) Text = t;
        };
    }

    void OnTextChanged(string text)
    {
        if ((Input.Text ?? "") != text) Input.Text = text;
        ClearBtn.IsVisible = text.Length > 0;
    }

    void OnClearClicked(object? sender, EventArgs e)
    {
        Text = "";
        Input.Unfocus();
    }
}
