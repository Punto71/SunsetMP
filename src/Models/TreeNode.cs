namespace SunsetMP.Models;

public class TreeNode : BaseViewModel
{
    public string Name { get; init; } = "";
    public string Subtitle { get; set; } = "";
    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);
    /// <summary>Текст, по которому ищем (локальное дерево)</summary>
    public string SearchText { get; set; } = "";
    public int Depth { get; init; }
    public bool IsFolder { get; init; }
    public Track? Track { get; init; }

    public List<TreeNode> Children { get; } = new();
    public bool ChildrenLoaded { get; set; }

    /// <summary>Ленивая загрузка потомков (Jellyfin)</summary>
    public Func<TreeNode, Task<List<TreeNode>>>? ChildLoader { get; set; }
    /// <summary>Все треки папки/трека (для «Воспроизвести» и «Добавить в плейлист»)</summary>
    public Func<Task<List<Track>>>? TracksProvider { get; set; }

    bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (Set(ref _isExpanded, value)) OnPropertyChanged(nameof(Icon)); }
    }

    bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { if (Set(ref _isBusy, value)) OnPropertyChanged(nameof(Icon)); }
    }

    public Thickness Indent => new(Depth * 20, 0, 0, 0);
    public string Icon => IsBusy ? "⏳" : !IsFolder ? "🎵" : IsExpanded ? "📂" : "📁";

    public List<Track> CollectLoadedTracks()
    {
        var result = new List<Track>();
        foreach (var c in Children)
        {
            if (c.IsFolder) result.AddRange(c.CollectLoadedTracks());
            else if (c.Track != null) result.Add(c.Track);
        }
        return result;
    }
}
