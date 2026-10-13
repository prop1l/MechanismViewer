using MechanismViewer.Catalog;
using System.Windows.Media;

namespace MechanismViewer.Search;

/// <summary>Карточка результата поиска в списке.</summary>
public sealed class SearchResultItem(SearchResult result)
{
    private static readonly Brush Good = Frozen(Color.FromRgb(108, 192, 112));
    private static readonly Brush Fair = Frozen(Color.FromRgb(224, 176, 80));
    private static readonly Brush Poor = Frozen(Color.FromRgb(214, 96, 90));
    private static readonly Brush Neutral = Frozen(Color.FromRgb(170, 180, 195));

    public MechanismDefinition Mechanism { get; } = result.Mechanism;
    public string Name => Mechanism.Name;

    public string Subtitle =>
        $"{Mechanism.Category} · {MechanismSearch.MotionName(Mechanism.Motion.Input)} → " +
        $"{MechanismSearch.MotionName(Mechanism.Motion.Output)} · {MechanismSearch.DriverName(Mechanism.Driver.Type)}";

    public string ScoreText => $"{result.Score:0}%";
    public Brush ScoreBrush => result.Score >= 80 ? Good : result.Score >= 50 ? Fair : Poor;

    public IReadOnlyList<NoteItem> Notes { get; } = result.Notes.Select(n => new NoteItem(n)).ToList();

    public sealed class NoteItem(SearchNote note)
    {
        public string Text => note.Text;
        public string Symbol => note.Kind switch { NoteKind.Match => "✓", NoteKind.Warning => "!", _ => "•" };
        public Brush Brush => note.Kind switch { NoteKind.Match => Good, NoteKind.Warning => Fair, _ => Neutral };
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>Строка «не подошёл» с причиной.</summary>
public sealed record RejectedItem(string Name, string Reason);
