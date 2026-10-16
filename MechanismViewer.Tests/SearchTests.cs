using MechanismViewer.Catalog;
using MechanismViewer.Search;

namespace MechanismViewer.Tests;

public class SearchTests
{
    private static readonly IReadOnlyList<MechanismDefinition> FullCatalog = CatalogLoader.LoadAll(CatalogLoader.DefaultRoot);

    // Фиксированный набор, чтобы ожидания не менялись при пополнении каталога.
    private static readonly IReadOnlyList<MechanismDefinition> Catalog = FullCatalog
        .Where(m => m.Id is "spur-pair-36-36" or "crank-slider-15-50" or "lever-rack-m15" or "button-rack-pinion")
        .ToList();

    private static SearchOutcome Run(SearchQuery query) => MechanismSearch.Run(Catalog, query);

    private static SearchOutcome RunFull(SearchQuery query) => MechanismSearch.Run(FullCatalog, query);

    [Fact]
    public void FixtureHasAllFourBaseMechanisms() => Assert.Equal(4, Catalog.Count);

    [Theory]
    [InlineData("oscillation", "four-bar-crank-rocker")]
    [InlineData("intermittent", "geneva-4")]
    public void FullCatalog_FindsByOutputMotion(string output, string expected)
    {
        Assert.Equal([expected], Ids(RunFull(new SearchQuery { Output = output })));
    }

    [Fact]
    public void FullCatalog_SmallRatioFindsWormFirst()
    {
        SearchOutcome outcome = RunFull(new SearchQuery { Input = "rotation", Output = "rotation", Ratio = 0.05 });

        Assert.Equal("worm-1-20", outcome.Results[0].Mechanism.Id);
        Assert.True(outcome.Results[0].Score > 95);
        // Пара 1:1 в 20 раз дальше — далеко за допуском.
        Assert.True(outcome.Results.Single(r => r.Mechanism.Id == "spur-pair-36-36").Score < 55);
    }

    [Fact]
    public void FullCatalog_HighForceFindsScrewAndClamp()
    {
        SearchOutcome outcome = RunFull(new SearchQuery { Output = "linear", OutputForceN = 80 });

        Assert.Equal(["screw-nut-tr8", "toggle-clamp"], Ids(outcome).Order());
    }

    private static string[] Ids(SearchOutcome outcome) => outcome.Results.Select(r => r.Mechanism.Id).ToArray();

    [Fact]
    public void EmptyQuery_ReturnsWholeCatalogWithFullScore()
    {
        SearchOutcome outcome = Run(new SearchQuery());

        Assert.Equal(Catalog.Count, outcome.Results.Count);
        Assert.All(outcome.Results, r => Assert.Equal(100.0, r.Score));
        Assert.Empty(outcome.Rejected);
    }

    [Fact]
    public void MotionType_FiltersCatalog()
    {
        SearchOutcome outcome = Run(new SearchQuery { Input = "rotation", Output = "linear" });

        Assert.Equal(["crank-slider-15-50", "lever-rack-m15"], Ids(outcome).Order());
        Assert.Contains(outcome.Rejected, r => r.Mechanism.Id == "spur-pair-36-36" && r.Reason.Contains("на выходе"));
    }

    [Theory]
    [InlineData(25.0, "lever-rack-m15")]
    [InlineData(30.0, "crank-slider-15-50")]
    public void Stroke_RanksClosestFirst(double strokeMm, string expectedFirst)
    {
        SearchOutcome outcome = Run(new SearchQuery { Input = "rotation", Output = "linear", StrokeMm = strokeMm });

        Assert.Equal(expectedFirst, outcome.Results[0].Mechanism.Id);
        Assert.True(outcome.Results[0].Score > outcome.Results[1].Score);
    }

    [Fact]
    public void FarOffStroke_IsListedWithWarning()
    {
        SearchOutcome outcome = Run(new SearchQuery { Output = "linear", StrokeMm = 100 });

        Assert.NotEmpty(outcome.Results);
        Assert.All(outcome.Results, r =>
        {
            Assert.True(r.Score < 60);
            Assert.Contains(r.Notes, n => n.Kind == NoteKind.Warning && n.Text.Contains("вне допуска"));
        });
    }

    [Fact]
    public void LoadAboveRating_IsRejected()
    {
        SearchOutcome outcome = Run(new SearchQuery { Output = "linear", OutputForceN = 5 });

        // Кривошип рассчитан на 0,4 Н, рычаг — на 30 Н.
        Assert.Equal(["lever-rack-m15"], Ids(outcome));
        Assert.Contains(outcome.Rejected, r => r.Mechanism.Id == "crank-slider-15-50" && r.Reason.Contains("усилие"));
    }

    [Fact]
    public void SmallLoadMargin_IsWarned()
    {
        SearchResult lever = Run(new SearchQuery { Output = "linear", OutputForceN = 28 }).Results.Single();

        Assert.Contains(lever.Notes, n => n.Kind == NoteKind.Warning && n.Text.Contains("малый запас"));
    }

    [Fact]
    public void Envelope_AllowsRotationButRejectsTooLarge()
    {
        // Рычаг 104×94×20 мм помещается в 20×110×100 только повёрнутым.
        SearchOutcome rotated = Run(new SearchQuery { Driver = "manual", MaxEnvelopeMm = [20, 110, 100] });
        SearchResult lever = Assert.Single(rotated.Results);
        Assert.Contains(lever.Notes, n => n.Text.Contains("при повороте"));

        SearchOutcome tooSmall = Run(new SearchQuery { Driver = "manual", MaxEnvelopeMm = [90, 90, 90] });
        Assert.Empty(tooSmall.Results);
        Assert.Contains("габариты", Assert.Single(tooSmall.Rejected, r => r.Mechanism.Id == "lever-rack-m15").Reason);
    }

    [Theory]
    [InlineData("motor", new[] { "crank-slider-15-50", "spur-pair-36-36" })]
    [InlineData("manual", new[] { "lever-rack-m15" })]
    [InlineData("button", new[] { "button-rack-pinion" })]
    public void Driver_FiltersByDriveType(string driver, string[] expected)
    {
        Assert.Equal(expected, Ids(Run(new SearchQuery { Driver = driver })).Order());
    }

    [Fact]
    public void LinearToRotation_FindsButtonWithAngle()
    {
        SearchResult button = Run(new SearchQuery { Input = "linear", Output = "rotation", AngleDeg = 80 }).Results.Single();

        Assert.Equal("button-rack-pinion", button.Mechanism.Id);
        Assert.Contains(button.Notes, n => n.Kind == NoteKind.Match && n.Text.StartsWith("угол поворота"));
        Assert.InRange(button.Score, 90, 100);
    }
}

public class CardFilterTests
{
    private static readonly IReadOnlyList<MechanismDefinition> FullCatalog = CatalogLoader.LoadAll(CatalogLoader.DefaultRoot);

    private static string[] Filter(string text) => FullCatalog
        .Select(m => new SearchResultItem(new SearchResult(m, 100, []), showScore: false))
        .Where(c => c.Matches(text))
        .Select(c => c.Mechanism.Id)
        .Order()
        .ToArray();

    [Theory]
    [InlineData("рейка")]
    [InlineData("рейкой")]
    [InlineData("РЕЙКУ")]
    public void WordForms_FindSameMechanisms(string word)
    {
        Assert.Contains("lever-rack-m15", Filter(word));
        Assert.Contains("gearbox-slider-indicator", Filter(word));
    }

    [Fact]
    public void EveryWordMustMatch()
    {
        Assert.Equal(["cam-flat-follower", "eccentric-follower"], Filter("кулачок мотор"));
        Assert.Empty(Filter("кулачок кнопка"));
    }

    [Fact]
    public void EmptyFilter_ShowsWholeCatalog() => Assert.Equal(FullCatalog.Count, Filter("  ").Length);

    [Fact]
    public void Subtitle_ShowsMotionKeyFigureAndDriver()
    {
        MechanismDefinition worm = FullCatalog.Single(m => m.Id == "worm-1-20");
        var card = new SearchResultItem(new SearchResult(worm, 100, []), showScore: false);

        Assert.Equal("вращение → вращение · 1:20 · мотор", card.Subtitle);
        Assert.Empty(card.Notes);
    }
}

public class ThumbnailTests
{
    [Fact]
    public void Thumbnail_DrawsMechanismOnTransparentBackground()
    {
        int painted = 0, total = 0;
        var thread = new Thread(() =>
        {
            MechanismDefinition m = CatalogLoader.Load(Path.Combine(CatalogLoader.DefaultRoot, "geneva-4", CatalogLoader.FileName));
            System.Windows.Media.Imaging.BitmapSource bitmap = MechanismViewer.Visuals.ThumbnailRenderer.Render(m, 84, 60);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            total = pixels.Length / 4;
            for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] > 0) painted++;
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        // Механизм занимает заметную часть кадра, но не весь кадр (фон прозрачный).
        Assert.InRange(painted, total / 5, total * 9 / 10);
    }
}
