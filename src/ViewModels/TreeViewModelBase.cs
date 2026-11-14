using System.Collections.ObjectModel;
using SunsetMP.Models;
using SunsetMP.Services;

namespace SunsetMP.ViewModels;

public abstract class TreeViewModelBase : BaseViewModel
{
    protected readonly PlaybackService Player;
    readonly List<TreeNode> _roots = new();
    CancellationTokenSource? _searchCts;

    protected TreeViewModelBase(PlaybackService player) => Player = player;

    /// <summary>Плоский список видимых узлов (дерево с учётом раскрытых папок или результаты поиска)</summary>
    public ObservableCollection<TreeNode> Visible { get; private set; } = new();

    /// <summary>Коллекция Visible заменена целиком — TreeList должен перепривязать ItemsSource</summary>
    public event Action? VisibleReplaced;

    /// <summary>Корневые узлы настоящего дерева (без учёта поиска)</summary>
    protected IReadOnlyList<TreeNode> Roots => _roots;

    string _status = "";
    public string Status
    {
        get => _status;
        private set { if (Set(ref _status, value)) OnPropertyChanged(nameof(HasStatus)); }
    }
    public bool HasStatus => !string.IsNullOrEmpty(_status);

    bool _isBusy;
    public bool IsBusy { get => _isBusy; protected set => Set(ref _isBusy, value); }

    bool _noResults;
    /// <summary>По запросу ничего не найдено</summary>
    public bool NoResults { get => _noResults; protected set => Set(ref _noResults, value); }

    string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value ?? "")) OnSearchTextChanged(); }
    }

    /// <summary>Сейчас введён непустой запрос</summary>
    public bool IsSearching => _searchText.Trim().Length > 0;

    protected async void ShowStatus(string text)
    {
        Status = text;
        await Task.Delay(3000);
        if (Status == text) Status = "";
    }

    // ---------- Дерево ----------
    protected void SetRoots(IEnumerable<TreeNode> roots)
    {
        ResetSearch();
        _roots.Clear();
        _roots.AddRange(roots);
        ShowRoots();
    }

    /// <summary>Показать настоящее дерево (раскрытые папки раскрыты)</summary>
    protected void ShowRoots()
    {
        var list = new List<TreeNode>();
        void Add(TreeNode n)
        {
            list.Add(n);
            if (n.IsFolder && n.IsExpanded)
                foreach (var c in n.Children) Add(c);
        }
        foreach (var r in _roots) Add(r);
        ReplaceVisible(list);
    }

    /// <summary>Показать готовый список (результаты поиска)</summary>
    protected void ShowFlat(List<TreeNode> nodes) => ReplaceVisible(nodes);

    void ReplaceVisible(List<TreeNode> nodes)
    {
        Visible = new ObservableCollection<TreeNode>(nodes);
        OnPropertyChanged(nameof(Visible));
        VisibleReplaced?.Invoke();
    }

    // ---------- Поиск ----------
    /// <summary>Выполнить поиск. Пустой запрос = вернуть обычное дерево.</summary>
    protected abstract Task OnSearchAsync(string query, CancellationToken ct);

    /// <summary>Сбросить запрос без запуска поиска (при загрузке нового дерева / отключении)</summary>
    protected void ResetSearch()
    {
        _searchCts?.Cancel();
        NoResults = false;
        if (_searchText.Length > 0)
        {
            _searchText = "";
            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(IsSearching));
        }
    }

    async void OnSearchTextChanged()
    {
        OnPropertyChanged(nameof(IsSearching));
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        var ct = cts.Token;
        try
        {
            var query = _searchText.Trim();
            if (query.Length > 0) await Task.Delay(300, ct);   // дождаться, пока допечатают
            await OnSearchAsync(query, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowStatus("Ошибка поиска: " + ex.Message); }
    }

    // ---------- Действия ----------
    public Task NodeTappedAsync(TreeNode n) => n.IsFolder ? ToggleAsync(n) : MenuAsync(n);

    public async Task ToggleAsync(TreeNode n)
    {
        if (!n.IsFolder || n.IsBusy) return;
        var idx = Visible.IndexOf(n);
        if (idx < 0) return;

        if (n.IsExpanded)
        {
            n.IsExpanded = false;
            while (idx + 1 < Visible.Count && Visible[idx + 1].Depth > n.Depth)
                Visible.RemoveAt(idx + 1);
            return;
        }

        if (!n.ChildrenLoaded && n.ChildLoader != null)
        {
            n.IsBusy = true;
            try
            {
                n.Children.AddRange(await n.ChildLoader(n));
                n.ChildrenLoaded = true;
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Ошибка", ex.Message, "OK");
                return;
            }
            finally { n.IsBusy = false; }
        }

        idx = Visible.IndexOf(n);
        if (idx < 0) return;
        n.IsExpanded = true;
        foreach (var d in VisibleDescendants(n)) Visible.Insert(++idx, d);
    }

    static IEnumerable<TreeNode> VisibleDescendants(TreeNode n)
    {
        foreach (var c in n.Children)
        {
            yield return c;
            if (c.IsExpanded)
                foreach (var d in VisibleDescendants(c)) yield return d;
        }
    }

    /// <summary>Контекстное меню папки/трека</summary>
    public async Task MenuAsync(TreeNode n)
    {
        if (n.IsBusy) return;
        const string play = "Воспроизвести";
        const string add = "Добавить в плейлист";

        var action = await Shell.Current.DisplayActionSheet(n.Name, "Отмена", null, play, add);
        if (action != play && action != add) return;

        List<Track> tracks;
        n.IsBusy = true;
        try { tracks = n.TracksProvider == null ? new List<Track>() : await n.TracksProvider(); }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Ошибка", ex.Message, "OK");
            return;
        }
        finally { n.IsBusy = false; }

        if (tracks.Count == 0) { ShowStatus("Треков не найдено"); return; }

        if (action == play)
        {
            Player.Replace(tracks);
            await Shell.Current.GoToAsync("//player");
        }
        else
        {
            Player.Add(tracks);
            ShowStatus($"Добавлено треков: {tracks.Count}");
        }
    }
}
