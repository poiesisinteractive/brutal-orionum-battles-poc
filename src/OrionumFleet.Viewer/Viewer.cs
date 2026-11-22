using System.Diagnostics;
using Brutal;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Backends;
using Brutal.Numerics;
using Brutal.OpenGlApi;
using OrionumFleet.Sim;
using OrionumFleet.Viewer.Rendering;

namespace OrionumFleet.Viewer;

/// <summary>Effet visuel éphémère tiré d'un événement de la sim, daté en temps de sim (la pause le fige).</summary>
struct Effect
{
    public SimEventType Type;
    public double Start;
    public float Life;
    public float3 At;
    public float3 To;
    public float Size;
    public float3 Color;
    /// <summary>Faisceaux épais (lances, rayon axial) : largeur en mètres ; 0 = trait fin.</summary>
    public float Width;
}

/// <summary>Ce que suit la caméra.</summary>
enum Follow
{
    None,
    Melee,
    Ship,
}

/// <summary>
/// Visualiseur de débug de la sim : la bataille tourne en direct (pas fixe de 0,05 s, de ×1/8 à ×64), rendue
/// en HDR avec le bloom de Brutal Arena, de la passe d'un chasseur de 30 m au système entier (des milliers de km).
/// Formes simples : pavés orientés pour les coques, astres procéduraux, traits lumineux pour les tirs, icônes
/// tactiques de loin. Ce n'est pas la direction artistique du jeu.
/// </summary>
sealed class Viewer : IDisposable
{
    const float SplashSeconds = 2f;
    /// <summary>Temps de calcul de la sim accordé à une image (ms) : au-delà, la vitesse effective baisse.</summary>
    const double StepBudgetMs = 30;
    const float MinDistance = 30;
    const float MaxDistance = 3e7f;
    /// <summary>En dessous de cette taille apparente (pixels), un vaisseau devient une icône tactique.</summary>
    const float IconBelowPixels = 3;

    // Couleurs des solides (sRGB) et des traits (HDR linéaire).
    static readonly float3 CoalitionHull = new(0.55f, 0.68f, 0.9f);
    static readonly float3 ClansHull = new(0.85f, 0.5f, 0.32f);
    static readonly float3 DepotHull = new(0.6f, 0.36f, 0.3f);
    static readonly float3 CoalitionGlow = new(0.35f, 0.65f, 1f);
    static readonly float3 ClansGlow = new(1f, 0.45f, 0.12f);
    static readonly float3 WellRing = new(1.6f, 0.45f, 2.6f);

    readonly GlfwWindow _window;
    readonly GlfwBackend _glfwBackend;
    readonly OpenGlBackend _openGlBackend;
    readonly Renderer _renderer;

    World _world = null!;
    int _scenario = Scenarios.SystemBattle;
    int _seed = 1;
    int _fleetScale = 2;
    bool _paused;
    float _speed = 1;
    bool _stepOnce;
    double _accumulator;
    double _stepMs;
    double _frameMs;
    double _effectiveSpeed;
    double _startTime;

    // Caméra orbitale autour d'un point visé.
    float3 _target;
    float _yaw = 0.6f;
    float _pitch = 0.45f;
    float _distance = 60_000;
    Follow _follow;

    // Sélection.
    int _selectedId;
    bool _dragging;
    float2 _pressAt;
    float2? _pendingClick;
    bool _pendingDouble;

    bool _showUi = true;
    bool _showLabels = true;
    bool _showIcons = true;
    bool _showReach = true;
    bool _showGrid = true;

    readonly List<Effect> _effects = new(512);
    readonly List<string> _feed = [];
    float2[] _screen = [];
    bool[] _onScreen = [];

    // Mode capture (--capture) : avance rapide jusqu'à --at secondes de sim, une image PNG, puis on quitte.
    readonly string? _capturePath;
    readonly double _captureAt;
    readonly string? _captureSelect;
    int _captureFrames;

    /// <summary>
    /// Options : --scenario n, --seed n, --per-side n, --capture fichier.png, --at secondes, --yaw/--pitch/--distance,
    /// --select nom (capture : sélectionne et suit ce vaisseau).
    /// </summary>
    public Viewer(string[] args)
    {
        string? Arg(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        float FloatArg(string name, float fallback) =>
            float.TryParse(Arg(name), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        _scenario = Math.Clamp((int)FloatArg("--scenario", _scenario), 0, Scenarios.Names.Length - 1);
        _seed = (int)FloatArg("--seed", 1);
        _fleetScale = Math.Clamp((int)FloatArg("--fleet", _fleetScale), 1, 8);
        _capturePath = Arg("--capture");
        _captureAt = FloatArg("--at", 60);
        _captureSelect = Arg("--select");
        var yaw = FloatArg("--yaw", float.NaN);
        var pitch = FloatArg("--pitch", float.NaN);
        var distance = FloatArg("--distance", float.NaN);

        Glfw.WindowHint(GlfwWindowHint.ContextVersionMajor, 4);
        Glfw.WindowHint(GlfwWindowHint.ContextVersionMinor, 1);
        Glfw.WindowHint(GlfwWindowHint.OpenGlProfile, Glfw.Constants.GLFW_OPENGL_CORE_PROFILE);
        if (Platform.IsMacOS)
            Glfw.WindowHint(GlfwWindowHint.OpenGlForwardCompat, 1);

        _window = Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = "Orionum : combat de flotte (visualiseur de débug)", Size = new int2(1600, 900) });
        _window.MakeContextCurrent();
        Glfw.SwapInterval(1);
        unsafe
        {
            if (!Glad.Initialize(Glfw.PInvoke.GetProcAddress))
                throw new InvalidOperationException("Échec de l'initialisation d'OpenGL");
        }
        _window.OnKey += OnKey;

        ImGui.GetIO().IniFilename = null;
        _glfwBackend = new GlfwBackend(_window, GlfwBackend.ClientApi.OpenGL);
        _openGlBackend = new OpenGlBackend();
        _renderer = new Renderer();
        _renderer.Post.BloomThreshold = 1.1f;
        _renderer.Post.BloomStrength = 0.9f;

        Restart();
        if (!float.IsNaN(yaw))
            _yaw = yaw;
        if (!float.IsNaN(pitch))
            _pitch = pitch;
        if (!float.IsNaN(distance))
            _distance = distance;
    }

    bool SystemScale => _world.Scenario.SpawnPoints.Length > 0;

    void Restart()
    {
        _world = Scenarios.Create(_scenario, (uint)Math.Max(1, _seed), _fleetScale);
        _effects.Clear();
        _feed.Clear();
        _accumulator = 0;
        _selectedId = 0;
        if (SystemScale)
        {
            // Vue de départ : la flotte de la Coalition, de haut, l'ennemi dans le champ.
            _target = _world.Home(Side.Coalition);
            var toEnemy = _world.Home(Side.Clans) - _target;
            _yaw = MathF.Atan2(-toEnemy.X, -toEnemy.Z);
            _pitch = 0.35f;
            _distance = 25_000;
            _follow = Follow.None;
        }
        else
        {
            _target = Centroid() ?? float3.Zero;
            (_yaw, _pitch, _distance, _follow) = (0.6f, 0.75f, 4200, Follow.Melee);
        }
    }

    public void Run()
    {
        var previous = Glfw.Time;
        _startTime = previous;
        while (!_window.ShouldClose)
        {
            Glfw.PollEvents();
            var now = Glfw.Time;
            var frameTime = Math.Min(now - previous, 0.25);
            previous = now;

            var capturing = _capturePath is not null;
            var splash = !capturing && now - _startTime < SplashSeconds;
            if (capturing)
                CaptureAdvance();
            else if (!splash)
                Advance(frameTime);

            _glfwBackend.NewFrame();
            _openGlBackend.NewFrame();
            ImGui.NewFrame();
            ImString.SharedStorage.Reset();
            if (!capturing)
                UpdateCamera((float)frameTime);
            else
                FollowTarget(1);

            var renderStart = Stopwatch.GetTimestamp();
            var drawn = RenderScene();
            _frameMs = Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds;
            if (drawn)
                Pick();
            BuildUi(splash);
            ImGui.Render();
            if (drawn)
                _openGlBackend.RenderDrawData(ImGui.GetDrawData());

            // Quelques images d'abord : ImGui a besoin de deux frames pour dimensionner ses fenêtres.
            if (capturing && drawn && ++_captureFrames == 4)
            {
                Glfw.PInvoke.GetFramebufferSize(_window, out var width, out var height);
                Capture.SavePng(_capturePath!, width, height);
                _window.ShouldClose = true;
            }
            _window.SwapBuffers();
        }
    }

    // --- Simulation ------------------------------------------------------------------------

    void Advance(double frameTime)
    {
        var steps = 0;
        var clock = Stopwatch.StartNew();
        if (_stepOnce)
        {
            StepOnce();
            steps = 1;
            _stepOnce = false;
        }
        else if (!_paused && _world.Result is null)
        {
            _accumulator += frameTime * _speed;
            // Autant de pas que le budget de l'image le permet ; le retard au-delà est abandonné (la vitesse
            // effective baisse plutôt que de geler l'affichage).
            while (_accumulator >= World.DtExact && _world.Result is null && clock.Elapsed.TotalMilliseconds < StepBudgetMs)
            {
                StepOnce();
                _accumulator -= World.DtExact;
                steps++;
            }
            _accumulator = Math.Min(_accumulator, World.DtExact * 2);
        }
        if (steps > 0)
            _stepMs = clock.Elapsed.TotalMilliseconds / steps;
        var advanced = steps * World.DtExact;
        _effectiveSpeed = frameTime > 0 ? _effectiveSpeed * 0.9 + 0.1 * advanced / frameTime : _effectiveSpeed;
        _effects.RemoveAll(fx => _world.Time - fx.Start > fx.Life);
    }

    void StepOnce()
    {
        _world.Step();
        foreach (var e in _world.DrainEvents())
            OnEvent(e);
    }

    void CaptureAdvance()
    {
        // Avance à pleine vitesse jusqu'à l'instant voulu ; les effets des dernières secondes restent visibles.
        while (_world.Time < _captureAt && _world.Result is null)
            StepOnce();
        _effects.RemoveAll(fx => _world.Time - fx.Start > fx.Life);
        if (_captureFrames > 0)
            return;
        if (_captureSelect is not null)
        {
            foreach (ref readonly var s in _world.Ships.AsSpan())
                if (s.IsActive && s.Name == _captureSelect)
                {
                    _selectedId = s.Id;
                    FocusSelected();
                    _target = s.Pos;
                    break;
                }
        }
        else if (!SystemScale)
        {
            // Dans le système, le barycentre des deux flottes tombe dans le vide entre elles : on garde la vue de départ.
            _target = Centroid() ?? _target;
        }
    }

    void OnEvent(in SimEvent e)
    {
        var glow = e.Side == Side.Coalition ? CoalitionGlow : ClansGlow;
        switch (e.Type)
        {
            case SimEventType.Beam when e.Beam == BeamKind.Lance:
                // Lance (rayon tenu, ré-émis à chaque pas) ou rayon axial (un éclair colossal).
                var spinalBeam = e.WeaponId == WeaponId.Spinal;
                _effects.Add(new Effect
                {
                    Type = e.Type, Start = _world.Time, Life = spinalBeam ? 0.9f : 0.07f, At = e.At, To = e.Vector,
                    Color = spinalBeam ? new float3(1.2f, 1.6f, 2.4f) : glow * 1.6f, Width = spinalBeam ? 45 : 9,
                });
                break;
            case SimEventType.Beam:
                var (beamColor, beamLife) = e.Beam switch
                {
                    BeamKind.Turbolaser => (glow * 5f, 0.3f),
                    BeamKind.Drone => (glow * 2.5f, 0.1f),
                    _ => (glow * 1.5f, 0.08f),
                };
                AddEffect(e.Type, beamLife, e.At, e.Vector, 0, beamColor);
                break;
            case SimEventType.Fire when e.WeaponId == WeaponId.Spinal:
                // Moment fort : la salve axiale d'un vaisseau amiral.
                AddEffect(SimEventType.Explosion, 1.2f, e.At, default, 260, new float3(1f, 0.9f, 0.7f));
                Feed($"{_world.Time:F0} s  {ShipLabel(e.OtherId)} tire son canon axial");
                break;
            case SimEventType.TurretDestroyed:
                AddEffect(SimEventType.Explosion, 1.1f, e.At, default, 45, new float3(1f, 0.55f, 0.2f));
                break;
            case SimEventType.Explosion:
                // Plafonnée pour les petites coques ; la mort d'un capital (taille = 2,2 rayons) reste énorme mais lisible.
                var size = e.Value >= 300 ? MathF.Min(e.Value * 0.45f, 1600) : Math.Clamp(e.Value * 1.5f, 6, 140);
                AddEffect(e.Type, e.Value >= 300 ? 3f : 0.7f, e.At, default, size, new float3(1f, 0.6f, 0.25f));
                break;
            case SimEventType.Secondary:
                AddEffect(SimEventType.Explosion, 0.8f, e.At, default, Math.Clamp(e.Value * 1.5f, 8, 60), new float3(1f, 0.5f, 0.2f));
                break;
            case SimEventType.Blast:
            case SimEventType.CoreBreach:
                AddEffect(e.Type, 1.4f, e.At, default, e.Value, new float3(1f, 0.55f, 0.2f));
                break;
            case SimEventType.EmpBlast:
                AddEffect(e.Type, 1.2f, e.At, default, e.Value, new float3(0.3f, 0.8f, 1f));
                break;
            case SimEventType.ShieldHit:
                AddEffect(e.Type, 0.18f, e.At, default, 6 + MathF.Min(30, e.Value * 0.15f), new float3(0.3f, 0.75f, 1f));
                break;
            case SimEventType.Swallowed:
                // Un tir qui s'écrase sur un astre, ou avalé par un cœur.
                var planet = WellIndex(e.OtherId) is var wi and >= 0 && _world.Wells[wi].Planet;
                AddEffect(e.Type, 0.6f, e.At, default, planet ? 4 + e.Value : 6 + e.Value * 2,
                    planet ? new float3(1f, 0.55f, 0.3f) : new float3(0.7f, 0.25f, 1f));
                break;
            case SimEventType.ShipCrippled:
                Feed($"{_world.Time:F1} s  {ShipLabel(e.ShipId)} hors de combat (par {ShipLabel(e.OtherId)})");
                break;
            case SimEventType.ShipDestroyed:
                Feed($"{_world.Time:F1} s  {ShipLabel(e.ShipId)} détruit");
                break;
        }
    }

    int WellIndex(int id)
    {
        for (var i = 0; i < _world.Wells.Count; i++)
            if (_world.Wells[i].Id == id)
                return i;
        return -1;
    }

    void AddEffect(SimEventType type, float life, float3 at, float3 to, float size, float3 color) =>
        _effects.Add(new Effect { Type = type, Start = _world.Time, Life = life, At = at, To = to, Size = size, Color = color });

    void Feed(string line)
    {
        _feed.Add(line);
        if (_feed.Count > 12)
            _feed.RemoveAt(0);
    }

    string ShipLabel(int id)
    {
        var i = id != 0 ? _world.ShipIndex(id) : -1;
        return i >= 0 ? $"{_world.Ships[i].Name} ({_world.Ships[i].Class.LabelFr})" : "?";
    }

    float3? Centroid()
    {
        var sum = float3.Zero;
        var n = 0;
        foreach (ref readonly var s in _world.Ships.AsSpan())
            if (s.IsActive && s.ClassId != ShipClassId.Depot)
            {
                sum += s.Pos;
                n++;
            }
        return n > 0 ? sum / n : null;
    }

    int SelectedIndex => _selectedId != 0 ? _world.ShipIndex(_selectedId) : -1;

    // --- Caméra et entrées -----------------------------------------------------------------

    void UpdateCamera(float dt)
    {
        var io = ImGui.GetIO();
        var mouse = ImGui.GetMousePos();
        if (!io.WantCaptureMouse)
        {
            // Clic simple (sans glisser) : sélection ; double-clic : sélection et suivi ; glisser : tourner.
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left, false))
                (_pressAt, _dragging) = (mouse, false);
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left) && Vec.Len(new float3(mouse.X - _pressAt.X, mouse.Y - _pressAt.Y, 0)) > 4)
                _dragging = true;
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && !_dragging)
                _pendingClick = mouse;
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                (_pendingClick, _pendingDouble) = (mouse, true);

            var delta = io.MouseDelta;
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left) && _dragging)
            {
                _yaw -= delta.X * 0.005f;
                _pitch = Math.Clamp(_pitch + delta.Y * 0.005f, -1.5f, 1.5f);
            }
            if (ImGui.IsMouseDown(ImGuiMouseButton.Right) || ImGui.IsMouseDown(ImGuiMouseButton.Middle))
            {
                _follow = Follow.None;
                var right = new float3(MathF.Cos(_yaw), 0, -MathF.Sin(_yaw));
                var forward = new float3(MathF.Sin(_yaw), 0, MathF.Cos(_yaw));
                var k = _distance * 0.0015f;
                _target += right * (-delta.X * k) + forward * (-delta.Y * k);
            }
            if (io.MouseWheel != 0)
                _distance = Math.Clamp(_distance * MathF.Pow(0.85f, io.MouseWheel), MinDistance, MaxDistance);
        }
        FollowTarget(1 - MathF.Exp(-dt * 4));
    }

    void FollowTarget(float blend)
    {
        switch (_follow)
        {
            case Follow.Ship when SelectedIndex is var i and >= 0 && _world.Ships[i].Alive:
                _target = float3.Lerp(_target, _world.Ships[i].Pos, blend);
                break;
            case Follow.Ship:
                _follow = Follow.None;
                break;
            case Follow.Melee when Centroid() is { } c:
                _target = float3.Lerp(_target, c, blend);
                break;
        }
    }

    float3 Eye => _target + new float3(MathF.Sin(_yaw) * MathF.Cos(_pitch), MathF.Sin(_pitch), MathF.Cos(_yaw) * MathF.Cos(_pitch)) * _distance;

    /// <summary>Le clic en attente : le vaisseau le plus proche du curseur à l'écran (icône ou modèle).</summary>
    void Pick()
    {
        if (_pendingClick is not { } at)
            return;
        var best = -1;
        var bestD = 18f * 18f;
        for (var i = 0; i < _world.Ships.Count && i < _screen.Length; i++)
        {
            if (!_onScreen[i])
                continue;
            var d = _screen[i] - at;
            var d2 = d.X * d.X + d.Y * d.Y;
            if (d2 < bestD)
                (best, bestD) = (i, d2);
        }
        if (best >= 0)
        {
            _selectedId = _world.Ships[best].Id;
            if (_pendingDouble)
                FocusSelected();
        }
        else if (!_pendingDouble)
        {
            _selectedId = 0;
        }
        (_pendingClick, _pendingDouble) = (null, false);
    }

    /// <summary>Zoom sur le vaisseau sélectionné, puis la caméra le suit.</summary>
    void FocusSelected()
    {
        if (SelectedIndex is not (var i and >= 0))
            return;
        _follow = Follow.Ship;
        _distance = Math.Clamp(_world.Ships[i].Class.Radius * 5, 150, 15_000);
    }

    void OnKey(GlfwWindow window, GlfwKey key, int scancode, GlfwKeyAction action, GlfwModifier mods)
    {
        if (action != GlfwKeyAction.Press || ImGui.GetIO().WantCaptureKeyboard)
            return;
        switch (key)
        {
            case GlfwKey.Escape:
                (_selectedId, _follow) = (0, _follow == Follow.Ship ? Follow.None : _follow);
                break;
            case GlfwKey.Space:
                _paused = !_paused;
                break;
            case GlfwKey.Period:
                _stepOnce = true;
                break;
            case GlfwKey.R:
                Restart();
                break;
            case GlfwKey.F:
                if (SelectedIndex >= 0)
                    FocusSelected();
                else
                    _follow = Follow.Melee;
                break;
            case GlfwKey.Home:
                FrameBattle();
                break;
            case GlfwKey.G:
                _showIcons = !_showIcons;
                break;
            case GlfwKey.F1:
                _showUi = !_showUi;
                break;
            case GlfwKey.KpAdd:
            case GlfwKey.Equal:
                _speed = MathF.Min(64, _speed * 2);
                break;
            case GlfwKey.KpSubtract:
            case GlfwKey.Minus:
                _speed = MathF.Max(0.125f, _speed / 2);
                break;
        }
    }

    /// <summary>Cadre toute la bataille : centre des coques, recul selon leur étendue.</summary>
    void FrameBattle()
    {
        if (Centroid() is not { } c)
            return;
        var spread = 0f;
        foreach (ref readonly var s in _world.Ships.AsSpan())
            if (s.IsActive)
                spread = MathF.Max(spread, Vec.Dist(s.Pos, c));
        (_target, _distance, _follow) = (c, Math.Clamp(spread * 2.2f, 2000, MaxDistance), Follow.None);
    }

    // --- Rendu -----------------------------------------------------------------------------

    /// <returns>false si la fenêtre est réduite et que rien n'a été dessiné.</returns>
    bool RenderScene()
    {
        Glfw.PInvoke.GetFramebufferSize(_window, out var width, out var height);
        if (width == 0 || height == 0)
            return false;

        var eye = Eye;
        FillLights(eye);
        _renderer.BeginFrame(new int2(width, height), eye, _target);

        DrawStar();
        if (_showGrid && !SystemScale)
            DrawGrid();
        DrawWells();
        DrawShips();
        DrawDrones();
        DrawProjectiles();
        DrawEffects();
        DrawSelection();

        _renderer.EndFrame();
        ProjectShips();
        return true;
    }

    void FillLights(float3 eye)
    {
        var lights = _renderer.Lights;
        lights.Clear();
        foreach (ref readonly var g in _world.Wells.AsSpan())
            if (!g.Planet)
                lights.Add(g.Pos - eye, g.Core * 4, new float3(0.6f, 0.2f, 1f), 1.2f);
        // Les explosions proches les plus récentes d'abord.
        for (var i = _effects.Count - 1; i >= 0 && lights.Count < LightBuffer.MaxLights; i--)
        {
            var fx = _effects[i];
            if (fx.Type is not (SimEventType.Explosion or SimEventType.Blast or SimEventType.CoreBreach))
                continue;
            if (Vec.Dist(fx.At, eye) > 20_000)
                continue;
            var t = (float)((_world.Time - fx.Start) / fx.Life);
            lights.Add(fx.At - eye, fx.Size * 8 + 150, new float3(1f, 0.6f, 0.3f), 6 * (1 - t));
        }
    }

    /// <summary>Wolf 359, naine rouge, loin dans la direction de la lumière (toujours à la même distance de l'œil).</summary>
    void DrawStar()
    {
        if (!SystemScale)
            return;
        var dir = _renderer.SunDirection;
        _renderer.DrawSphere(_renderer.Origin + dir * 4e8f, 1.1e7f, new float3(1f, 0.38f, 0.18f), 40);
    }

    void DrawGrid()
    {
        const float Step = 500, Extent = 3500, Y = -400;
        var color = new float3(0.03f, 0.045f, 0.08f);
        for (var x = -Extent; x <= Extent; x += Step)
        {
            _renderer.Line(new float3(x, Y, -Extent), new float3(x, Y, Extent), color);
            _renderer.Line(new float3(-Extent, Y, x), new float3(Extent, Y, x), color);
        }
    }

    /// <summary>Couleur et surface d'un astre, d'après son nom.</summary>
    static (float3 Color, Surface Surface) PlanetLook(string name) => name switch
    {
        "Wolf 359 b" => (new float3(0.86f, 0.62f, 0.42f), Surface.GasGiant),
        "Mirande" => (new float3(0.45f, 0.55f, 0.68f), Surface.Rock),
        "Ixion" => (new float3(0.62f, 0.6f, 0.58f), Surface.Rock),
        _ => (new float3(0.55f, 0.5f, 0.46f), Surface.Rock),
    };

    void DrawWells()
    {
        foreach (ref readonly var g in _world.Wells.AsSpan())
        {
            if (g.Planet)
            {
                var (color, surface) = PlanetLook(_world.WellName(g));
                _renderer.DrawGlobe(g.Pos, g.Core, color, surface);
                continue;
            }
            var core = Terrain.WellCore(g);
            if (core > 0)
            {
                _renderer.DrawSphere(g.Pos, core, new float3(0.01f, 0.01f, 0.015f));
                _renderer.Circle(g.Pos, core * 1.12f, WellRing, 96);
                _renderer.Circle(g.Pos, core * 1.35f, WellRing * 0.35f, 96);
            }
            if (_showReach)
                _renderer.Circle(g.Pos, g.Reach, new float3(0.12f, 0.04f, 0.2f), 128);
        }
    }

    void DrawShips()
    {
        foreach (ref readonly var s in _world.Ships.AsSpan())
        {
            if (!s.Alive || s.Withdrawn)
                continue;
            var cls = s.Class;
            var r = cls.Radius;
            // Trop petit pour être vu : l'icône tactique prend le relais (le modèle coûterait sans rien montrer).
            if (_showIcons && _renderer.PixelRadius(s.Pos, r) < IconBelowPixels * 0.5f)
                continue;
            var fwd = Vec.LenSq(s.Fwd) > 1e-6f ? Vec.Normalize(s.Fwd) : float3.UnitZ;
            var up = MathF.Abs(fwd.Y) > 0.98f ? float3.UnitX : float3.UnitY;
            var world = float4x4.CreateWorld(s.Pos, fwd, up);
            var depot = s.ClassId == ShipClassId.Depot;
            var hullColor = depot ? DepotHull : s.Side == Side.Coalition ? CoalitionHull : ClansHull;
            var glow = s.Side == Side.Coalition ? CoalitionGlow : ClansGlow;
            var dying = s.Dying is not null;
            if (dying)
                hullColor *= 0.35f;

            if (depot)
            {
                _renderer.DrawBox(float4x4.CreateScale(r * 0.7f, r * 0.45f, r * 0.7f) * world, hullColor);
                _renderer.DrawBox(float4x4.CreateScale(r * 0.25f, r * 0.75f, r * 0.25f) * world, hullColor * 0.8f, 0.2f);
            }
            else
            {
                // Pavé allongé dans l'axe du nez, avec une passerelle sur le dos.
                _renderer.DrawBox(float4x4.CreateScale(r * 0.38f, r * 0.2f, r) * world, hullColor);
                _renderer.DrawBox(float4x4.CreateScale(r * 0.16f, r * 0.14f, r * 0.3f) * float4x4.CreateTranslation(0, r * 0.3f, r * 0.25f) * world, hullColor * 0.85f);
                // Tuyère : brille avec la poussée (en croisière, à plein).
                var engine = s.Pos - fwd * (r * 1.05f);
                _renderer.DrawSphere(engine, r * 0.17f, glow, dying ? 0.3f : 1.5f + s.Thrust * 6 + (s.Cruise ? 6 : 0));
                _renderer.Line(s.Pos + fwd * r, s.Pos + fwd * (r * 1.6f), glow * 2);
                if (s.Cruise)
                    _renderer.Line(engine, engine - fwd * (r * 12), glow * 1.2f, float3.Zero); // traînée de croisière
            }

            if (dying)
            {
                var flicker = 0.5f + 0.5f * MathF.Sin((float)_world.Time * 23 + s.Id);
                _renderer.DrawSphere(s.Pos, r * 0.3f, new float3(1f, 0.45f, 0.15f), 3 + 5 * flicker);
            }
        }
    }

    /// <summary>
    /// Drones : petite flèche émissive dans l'axe du vol, avec une traînée. Les intercepteurs sont plus clairs ;
    /// un drone orphelin (porteur perdu) s'éteint peu à peu. Invisibles de loin (moins d'un pixel) : sautés.
    /// </summary>
    void DrawDrones()
    {
        var eye = _renderer.Origin;
        var maxRange = _renderer.PixelsPerRadian * 5 / 0.6f; // un drone de 5 m fait encore 0,6 pixel
        foreach (ref readonly var d in _world.Drones.AsSpan())
        {
            if (!d.Alive || Vec.DistSq(d.Pos, eye) > maxRange * maxRange)
                continue;
            var glow = d.Side == Side.Coalition ? CoalitionGlow : ClansGlow;
            var interceptor = d.Role == DroneRole.Interceptor;
            var power = d.OrphanTime > 0 ? 0.6f : interceptor ? 3.5f : 2.2f;
            var color = interceptor ? float3.Lerp(glow, new float3(1, 1, 1), 0.45f) : glow;
            var fwd = Vec.LenSq(d.Fwd) > 1e-6f ? Vec.Normalize(d.Fwd) : float3.UnitZ;
            var up = MathF.Abs(fwd.Y) > 0.98f ? float3.UnitX : float3.UnitY;
            _renderer.DrawBox(float4x4.CreateScale(2.2f, 1.2f, 5f) * float4x4.CreateWorld(d.Pos, fwd, up), color, power);
            _renderer.Line(d.Pos - Vec.Scale(d.Vel, 0.12f), d.Pos, new float3(0, 0, 0), color * (power * 0.4f));
        }
    }

    void DrawProjectiles()
    {
        var eye = _renderer.Origin;
        var maxRange = MathF.Max(60_000, _distance * 3);
        foreach (ref readonly var p in _world.Projectiles.AsSpan())
        {
            if (!p.Alive || Vec.DistSq(p.Pos, eye) > maxRange * maxRange)
                continue;
            var glow = p.Side == Side.Coalition ? CoalitionGlow : ClansGlow;
            switch (p.Weapon)
            {
                case WeaponKind.Kinetic:
                    _renderer.Line(p.Pos - Vec.Scale(p.Vel, 0.07f), p.Pos, new float3(0.6f, 0.5f, 0.3f), new float3(4f, 3.4f, 2.2f));
                    break;
                case WeaponKind.Laser:
                    _renderer.Line(p.Pos - Vec.Scale(p.Vel, 0.05f), p.Pos, glow * 4);
                    break;
                case WeaponKind.Missile:
                    _renderer.Line(p.Pos - Vec.Scale(p.Vel, 0.15f), p.Pos, new float3(0.2f, 0.2f, 0.2f), new float3(4f, 2f, 0.6f));
                    _renderer.DrawSphere(p.Pos, 3, new float3(1f, 0.6f, 0.3f), 4);
                    break;
                case WeaponKind.Bomb:
                    _renderer.DrawSphere(p.Pos, 10, new float3(1f, 0.3f, 0.8f), 6);
                    break;
                case WeaponKind.Emp:
                    _renderer.DrawSphere(p.Pos, 9, new float3(0.3f, 0.85f, 1f), 6);
                    break;
                default:
                    _renderer.DrawSphere(p.Pos, 6, new float3(0.8f, 0.8f, 0.8f), 2);
                    break;
            }
        }
    }

    void DrawEffects()
    {
        var eye = _renderer.Origin;
        var maxRange = MathF.Max(80_000, _distance * 4);
        foreach (var fx in _effects)
        {
            if (Vec.DistSq(fx.At, eye) > maxRange * maxRange)
                continue;
            var t = Math.Clamp((float)((_world.Time - fx.Start) / fx.Life), 0, 1);
            var fade = 1 - t;
            switch (fx.Type)
            {
                case SimEventType.Beam when fx.Width > 0:
                    // Faisceau épais : un pavé émissif étiré de la bouche à l'impact, et un cœur blanc.
                    var axis = fx.To - fx.At;
                    var length = Vec.Len(axis);
                    if (length < 1)
                        break;
                    var dir = axis / length;
                    var up = MathF.Abs(dir.Y) > 0.98f ? float3.UnitX : float3.UnitY;
                    var frame = float4x4.CreateWorld((fx.At + fx.To) * 0.5f, dir, up);
                    var width = fx.Width * (0.6f + 0.4f * fade);
                    _renderer.DrawBox(float4x4.CreateScale(width, width, length / 2) * frame, fx.Color, 6 * fade);
                    _renderer.Line(fx.At, fx.To, new float3(3, 3, 3) * fade);
                    break;
                case SimEventType.Beam:
                    _renderer.Line(fx.At, fx.To, fx.Color * fade);
                    break;
                case SimEventType.Explosion:
                case SimEventType.ShieldHit:
                case SimEventType.Swallowed:
                    _renderer.DrawSphere(fx.At, fx.Size * (0.4f + 0.8f * t), fx.Color, 5 * fade * fade);
                    break;
                case SimEventType.Blast:
                case SimEventType.CoreBreach:
                case SimEventType.EmpBlast:
                    // Un noyau bref ; l'anneau qui s'étend montre la portée du souffle.
                    _renderer.DrawSphere(fx.At, MathF.Min(40, fx.Size * 0.1f) * (1 - t * 0.5f), fx.Color, 6 * fade * fade * fade);
                    _renderer.Circle(fx.At, fx.Size * (0.3f + 0.7f * t), fx.Color * (4 * fade), 72);
                    break;
            }
        }
    }

    /// <summary>
    /// Le vaisseau sélectionné : lignes vers ses cibles (rouges) et depuis ceux qui le visent (orangées), cercle de
    /// sa plus longue portée, ses drones reliés à lui, et son groupe de flotte cerclé.
    /// </summary>
    void DrawSelection()
    {
        if (SelectedIndex is not (var si and >= 0))
            return;
        ref readonly var s = ref _world.Ships[si];
        var cls = s.Class;
        var white = new float3(2.5f, 2.5f, 2.5f);
        _renderer.Circle(s.Pos, cls.Radius * 2.2f, white, 48);
        if (!s.Alive)
            return;
        _renderer.Circle(s.Pos, Movement.MaxWeaponRange(s), new float3(0.6f, 0.6f, 0.2f), 128);

        var targets = new HashSet<int>();
        for (var w = 0; w < s.WeaponCount; w++)
            if (s.Weapons[w].TargetId != 0)
                targets.Add(s.Weapons[w].TargetId);
        if (s.Orders.Count > 0 && s.Orders[0].TargetId != 0)
            targets.Add(s.Orders[0].TargetId);
        foreach (var id in targets)
            if (_world.ShipIndex(id) is var ti and >= 0 && _world.Ships[ti].Alive)
                _renderer.Line(s.Pos, _world.Ships[ti].Pos, new float3(3f, 0.4f, 0.3f), new float3(0.8f, 0.1f, 0.1f));

        foreach (ref readonly var o in _world.Ships.AsSpan())
        {
            if (!o.IsActive || o.Side == s.Side)
                continue;
            for (var w = 0; w < o.WeaponCount; w++)
                if (o.Weapons[w].TargetId == s.Id)
                {
                    _renderer.Line(o.Pos, s.Pos, new float3(1.2f, 0.5f, 0.1f), new float3(0.2f, 0.08f, 0.02f));
                    break;
                }
        }

        foreach (ref readonly var d in _world.Drones.AsSpan())
            if (d.Alive && d.CarrierId == s.Id)
                _renderer.Line(s.Pos, d.Pos, new float3(0.1f, 0.3f, 0.15f), new float3(0.4f, 1.6f, 0.6f));

        if (FleetSystem.GroupOf(_world.Fleet(s.Side), s.Id) is { } g)
            foreach (var id in g.Members)
                if (id != s.Id && _world.ShipIndex(id) is var gi and >= 0 && _world.Ships[gi].IsActive)
                    _renderer.Circle(_world.Ships[gi].Pos, _world.Ships[gi].Class.Radius * 1.8f, new float3(1.6f, 1.4f, 0.3f), 24);
    }

    /// <summary>Positions écran des coques (icônes, étiquettes, sélection).</summary>
    void ProjectShips()
    {
        var count = _world.Ships.Count;
        if (_screen.Length < count)
        {
            _screen = new float2[count];
            _onScreen = new bool[count];
        }
        for (var i = 0; i < count; i++)
        {
            ref readonly var s = ref _world.Ships[i];
            var p = s.Alive && !s.Withdrawn ? _renderer.Project(s.Pos, 1.05f) : null;
            _onScreen[i] = p.HasValue;
            if (p is { } v)
                _screen[i] = v;
        }
    }

    // --- UI --------------------------------------------------------------------------------

    static ImColor8 Col(float r, float g, float b, float a = 1)
    {
        var c = new float4(r, g, b, a);
        return ImGui.GetColorU32(in c);
    }

    static string Km(float meters) => meters >= 10_000 ? $"{meters / 1000:F0} km" : meters >= 1000 ? $"{meters / 1000:F1} km" : $"{meters:F0} m";

    void BuildUi(bool splash)
    {
        var io = ImGui.GetIO();
        var display = io.DisplaySize;
        const ImGuiWindowFlags OverlayFlags =
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove;

        if (splash)
        {
            // Licence BRUTAL §2.2 : le logo officiel (ahwoo.com/brutal-branding) doit remplacer ce texte avant toute diffusion.
            ImGui.SetNextWindowPos(display * 0.5f, ImGuiCond.Always, new float2(0.5f, 0.5f));
            if (ImGui.Begin("Splash"u8, OverlayFlags | ImGuiWindowFlags.NoInputs))
            {
                ImGui.PushFont(default, 48f);
                ImGui.Text("BRUTAL"u8);
                ImGui.PopFont();
                ImGui.Text("Made with BRUTAL - (c) Ahwoo Limited"u8);
            }
            ImGui.End();
            return;
        }

        DrawOverlay();
        if (!_showUi)
            return;
        BuildBattleWindow(io);
        BuildShipWindow(display);
    }

    void BuildBattleWindow(ImGuiIOPtr io)
    {
        ImGui.SetNextWindowPos(new float2(10, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(560, 0), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Bataille (F1)"u8))
        {
            for (var i = 0; i < Scenarios.Names.Length; i++)
                if (ImGui.RadioButton(Scenarios.Names[i], _scenario == i))
                {
                    _scenario = i;
                    Restart();
                }
            if (_scenario == Scenarios.SystemBattle)
            {
                ImGui.SetNextItemWidth(260);
                ImGui.SliderInt("Flotte (x 25 coques par camp)"u8, ref _fleetScale, 1, 8, "x%d"u8, ImGuiSliderFlags.None);
            }
            ImGui.SetNextItemWidth(120);
            ImGui.InputInt("Graine"u8, ref _seed, 1, 10, ImGuiInputTextFlags.None);
            ImGui.SameLine();
            if (ImGui.Button("Relancer (R)"u8))
                Restart();

            ImGui.Separator();
            ImGui.Checkbox("Pause (Espace)"u8, ref _paused);
            ImGui.SameLine();
            if (ImGui.Button("Pas (.)"u8))
                _stepOnce = true;
            ImGui.SliderFloat("Vitesse (+/-)"u8, ref _speed, 0.125f, 64f, "x%.2f"u8, ImGuiSliderFlags.Logarithmic);

            var w = _world;
            var lagging = !_paused && w.Result is null && _effectiveSpeed < _speed * 0.9;
            ImGui.Text($"Temps {TimeSpan.FromSeconds(w.Time):hh\\:mm\\:ss}   vitesse effective x{_effectiveSpeed:F1}{(lagging ? " (la machine ne suit plus)" : "")}");
            ImGui.TextDisabled($"sim {_stepMs:F2} ms/pas   rendu {_frameMs:F1} ms   {io.Framerate:F0} FPS   {_renderer.LastInstances} instances, {_renderer.LastLines} segments");
            if (w.Result is { } result)
                ImGui.TextColored(new float4(1f, 0.85f, 0.3f, 1f), $"Fin : victoire {(result.Winner == Side.Coalition ? "de la Coalition" : "des Clans")} ({result.Reason}) à {TimeSpan.FromSeconds(result.Time):hh\\:mm\\:ss}");

            foreach (var side in (ReadOnlySpan<Side>)[Side.Coalition, Side.Clans])
            {
                int active = 0, total = 0, cruising = 0;
                float hull = 0, hullMax = 0;
                foreach (ref readonly var s in w.Ships.AsSpan())
                {
                    if (s.Side != side || s.ClassId == ShipClassId.Depot)
                        continue;
                    total++;
                    if (s.IsActive)
                        active++;
                    if (s.IsActive && s.Cruise)
                        cruising++;
                    hull += s.Alive ? MathF.Max(0, s.Hull) : 0;
                    hullMax += s.HullMax;
                }
                var color = side == Side.Coalition ? new float4(0.55f, 0.75f, 1f, 1f) : new float4(1f, 0.6f, 0.35f, 1f);
                string line = $"{(side == Side.Coalition ? "Coalition" : "Clans")} : {active}/{total} coques, coque {100 * hull / MathF.Max(1, hullMax):F0} %%{(cruising > 0 ? $", {cruising} en croisière" : "")}";
                ImGui.TextColored(color, line);
            }
            var dronesAlive = 0;
            foreach (ref readonly var d in w.Drones.AsSpan())
                if (d.Alive)
                    dronesAlive++;
            ImGui.Text($"Drones en vol {dronesAlive}   lancés {w.Stats.DronesLaunched}   abattus par la DCA {w.Stats.DronesLostToPd}   projectiles {w.Projectiles.Count}");

            if (w.Ships.Count > 40 && ImGui.CollapsingHeader("Classes"u8))
                DrawClassSummary();
            if (ImGui.CollapsingHeader("Journal"u8))
                foreach (var line in _feed)
                    ImGui.Text(line);
            if (ImGui.CollapsingHeader("Affichage"u8))
            {
                ImGui.Checkbox("Icônes tactiques (G)"u8, ref _showIcons);
                ImGui.SameLine();
                ImGui.Checkbox("Étiquettes"u8, ref _showLabels);
                ImGui.SameLine();
                ImGui.Checkbox("Grille"u8, ref _showGrid);
                ImGui.SameLine();
                ImGui.Checkbox("Portée des puits"u8, ref _showReach);
                var melee = _follow == Follow.Melee;
                if (ImGui.Checkbox("Suivre la mêlée"u8, ref melee))
                    _follow = melee ? Follow.Melee : Follow.None;
                var post = _renderer.Post;
                ImGui.Checkbox("Bloom"u8, ref post.BloomEnabled);
                ImGui.SliderFloat("Exposition"u8, ref post.Exposure, 0.1f, 4f);
                ImGui.SliderFloat("Bloom force"u8, ref post.BloomStrength, 0f, 3f);
                ImGui.SliderFloat("Étoile"u8, ref _renderer.SunIntensity, 0f, 4f);
                ImGui.SliderFloat("Ambiance"u8, ref _renderer.AmbientIntensity, 0f, 5f);
            }
            ImGui.TextColored(new float4(0.6f, 0.6f, 0.6f, 1f), "Clic : sélectionner. Double-clic ou F : suivre. Glisser : tourner."u8);
            ImGui.TextColored(new float4(0.6f, 0.6f, 0.6f, 1f), "Clic droit : déplacer. Molette : zoom. Origine : toute la bataille. Échap : lâcher."u8);
        }
        ImGui.End();
    }

    /// <summary>Grandes batailles : par classe, coques en jeu et coque restante.</summary>
    void DrawClassSummary()
    {
        Span<int> total = stackalloc int[GameData.Classes.Length];
        Span<int> active = stackalloc int[GameData.Classes.Length];
        Span<float> hull = stackalloc float[GameData.Classes.Length];
        Span<float> hullMax = stackalloc float[GameData.Classes.Length];
        foreach (ref readonly var s in _world.Ships.AsSpan())
        {
            var c = (int)s.ClassId;
            total[c]++;
            if (s.IsActive)
                active[c]++;
            hull[c] += s.Alive ? MathF.Max(0, s.Hull) : 0;
            hullMax[c] += s.HullMax;
        }
        for (var c = 0; c < total.Length; c++)
        {
            if (total[c] == 0)
                continue;
            var cls = GameData.Classes[c];
            var color = cls.Side == Side.Coalition ? new float4(0.6f, 0.78f, 1f, 1f) : new float4(1f, 0.65f, 0.4f, 1f);
            string line = $"{cls.LabelFr,-22} {active[c],4} / {total[c],-4} en jeu   coque {100 * hull[c] / MathF.Max(1, hullMax[c]),3:F0} %%";
            ImGui.TextColored(color, line);
        }
    }

    /// <summary>Fiche du vaisseau sélectionné : état, boucliers, sous-systèmes, armes, ordre, groupe, bilan.</summary>
    void BuildShipWindow(float2 display)
    {
        if (SelectedIndex is not (var si and >= 0))
            return;
        ImGui.SetNextWindowPos(new float2(display.X - 10, 10), ImGuiCond.FirstUseEver, new float2(1, 0));
        ImGui.SetNextWindowSize(new float2(470, 0), ImGuiCond.FirstUseEver);
        ref readonly var s = ref _world.Ships[si];
        var cls = s.Class;
        var sideColor = s.Side == Side.Coalition ? new float4(0.6f, 0.78f, 1f, 1f) : new float4(1f, 0.65f, 0.4f, 1f);
        if (ImGui.Begin("Vaisseau"u8))
        {
            ImGui.TextColored(sideColor, $"{s.Name} - {cls.LabelFr}");
            var state = !s.Alive ? "détruit" : s.Withdrawn ? "parti" : s.Dying is not null ? "à l'agonie" : s.Cruise ? "en croisière" : "au combat";
            ImGui.Text($"{(s.Side == Side.Coalition ? "Coalition" : "Clans")}, {cls.Role}, {state}");
            ImGui.Text($"Vitesse {Vec.Len(s.Vel):F0} m/s   position ({s.Pos.X / 1000:F1}, {s.Pos.Y / 1000:F1}, {s.Pos.Z / 1000:F1}) km");
            if (_world.ShipIndex(s.Orders.Count > 0 ? s.Orders[0].TargetId : 0) is var oi and >= 0)
                ImGui.Text($"Distance à la cible {Km(Vec.Dist(s.Pos, _world.Ships[oi].Pos))}");
            if (ImGui.Button("Suivre (F)"u8))
                FocusSelected();
            ImGui.SameLine();
            if (ImGui.Button("Lâcher (Échap)"u8))
                _selectedId = 0;

            ImGui.Separator();
            // ImGui.Text avec une chaîne interpolée n'est pas formaté (pas de %% à doubler), TextColored(string) l'est.
            ImGui.Text($"Coque {MathF.Max(0, s.Hull):F0} / {s.HullMax:F0}  ({100 * MathF.Max(0, s.Hull) / s.HullMax:F0} %)");
            var faceMax = Combat.FaceMax(s, Face.Front);
            ImGui.Text($"Boucliers  avant {s.Faces[(int)Face.Front]:F0}  arrière {s.Faces[(int)Face.Back]:F0}  bâbord {s.Faces[(int)Face.Port]:F0}  tribord {s.Faces[(int)Face.Starboard]:F0}");
            ImGui.Text($"           dorsal {s.Faces[(int)Face.Dorsal]:F0}  ventral {s.Faces[(int)Face.Ventral]:F0}");
            ImGui.TextDisabled($"  (max {faceMax:F0} par face{(s.ShieldLock > 0 ? $", verrouillés {s.ShieldLock:F1} s" : "")})");
            string subs = "Sous-systèmes ";
            foreach (var sub in Enum.GetValues<Subsystem>())
            {
                var ss = s.Subsystems[(int)sub];
                subs += $" {sub} {ss.Hp:F0}/{ss.Max:F0}{(ss.Offline ? " HS" : "")}";
            }
            ImGui.Text(subs);

            ImGui.Separator();
            for (var w = 0; w < s.WeaponCount; w++)
            {
                ref readonly var ws = ref s.Weapons[w];
                var def = cls.Weapons[ws.DefIndex];
                var target = ws.TargetId != 0 && _world.ShipIndex(ws.TargetId) is var t and >= 0 ? _world.Ships[t].Name : "-";
                string line = $"{def.Id,-11} {def.Kind,-8} portée {Km(def.Range),-7} recharge {Math.Max(0, ws.Cooldown),4:F1} s  cible {target}";
                ImGui.Text(line);
            }
            if (s.HasHangar)
            {
                var flying = 0;
                foreach (ref readonly var d in _world.Drones.AsSpan())
                    if (d.Alive && d.CarrierId == s.Id)
                        flying++;
                ImGui.Text($"Hangar : {s.Hangar.Stock} en soute, {flying} en vol, mode {s.Hangar.Mode}");
            }
            if (s.EmpAmmo > 0)
                ImGui.Text($"Bombe à énergie : {s.EmpAmmo}");

            ImGui.Separator();
            if (s.Orders.Count > 0)
            {
                var o = s.Orders[0];
                var target = o.TargetId != 0 ? ShipLabel(o.TargetId) : "-";
                ImGui.Text($"Ordre {o.Type}  cible {target}{(o.Distance is { } dist ? $"  à {Km(dist)}" : "")}{(o.Subsystem is { } sub ? $"  visée {sub}" : "")}");
            }
            else
            {
                ImGui.Text("Aucun ordre"u8);
            }
            if (FleetSystem.GroupOf(_world.Fleet(s.Side), s.Id) is { } g)
            {
                int alive = 0;
                float hull = 0, hullMax = 0;
                foreach (var id in g.Members)
                    if (_world.ShipIndex(id) is var gi and >= 0)
                    {
                        if (_world.Ships[gi].IsActive)
                            alive++;
                        hull += MathF.Max(0, _world.Ships[gi].Hull);
                        hullMax += _world.Ships[gi].HullMax;
                    }
                var gt = g.TargetId != 0 ? ShipLabel(g.TargetId) : g.AutoTarget ? "automatique" : "-";
                ImGui.Text($"Groupe « {g.Name} » : {alive} en jeu, coque {100 * hull / MathF.Max(1, hullMax):F0} %");
                ImGui.Text($"  formation {g.Formation}, cible {gt}");
            }
            else
            {
                ImGui.Text("Détachée de son groupe"u8);
            }
            ImGui.Text($"Bilan : {s.Stats.DamageDealt:F0} dégâts infligés, {s.Stats.Kills} victimes");
        }
        ImGui.End();
    }

    /// <summary>
    /// Par-dessus la scène, sous les fenêtres : icônes tactiques des vaisseaux trop petits pour être vus, étiquettes
    /// des vaisseaux proches, noms des astres et des puits.
    /// </summary>
    void DrawOverlay()
    {
        var draw = ImGui.GetBackgroundDrawList(ImGui.GetMainViewport());
        foreach (ref readonly var g in _world.Wells.AsSpan())
        {
            // Un astre qui remplit l'écran (la géante sous la bataille) n'a pas besoin d'étiquette.
            if (g.Planet && _renderer.PixelRadius(g.Pos, g.Core) > 400)
                continue;
            var at = g.Pos + new float3(0, g.Core * (g.Planet ? 1.02f : 1.5f), 0);
            if (_renderer.Project(at) is { } p)
                draw.AddText(p, g.Planet ? Col(1f, 0.85f, 0.7f, 0.8f) : Col(0.8f, 0.6f, 1f), _world.WellName(g));
        }

        var selected = SelectedIndex;
        var group = selected >= 0 ? FleetSystem.GroupOf(_world.Fleet(_world.Ships[selected].Side), _world.Ships[selected].Id) : null;
        var grouped = _showIcons ? DrawSquadronMarkers(draw, group) : null;
        var labelled = 0;
        for (var i = 0; i < _world.Ships.Count && i < _screen.Length; i++)
        {
            if (!_onScreen[i] || (grouped is not null && grouped[i] && i != selected))
                continue;
            ref readonly var s = ref _world.Ships[i];
            var p = _screen[i];
            var px = _renderer.PixelRadius(s.Pos, s.Class.Radius);
            var inGroup = group is not null && group.Members.Contains(s.Id);
            if (_showIcons && px < IconBelowPixels)
                DrawIcon(draw, p, s, i == selected, inGroup);
            else if (i == selected)
                draw.AddCircle(p, MathF.Max(10, px * 1.6f), Col(1, 1, 1), 32, 1.5f);

            var label = i == selected || (_showLabels && px >= 12 && labelled < 30);
            if (!label)
                continue;
            labelled++;
            var text = s.Side == Side.Coalition ? Col(0.65f, 0.8f, 1f, 0.9f) : Col(1f, 0.7f, 0.45f, 0.9f);
            var top = p + new float2(-30, -MathF.Max(14, px) - 26);
            draw.AddText(top, text, s.Name);
            var hull = Math.Clamp(s.Hull / MathF.Max(1, s.HullMax), 0, 1);
            float shields = 0;
            for (var f = 0; f < 6; f++)
                shields += s.Faces[f];
            var shield = Math.Clamp(shields / (6 * MathF.Max(1, s.Class.ShieldFace)), 0, 1);
            var a = top + new float2(0, 16);
            draw.AddLine(a, a + new float2(60, 0), Col(0.15f, 0.15f, 0.15f, 0.8f), 4);
            draw.AddLine(a, a + new float2(60 * hull, 0), s.Dying is not null ? Col(0.5f, 0.5f, 0.5f) : Col(0.3f, 0.9f, 0.4f), 4);
            var b = a + new float2(0, 5);
            draw.AddLine(b, b + new float2(60 * shield, 0), Col(0.35f, 0.7f, 1f), 2);
        }
    }

    bool[] _grouped = [];

    /// <summary>
    /// Très loin, les icônes d'une escadre se confondent : quand toutes celles qui sont à l'écran tiennent dans
    /// quelques pixels, un seul marqueur d'escadre (cadre et effectif) les remplace. Rend, par coque, si elle est
    /// représentée par un marqueur.
    /// </summary>
    bool[] DrawSquadronMarkers(ImDrawListPtr draw, Group? selectedGroup)
    {
        if (_grouped.Length < _world.Ships.Count)
            _grouped = new bool[_world.Ships.Count];
        Array.Clear(_grouped);
        foreach (var side in (ReadOnlySpan<Side>)[Side.Coalition, Side.Clans])
            foreach (var g in _world.Fleet(side).Groups)
            {
                var sum = float2.Zero;
                var n = 0;
                foreach (var id in g.Members)
                    if (_world.ShipIndex(id) is var i and >= 0 && i < _onScreen.Length && _onScreen[i] && _world.Ships[i].IsActive)
                    {
                        sum += _screen[i];
                        n++;
                    }
                if (n < 2)
                    continue;
                var center = sum / n;
                var spread = 0f;
                foreach (var id in g.Members)
                    if (_world.ShipIndex(id) is var i and >= 0 && i < _onScreen.Length && _onScreen[i] && _world.Ships[i].IsActive)
                    {
                        var d = _screen[i] - center;
                        spread = MathF.Max(spread, MathF.Sqrt(d.X * d.X + d.Y * d.Y));
                    }
                if (spread > 10)
                    continue;
                foreach (var id in g.Members)
                    if (_world.ShipIndex(id) is var i and >= 0 && i < _grouped.Length)
                        _grouped[i] = true;
                var color = side == Side.Coalition ? Col(0.45f, 0.7f, 1f, 0.95f) : Col(1f, 0.55f, 0.25f, 0.95f);
                var half = new float2(9, 6);
                Poly(draw, color, center - half, center + new float2(half.X, -half.Y), center + half, center + new float2(-half.X, half.Y));
                draw.AddLine(center + new float2(-half.X, 0), center + new float2(half.X, 0), color, 1);
                if (ReferenceEquals(g, selectedGroup))
                    draw.AddCircle(center, 14, Col(1f, 0.9f, 0.3f, 0.9f), 20, 1.5f);
                draw.AddText(center + new float2(11, -7), color, $"{n}");
            }
        return _grouped;
    }

    /// <summary>Icône tactique : losange (capital), carré (porteur, station), triangle (raider), cercle (escorte, ligne).</summary>
    static void DrawIcon(ImDrawListPtr draw, float2 p, in Ship s, bool selected, bool inGroup)
    {
        var dying = s.Dying is not null;
        var color = dying ? Col(0.5f, 0.5f, 0.5f, 0.7f)
            : s.Side == Side.Coalition ? Col(0.45f, 0.7f, 1f, 0.95f) : Col(1f, 0.55f, 0.25f, 0.95f);
        var role = s.Class.Role;
        var r = role is Role.Capital or Role.Carrier or Role.Depot ? 5f : 3.5f;
        switch (role)
        {
            case Role.Capital:
                Poly(draw, color, p + new float2(0, -r), p + new float2(r, 0), p + new float2(0, r), p + new float2(-r, 0));
                break;
            case Role.Carrier:
            case Role.Depot:
                Poly(draw, color, p + new float2(-r, -r), p + new float2(r, -r), p + new float2(r, r), p + new float2(-r, r));
                break;
            case Role.Brawler:
                Poly(draw, color, p + new float2(0, -r), p + new float2(r, r * 0.8f), p + new float2(-r, r * 0.8f));
                break;
            default:
                draw.AddCircle(p, r, color, 8, 1.5f);
                break;
        }
        if (s.Cruise)
            draw.AddLine(p + new float2(-r - 3, r + 3), p + new float2(r + 3, r + 3), color, 1);
        if (selected)
            draw.AddCircle(p, r + 6, Col(1, 1, 1), 24, 1.5f);
        else if (inGroup)
            draw.AddCircle(p, r + 4, Col(1f, 0.9f, 0.3f, 0.9f), 16, 1);
    }

    static void Poly(ImDrawListPtr draw, ImColor8 color, params ReadOnlySpan<float2> points)
    {
        for (var i = 0; i < points.Length; i++)
            draw.AddLine(points[i], points[(i + 1) % points.Length], color, 1.5f);
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _openGlBackend.Dispose();
        _glfwBackend.Dispose();
        _window.Dispose();
    }
}
