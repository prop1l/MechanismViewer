using System.Text.Json.Serialization;

namespace MechanismViewer.Catalog;

/// <summary>
/// Описание механизма из каталога (<c>Catalog/&lt;id&gt;/mechanism.json</c>).
/// Длины в мм (как в STL), углы в градусах, динамика в СИ.
/// </summary>
public sealed record MechanismDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";

    public required MotionSpec Motion { get; init; }
    public SpecsSpec Specs { get; init; } = new();

    /// <summary>Габариты механизма X × Y × Z, мм.</summary>
    public double[] EnvelopeMm { get; init; } = [];

    public required DriverSpec Driver { get; init; }

    /// <summary>Выходная деталь — к ней приложена нагрузка.</summary>
    public string? Output { get; init; }

    public required IReadOnlyList<PartSpec> Parts { get; init; }
    public IReadOnlyList<CouplingSpec> Couplings { get; init; } = [];
    public IReadOnlyList<FastenerSpec> Fasteners { get; init; } = [];

    /// <summary>Папка, из которой загружено описание (STL ищутся относительно неё).</summary>
    [JsonIgnore]
    public string Directory { get; init; } = "";

    public PartSpec Part(string id) =>
        Parts.FirstOrDefault(p => p.Id == id)
        ?? throw new KeyNotFoundException($"Механизм «{Id}»: нет детали «{id}»");
}

/// <summary>Вид движения на входе и выходе: rotation, linear, oscillation, intermittent.</summary>
public sealed record MotionSpec
{
    public required string Input { get; init; }
    public required string Output { get; init; }
}

/// <summary>Паспортные характеристики, по которым идёт поиск. Отсутствующие — null.</summary>
public sealed record SpecsSpec
{
    public double? Ratio { get; init; }
    public double? MaxTorqueNm { get; init; }
    public double? MaxForceN { get; init; }
    public double? MaxSpeedRadS { get; init; }
    public double? StrokeMm { get; init; }
    public double? AngleDeg { get; init; }
}

/// <summary>
/// Привод: motor (крутится сам), lever (рычаг в пределах range), crank (ручка без ограничений),
/// button (нажатие и возврат пружиной). Величины — в единицах шарнира ведущей детали:
/// градусы и Н·м для revolute, мм и Н для prismatic.
/// </summary>
public sealed record DriverSpec
{
    public required string Type { get; init; }

    /// <summary>Ведущая деталь.</summary>
    public required string Part { get; init; }

    /// <summary>Для мотора: зазор от торца ведущей детали до фланца, мм.</summary>
    public double MotorGapMm { get; init; }

    /// <summary>Для мотора: передний торец ведущей детали, к которому её прижимает ступица на валу, мм.</summary>
    public double HubSeatMm { get; init; } = 10.0;

    /// <summary>
    /// Для мотора: вал кончается на этой высоте внутри ступицы ведущей детали, а ступицы мотора нет, мм.
    /// Нужно, когда над деталью ходят шатуны (кривошип). Нет — вал со ступицей мотора.
    /// </summary>
    public double? ShaftEndMm { get; init; }

    /// <summary>Пределы хода [min, max] от начального положения: ° или мм. Нет — без упоров.</summary>
    public double[]? Range { get; init; }

    /// <summary>Наибольшее усилие руки: Н·м для рычага/ручки, Н для кнопки (с ним кнопка и нажимается).</summary>
    public double? HandForce { get; init; }

    /// <summary>Возвратная пружина на ведущей детали.</summary>
    public SpringSpec? Spring { get; init; }

    /// <summary>Мотор: на упорах range меняет направление (ходовой винт ездит туда-обратно).</summary>
    public bool ReverseAtLimits { get; init; }
}

/// <summary>
/// Пружина тянет ведущую деталь к началу хода: F = −(preload + stiffness·q).
/// stiffness — Н/мм или Н·м/°, preload — Н или Н·м.
/// </summary>
public sealed record SpringSpec
{
    public double Stiffness { get; init; }
    public double Preload { get; init; }
}

public sealed record PartSpec
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>STL детали (путь от папки механизма) — для моделей из CAD. Задаётся либо stl, либо shape.</summary>
    public string? Stl { get; init; }

    /// <summary>Форма из примитивов в системе детали, мм: строится при загрузке, файл не нужен.</summary>
    public IReadOnlyList<ShapeSpec>? Shape { get; init; }

    /// <summary>
    /// Ось, вдоль которой выдавливается shape: z (по умолчанию); x — форма строится в плоскости XY
    /// и переносится (x, y, z) → (z, y, x), как колесо червячной пары с осью X;
    /// y — поворачивается (x, y, z) → (x, z, −y): ось построения смотрит вверх (вертикальный винт).
    /// </summary>
    public string ShapeAxis { get; init; } = "z";

    /// <summary>Цвет в формате #RRGGBB.</summary>
    public string Color { get; init; } = "#A0A0A0";

    public JointSpec Joint { get; init; } = new();

    /// <summary>Плотность материала, кг/м³ (для оценки инерции).</summary>
    public double DensityKgM3 { get; init; } = 1240.0;

    /// <summary>Параметры зубчатого колеса, если деталь — шестерня.</summary>
    public GearSpec? Gear { get; init; }

    /// <summary>Момент инерции относительно оси шарнира, кг·м² (revolute). Для шестерни считается сам.</summary>
    public double? InertiaKgM2 { get; init; }

    /// <summary>Масса, кг (prismatic).</summary>
    public double? MassKg { get; init; }

    /// <summary>Вязкое трение: Н·м·с/рад (revolute) или Н·с/м (prismatic).</summary>
    public double? ViscousFriction { get; init; }

    /// <summary>Сухое трение: Н·м (revolute) или Н (prismatic).</summary>
    public double? CoulombFriction { get; init; }
}

/// <summary>
/// Шарнир детали: revolute (вращение вокруг оси), prismatic (сдвиг вдоль оси), fixed,
/// link — звено-шатун между точками двух других деталей (без своей степени свободы).
/// </summary>
public sealed record JointSpec
{
    public string Type { get; init; } = "fixed";
    public double[] Origin { get; init; } = [0, 0, 0];

    /// <summary>Ось шарнира; для link — нормаль плоскости движения шатуна.</summary>
    public double[] Axis { get; init; } = [0, 0, 1];

    /// <summary>Начальная координата: угол (°) для revolute, смещение (мм) для prismatic.</summary>
    public double Phase { get; init; }

    public LinkSpec? Link { get; init; }
}

/// <summary>
/// Шатун: STL построен от первой шарнирной точки (начало координат) вдоль +X ко второй.
/// Точки заданы в системах координат соединяемых деталей, мм.
/// </summary>
public sealed record LinkSpec
{
    public required string From { get; init; }
    public double[] FromPoint { get; init; } = [0, 0, 0];
    public required string To { get; init; }
    public double[] ToPoint { get; init; } = [0, 0, 0];

    /// <summary>
    /// Для пружины: длина STL вдоль +X, мм. Если задана, звено растягивается/сжимается по X
    /// до расстояния между точками; иначе длина должна совпадать с расстоянием.
    /// </summary>
    public double? StretchLengthMm { get; init; }
}

public sealed record GearSpec
{
    public required int Teeth { get; init; }
    public required double PitchRadiusMm { get; init; }
    public required double RootRadiusMm { get; init; }
    public required double ThicknessMm { get; init; }
}

/// <summary>
/// Связь координат двух деталей: from — ближе к приводу, to — ведомая.
/// gear (вращение→вращение), rack и screw (вращение→сдвиг), crank-slider (кривошип→ползун).
/// </summary>
public sealed record CouplingSpec
{
    public required string Type { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }

    /// <summary>gear: dθ_to/dθ_from. Если не задано — −z_from/z_to по числу зубьев.</summary>
    public double? Ratio { get; init; }

    /// <summary>rack: радиус делительной окружности шестерни, мм (по умолчанию из gear детали from).</summary>
    public double? RadiusMm { get; init; }

    /// <summary>screw: ход резьбы за оборот, мм (знак задаёт направление).</summary>
    public double? LeadMm { get; init; }

    /// <summary>crank-slider: радиус кривошипа, длина шатуна, смещение оси ползуна (дезаксиал), мм.</summary>
    public double? CrankMm { get; init; }
    public double? RodMm { get; init; }
    public double OffsetMm { get; init; }

    /// <summary>four-bar: длины шатуна и коромысла, мм (кривошип — crankMm, стойка — расстояние между осями).</summary>
    public double? CouplerMm { get; init; }
    public double? RockerMm { get; init; }

    /// <summary>four-bar: сборка (+1 или −1) — по какую сторону от линии «палец кривошипа — ось коромысла» лежит шарнир.</summary>
    public int Branch { get; init; } = 1;

    /// <summary>geneva: число пазов креста.</summary>
    public int? Slots { get; init; }

    /// <summary>cam: участки закона движения толкателя за оборот кулачка (сумма углов — 360°).</summary>
    public IReadOnlyList<CamSegmentSpec>? Segments { get; init; }
}

/// <summary>Участок закона движения толкателя: подъём (rise > 0), опускание (rise < 0) или выстой (rise = 0).</summary>
public sealed record CamSegmentSpec
{
    public required double AngleDeg { get; init; }
    public double RiseMm { get; init; }

    /// <summary>cycloidal (без ударов по ускорению) или harmonic.</summary>
    public string Law { get; init; } = "cycloidal";
}

/// <summary>
/// Позиция спецификации (крепёж и комплектующие). Если заданы <see cref="Positions"/> и геометрия
/// (STL или стандартный <see cref="Type"/>), элемент рисуется в сцене; иначе — только в спецификации.
/// </summary>
public sealed record FastenerSpec
{
    /// <summary>Назначение: «Винт крепления направляющих».</summary>
    public required string Kind { get; init; }

    /// <summary>Обозначение размера для спецификации: «M3×8».</summary>
    public required string Size { get; init; }

    /// <summary>socket-screw, self-tapping, set-screw, pin, retaining-ring, spring — для процедурной модели и стандарта.</summary>
    public string? Type { get; init; }

    /// <summary>Номинальный диаметр (резьбы, штифта, вала под кольцо), мм.</summary>
    public double? DiameterMm { get; init; }

    /// <summary>Длина стержня, мм.</summary>
    public double? LengthMm { get; init; }

    /// <summary>Своя модель вместо процедурной (начало координат — опорная плоскость головки, ось +Z — вглубь).</summary>
    public string? Stl { get; init; }

    /// <summary>
    /// Точки установки в системе координат детали <see cref="AttachedTo"/> (или мира): центр опорной плоскости головки, мм.
    /// </summary>
    public double[][]? Positions { get; init; }

    /// <summary>Направление ввинчивания (от головки к концу стержня).</summary>
    public double[] Axis { get; init; } = [0, 0, 1];

    /// <summary>Деталь, с которой элемент движется и которую подсвечивает выбор в спецификации.</summary>
    public string? AttachedTo { get; init; }

    /// <summary>Количество; при заданных <see cref="Positions"/> берётся их число.</summary>
    public int Count { get; init; } = 1;

    /// <summary>Пояснение, например «входит в модель мотора».</summary>
    public string? Note { get; init; }

    public int Quantity => Positions is { Length: > 0 } p ? p.Length : Count;
}

/// <summary>
/// Примитив формы детали; задаётся ровно одно поле. Тела выдавливаются вдоль Z (z — [от, до]), мм.
/// Пересекающиеся примитивы образуют одно тело, как при объединении.
/// </summary>
public sealed record ShapeSpec
{
    /// <summary>Параллелепипед [x0, y0, z0, x1, y1, z1].</summary>
    public double[]? Box { get; init; }

    public CylinderShape? Cylinder { get; init; }
    public PolygonShape? Polygon { get; init; }
    public GearShape? Gear { get; init; }
    public RackShape? Rack { get; init; }
    public RingXShape? RingX { get; init; }
    public ThreadShape? Thread { get; init; }
    public CamShape? Cam { get; init; }
    public GenevaShape? GenevaStar { get; init; }
    public GenevaShape? GenevaDisk { get; init; }

    [JsonIgnore]
    public int KindCount =>
        new object?[] { Box, Cylinder, Polygon, Gear, Rack, RingX, Thread, Cam, GenevaStar, GenevaDisk }.Count(k => k is not null);
}

/// <summary>Цилиндр с осью Z: центр at, радиус r, сегментов по окружности.</summary>
public sealed record CylinderShape
{
    public double[] At { get; init; } = [0, 0];
    public required double R { get; init; }
    public required double[] Z { get; init; }
    public int Segments { get; init; } = 48;
}

/// <summary>Простой многоугольник [[x, y], …] (против часовой), выдавленный по Z.</summary>
public sealed record PolygonShape
{
    public required double[][] Points { get; init; }
    public required double[] Z { get; init; }
}

/// <summary>Прямозубое колесо: зуб — трапеция с углом профиля 20° и боковым зазором; первый зуб на phaseDeg.</summary>
public sealed record GearShape
{
    public double[] At { get; init; } = [0, 0];
    public required double Module { get; init; }
    public required int Teeth { get; init; }
    public double PhaseDeg { get; init; }
    public required double[] Z { get; init; }
}

/// <summary>
/// Зубья рейки: делительная прямая через origin вдоль direction, зубья смотрят влево от направления;
/// зуб k стоит на k·π·m, teeth — [первый k, последний k].
/// </summary>
public sealed record RackShape
{
    public required double[] Origin { get; init; }
    public double[] Direction { get; init; } = [1, 0];
    public required double Module { get; init; }
    public required int[] Teeth { get; init; }
    public required double[] Z { get; init; }
}

/// <summary>Кольцо (виток пружины) вокруг оси X: x — [от, до], r — [внутренний, наружный].</summary>
public sealed record RingXShape
{
    public required double[] X { get; init; }
    public required double[] R { get; init; }
    public int Segments { get; init; } = 32;
}

/// <summary>Правая резьба вокруг Z: трапеция r — [впадина, вершина], width — [у впадины, у вершины], шаг pitch.</summary>
public sealed record ThreadShape
{
    public required double[] R { get; init; }
    public required double Pitch { get; init; }
    public required double[] Z { get; init; }
    public required double[] Width { get; init; }
    public int PerTurn { get; init; } = 32;
}

/// <summary>
/// Контур дискового кулачка под плоский толкатель: огибающая положений тарелки по закону связи cam этой детали.
/// Базовый радиус и направление — от оси кулачка до начала координат толкателя.
/// </summary>
public sealed record CamShape
{
    public required double[] Z { get; init; }
    public int Samples { get; init; } = 720;
}

/// <summary>
/// Мальтийский крест (genevaStar) или запирающий диск ведущего звена с выемкой у пальца (genevaDisk).
/// Пазы креста — на π/n + 2πk/n, палец ведущего диска — на его локальной +X.
/// </summary>
public sealed record GenevaShape
{
    public required int Slots { get; init; }
    public required double CenterDistance { get; init; }
    public required double SlotWidth { get; init; }
    public required double PinRadius { get; init; }
    public required double[] Z { get; init; }
}
