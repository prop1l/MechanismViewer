using MechanismViewer.Catalog;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MechanismViewer.Search;

/// <summary>
/// Карточка механизма в списке: миниатюра, название, движение с главным параметром и привод.
/// Оценка и пояснения показываются, только когда задан запрос по параметрам.
/// </summary>
public sealed class SearchResultItem(SearchResult result, bool showScore) : INotifyPropertyChanged
{
    private static readonly Brush Good = Frozen(Color.FromRgb(108, 192, 112));
    private static readonly Brush Fair = Frozen(Color.FromRgb(224, 176, 80));
    private static readonly Brush Poor = Frozen(Color.FromRgb(214, 96, 90));
    private static readonly Brush Neutral = Frozen(Color.FromRgb(170, 180, 195));
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private ImageSource? _thumbnail;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MechanismDefinition Mechanism { get; } = result.Mechanism;
    public string Name => Mechanism.Name;
    public string Category => Mechanism.Category;
    public string Description => Mechanism.Description;

    /// <summary>«вращение → линейное · ход 30 мм · мотор».</summary>
    public string Subtitle =>
        string.Join(" · ", new[] { $"{Motion(Mechanism.Motion.Input)} → {Motion(Mechanism.Motion.Output)}", KeyFigure(Mechanism.Specs),
            MechanismSearch.DriverName(Mechanism.Driver.Type) }.Where(s => s.Length > 0));

    /// <summary>Отрисованная миниатюра механизма; приходит позже, пока карточка уже показана.</summary>
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }

    public string ScoreText => $"{result.Score:0}%";
    public Brush ScoreBrush => result.Score >= 80 ? Good : result.Score >= 50 ? Fair : Poor;
    public Visibility ScoreVisibility => showScore ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<NoteItem> Notes { get; } = showScore ? result.Notes.Select(n => new NoteItem(n)).ToList() : [];
    public Visibility NotesVisibility => Notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Подходит ли карточка под быстрый фильтр: каждое слово есть в названии, категории, подписи или описании.
    /// Слово сравнивается без окончания, поэтому «рейка» находит и «рейкой», «шестерня» — «шестерней».
    /// </summary>
    public bool Matches(string filter)
    {
        string text = $"{Name} {Category} {Subtitle} {Description}";
        return filter.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => text.Contains(Stem(word), StringComparison.CurrentCultureIgnoreCase));
    }

    private static readonly string[] Endings =
    [
        "ями", "ами", "ого", "его", "ому", "ему", "ыми", "ими", "ой", "ей", "ий", "ый", "ая", "яя", "ое", "ее",
        "ые", "ие", "ом", "ем", "ам", "ям", "ах", "ях", "ов", "ев", "ую", "юю", "а", "я", "о", "е", "ы", "и", "у", "ю", "ь", "й"
    ];

    /// <summary>Слово без падежного окончания; основа не короче трёх букв.</summary>
    private static string Stem(string word)
    {
        foreach (string ending in Endings)
        {
            if (word.Length - ending.Length >= 3 && word.EndsWith(ending, StringComparison.CurrentCultureIgnoreCase))
            {
                return word[..^ending.Length];
            }
        }

        return word;
    }

    private static string Motion(string motion) => motion switch
    {
        "linear" => "линейное",
        "intermittent" => "прерывистое",
        _ => MechanismSearch.MotionName(motion)
    };

    /// <summary>Главный параметр: передаточное число, ход или угол.</summary>
    private static string KeyFigure(SpecsSpec specs)
    {
        if (specs.Ratio is { } ratio and > 0)
        {
            return ratio < 1 ? $"1:{(1 / ratio).ToString("0.#", Ru)}" : $"{ratio.ToString("0.##", Ru)}:1";
        }

        if (specs.StrokeMm is { } stroke)
        {
            return $"ход {stroke.ToString("0.#", Ru)} мм";
        }

        return specs.AngleDeg is { } angle ? $"{angle.ToString("0.#", Ru)}°" : "";
    }

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
