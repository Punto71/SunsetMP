namespace SunsetMP.Services;

/// <summary>
/// Свайпы по экрану. Горизонтальный свайп переключает вкладки, кроме областей, которые
/// зарегистрированы здесь: там свайп либо запрещён (полоса перемотки, поле поиска),
/// либо выполняет своё действие (смена трека по области с названием).
/// Сами касания ловит Android-часть (Platforms/Android/SwipeDetector.cs).
/// </summary>
public static class SwipeNavigation
{
    static readonly string[] Routes = { "player", "local", "jellyfin" };
    static readonly List<Zone> _zones = new();

    public sealed class Zone
    {
        public Zone(VisualElement view, Action<int>? onSwipe)
        {
            View = new WeakReference<VisualElement>(view);
            OnSwipe = onSwipe;
        }

        public WeakReference<VisualElement> View { get; }
        /// <summary>null — в этой области свайп вкладок отключён и больше ничего не происходит</summary>
        public Action<int>? OnSwipe { get; }
    }

    public static IReadOnlyList<Zone> Zones => _zones;

    /// <summary>Свайп, начатый в этой области, не переключает вкладки.</summary>
    public static void Block(VisualElement view) => _zones.Add(new Zone(view, null));

    /// <summary>Свайп в этой области вызывает действие: +1 — свайп влево, -1 — свайп вправо.</summary>
    public static void Handle(VisualElement view, Action<int> onSwipe) => _zones.Add(new Zone(view, onSwipe));

    /// <summary>+1 — следующая вкладка (свайп влево), -1 — предыдущая (свайп вправо).</summary>
    public static async void SwitchTab(int direction)
    {
        try
        {
            var item = Shell.Current?.CurrentItem;
            if (item == null) return;

            var index = item.Items.IndexOf(item.CurrentItem);
            var target = index + direction;
            if (index < 0 || target < 0 || target >= Math.Min(item.Items.Count, Routes.Length)) return;

            await Shell.Current!.GoToAsync("//" + Routes[target]);
        }
        catch { /* свайп — не критичная операция */ }
    }
}
