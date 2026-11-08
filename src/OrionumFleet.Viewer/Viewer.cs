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
}

/// <summary>
/// Visualiseur de débug de la sim : la bataille tourne en direct (pas fixe de 0,05 s, accéléré ou figé), rendue
/// en HDR avec le bloom de Brutal Arena. Formes simples : pavés orientés pour les coques, sphères noires pour
/// les cœurs des puits, traits lumineux pour les tirs. Ce n'est pas la direction artistique du jeu.
/// </summary>
sealed class Viewer : IDisposable
{
    const float SplashSeconds = 2f;
    const int MaxStepsPerFrame = 400;

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
    int _scenario;
    int _seed = 1;
    bool _paused;
    float _speed = 1;
    bool _stepOnce;
    double _accumulator;
    double _stepMs;
    double _startTime;

    // Caméra orbitale autour d'un point visé.
    float3 _target;
    float _yaw = 0.6f;
    float _pitch = 0.75f;
    float _distance = 4200;
    bool _follow = true;

    bool _showUi = true;
    bool _showLabels = true;
    bool _showReach = true;
    bool _showGrid = true;

    readonly List<Effect> _effects = new(512);
    readonly List<string> _feed = [];

    // Mode capture (--capture) : avance rapide jusqu'à --at secondes de sim, une image PNG, puis on quitte.
    readonly string? _capturePath;
    readonly double _captureAt;
    int _captureFrames;

    /// <summary>Options : --scenario n, --seed n, --capture fichier.png, --at secondes, --yaw/--pitch/--distance.</summary>
    public Viewer(string[] args)
    {
        string? Arg(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        float FloatArg(string name, float fallback) =>
            float.TryParse(Arg(name), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        _scenario = Math.Clamp((int)FloatArg("--scenario", 0), 0, Scenarios.Names.Length - 1);
        _seed = (int)FloatArg("--seed", 1);
        _capturePath = Arg("--capture");
        _captureAt = FloatArg("--at", 60);
        _yaw = FloatArg("--yaw", _yaw);
        _pitch = FloatArg("--pitch", _pitch);
        _distance = FloatArg("--distance", _distance);

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
    }

    void Restart()
    {
        _world = Scenarios.Create(_scenario, (uint)Math.Max(1, _seed));
        _effects.Clear();
        _feed.Clear();
        _accumulator = 0;
        _target = Centroid() ?? float3.Zero;
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
            {
                // Avance à pleine vitesse jusqu'à l'instant voulu ; les effets des dernières secondes restent visibles.
                while (_world.Time < _captureAt && _world.Result is null)
                {
                    _world.Step();
                    foreach (var e in _world.DrainEvents())
                        OnEvent(e);
                }
                _effects.RemoveAll(fx => _world.Time - fx.Start > fx.Life);
                if (_captureFrames == 0)
                    _target = Centroid() ?? _target;
            }
            else if (!splash)
                Advance(frameTime);

            _glfwBackend.NewFrame();
            _openGlBackend.NewFrame();
            ImGui.NewFrame();
            ImString.SharedStorage.Reset();
            if (!capturing)
                UpdateCamera((float)frameTime);

            var drawn = RenderScene();
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
        if (_stepOnce)
        {
            steps = 1;
            _stepOnce = false;
        }
        else if (!_paused && _world.Result is null)
        {
            _accumulator += frameTime * _speed;
            steps = (int)Math.Min(MaxStepsPerFrame, Math.Floor(_accumulator / World.DtExact));
            _accumulator = Math.Min(_accumulator - steps * World.DtExact, World.DtExact);
        }
        if (steps == 0)
            return;

        var clock = Stopwatch.StartNew();
        for (var i = 0; i < steps && _world.Result is null; i++)
        {
            _world.Step();
            foreach (var e in _world.DrainEvents())
                OnEvent(e);
        }
        _stepMs = clock.Elapsed.TotalMilliseconds / steps;
        _effects.RemoveAll(fx => _world.Time - fx.Start > fx.Life);
    }

    void OnEvent(in SimEvent e)
    {
        var glow = e.Side == Side.Coalition ? CoalitionGlow : ClansGlow;
        switch (e.Type)
        {
            case SimEventType.Beam:
                var beamColor = e.Beam == BeamKind.Pd ? glow * 1.5f : glow * 5f;
                AddEffect(e.Type, e.Beam == BeamKind.Pd ? 0.08f : 0.3f, e.At, e.Vector, 0, beamColor);
                break;
            case SimEventType.Explosion:
                AddEffect(e.Type, 0.7f, e.At, default, MathF.Max(6, e.Value * 2.5f), new float3(1f, 0.6f, 0.25f));
                break;
            case SimEventType.Secondary:
                AddEffect(SimEventType.Explosion, 0.8f, e.At, default, MathF.Max(10, e.Value * 3), new float3(1f, 0.5f, 0.2f));
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
                AddEffect(e.Type, 0.6f, e.At, default, 6 + e.Value * 2, new float3(0.7f, 0.25f, 1f));
                break;
            case SimEventType.ShipCrippled:
                Feed($"{_world.Time:F1} s  {ShipLabel(e.ShipId)} hors de combat (par {ShipLabel(e.OtherId)})");
                break;
            case SimEventType.ShipDestroyed:
                Feed($"{_world.Time:F1} s  {ShipLabel(e.ShipId)} détruit");
                break;
        }
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

    // --- Caméra et entrées -----------------------------------------------------------------

    void UpdateCamera(float dt)
    {
        var io = ImGui.GetIO();
        if (!io.WantCaptureMouse)
        {
            var delta = io.MouseDelta;
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _yaw -= delta.X * 0.005f;
                _pitch = Math.Clamp(_pitch + delta.Y * 0.005f, -1.45f, 1.45f);
            }
            if (ImGui.IsMouseDown(ImGuiMouseButton.Right) || ImGui.IsMouseDown(ImGuiMouseButton.Middle))
            {
                _follow = false;
                var (right, forward) = (new float3(MathF.Cos(_yaw), 0, -MathF.Sin(_yaw)), new float3(MathF.Sin(_yaw), 0, MathF.Cos(_yaw)));
                var k = _distance * 0.0015f;
                _target += right * (-delta.X * k) + forward * (-delta.Y * k);
            }
            if (io.MouseWheel != 0)
                _distance = Math.Clamp(_distance * MathF.Pow(0.88f, io.MouseWheel), 150, 20000);
        }
        if (_follow && Centroid() is { } c)
            _target = float3.Lerp(_target, c, 1 - MathF.Exp(-dt * 1.5f));
    }

    float3 Eye => _target + new float3(MathF.Sin(_yaw) * MathF.Cos(_pitch), MathF.Sin(_pitch), MathF.Cos(_yaw) * MathF.Cos(_pitch)) * _distance;

    void OnKey(GlfwWindow window, GlfwKey key, int scancode, GlfwKeyAction action, GlfwModifier mods)
    {
        if (action != GlfwKeyAction.Press || ImGui.GetIO().WantCaptureKeyboard)
            return;
        switch (key)
        {
            case GlfwKey.Escape:
                window.ShouldClose = true;
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
                _follow = true;
                break;
            case GlfwKey.F1:
                _showUi = !_showUi;
                break;
            case GlfwKey.KpAdd:
            case GlfwKey.Equal:
                _speed = MathF.Min(32, _speed * 2);
                break;
            case GlfwKey.KpSubtract:
            case GlfwKey.Minus:
                _speed = MathF.Max(0.125f, _speed / 2);
                break;
        }
    }

    // --- Rendu -----------------------------------------------------------------------------

    /// <returns>false si la fenêtre est réduite et que rien n'a été dessiné.</returns>
    bool RenderScene()
    {
        Glfw.PInvoke.GetFramebufferSize(_window, out var width, out var height);
        if (width == 0 || height == 0)
            return false;

        var w = _world;
        FillLights();
        _renderer.BeginFrame(new int2(width, height), Eye, _target);

        if (_showGrid)
            DrawGrid();
        DrawWells();
        DrawShips();
        DrawProjectiles();
        DrawEffects();

        _renderer.EndFrame();
        return true;
    }

    void FillLights()
    {
        var lights = _renderer.Lights;
        lights.Clear();
        foreach (ref readonly var g in _world.Wells.AsSpan())
            lights.Add(g.Pos, g.Core * 4, new float3(0.6f, 0.2f, 1f), 1.2f);
        // Les explosions les plus récentes d'abord.
        for (var i = _effects.Count - 1; i >= 0 && lights.Count < LightBuffer.MaxLights; i--)
        {
            var fx = _effects[i];
            if (fx.Type is not (SimEventType.Explosion or SimEventType.Blast or SimEventType.CoreBreach))
                continue;
            var t = (float)((_world.Time - fx.Start) / fx.Life);
            lights.Add(fx.At, fx.Size * 8 + 150, new float3(1f, 0.6f, 0.3f), 6 * (1 - t));
        }
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

    void DrawWells()
    {
        foreach (ref readonly var g in _world.Wells.AsSpan())
        {
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
                // Tuyère : brille avec la poussée.
                var engine = s.Pos - fwd * (r * 1.05f);
                _renderer.DrawSphere(engine, r * 0.17f, glow, dying ? 0.3f : 1.5f + s.Thrust * 6);
                // Nez : repère de cap.
                _renderer.Line(s.Pos + fwd * r, s.Pos + fwd * (r * 1.6f), glow * 2);
            }

            if (dying)
            {
                var flicker = 0.5f + 0.5f * MathF.Sin((float)_world.Time * 23 + s.Id);
                _renderer.DrawSphere(s.Pos, r * 0.3f, new float3(1f, 0.45f, 0.15f), 3 + 5 * flicker);
            }
        }
    }

    void DrawProjectiles()
    {
        foreach (ref readonly var p in _world.Projectiles.AsSpan())
        {
            if (!p.Alive)
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
        foreach (var fx in _effects)
        {
            var t = Math.Clamp((float)((_world.Time - fx.Start) / fx.Life), 0, 1);
            var fade = 1 - t;
            switch (fx.Type)
            {
                case SimEventType.Beam:
                    _renderer.Line(fx.At, fx.To, fx.Color * fade);
                    break;
                case SimEventType.Explosion:
                case SimEventType.ShieldHit:
                case SimEventType.Swallowed:
                    _renderer.DrawSphere(fx.At, fx.Size * (0.4f + 0.8f * t), fx.Color, 8 * fade * fade);
                    break;
                case SimEventType.Blast:
                case SimEventType.CoreBreach:
                case SimEventType.EmpBlast:
                    _renderer.DrawSphere(fx.At, fx.Size * 0.25f * (1 - t * 0.5f), fx.Color, 10 * fade * fade);
                    _renderer.Circle(fx.At, fx.Size * (0.3f + 0.7f * t), fx.Color * (4 * fade), 72);
                    break;
            }
        }
    }

    // --- UI --------------------------------------------------------------------------------

    static ImColor8 Col(float r, float g, float b, float a = 1)
    {
        var c = new float4(r, g, b, a);
        return ImGui.GetColorU32(in c);
    }

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

        if (_showLabels)
            DrawLabels();
        if (!_showUi)
            return;

        ImGui.SetNextWindowPos(new float2(10, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(600, 0), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Bataille (F1)"u8))
        {
            for (var i = 0; i < Scenarios.Names.Length; i++)
                if (ImGui.RadioButton(Scenarios.Names[i], _scenario == i))
                {
                    _scenario = i;
                    Restart();
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
            ImGui.SliderFloat("Vitesse (+/-)"u8, ref _speed, 0.125f, 32f, "x%.2f"u8, ImGuiSliderFlags.Logarithmic);

            var w = _world;
            ImGui.Text($"Temps {w.Time:F1} s   pas {w.Tick}   {_stepMs:F3} ms/pas   {io.Framerate:F0} FPS");
            if (w.Result is { } result)
                ImGui.TextColored(new float4(1f, 0.85f, 0.3f, 1f), $"Fin : victoire {(result.Winner == Side.Coalition ? "de la Coalition" : "des Clans")} ({result.Reason}) à {result.Time:F1} s");

            foreach (var side in (ReadOnlySpan<Side>)[Side.Coalition, Side.Clans])
            {
                int active = 0, total = 0;
                float hull = 0, hullMax = 0;
                foreach (ref readonly var s in w.Ships.AsSpan())
                {
                    if (s.Side != side || s.ClassId == ShipClassId.Depot)
                        continue;
                    total++;
                    if (s.IsActive)
                        active++;
                    hull += s.Alive ? s.Hull : 0;
                    hullMax += s.HullMax;
                }
                var color = side == Side.Coalition ? new float4(0.55f, 0.75f, 1f, 1f) : new float4(1f, 0.6f, 0.35f, 1f);
                ImGui.TextColored(color, $"{(side == Side.Coalition ? "Coalition" : "Clans")} : {active}/{total} coques, coque {100 * hull / MathF.Max(1, hullMax):F0} %%");
            }
            ImGui.Text($"Projectiles en vol {w.Projectiles.Count}   avalés {w.Stats.Swallowed}   tirs courbes {w.Stats.ArcShots.Coalition + w.Stats.ArcShots.Clans}   bombes {w.Stats.BombsDetonated}");

            if (ImGui.CollapsingHeader("Coques"u8, ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (ref readonly var s in w.Ships.AsSpan())
                {
                    if (!s.Alive)
                        continue;
                    float shields = 0;
                    for (var f = 0; f < 6; f++)
                        shields += s.Faces[f];
                    var shieldMax = 6 * MathF.Max(1, s.Class.ShieldFace);
                    var order = s.Orders.Count > 0 ? s.Orders[0].Type.ToString() : "-";
                    var color = s.Dying is not null ? new float4(0.5f, 0.5f, 0.5f, 1f)
                        : s.Side == Side.Coalition ? new float4(0.6f, 0.78f, 1f, 1f) : new float4(1f, 0.65f, 0.4f, 1f);
                    // ImGui.Text* lit le texte comme un format printf : les « % » sont doublés.
                    string line = $"{s.Name,-16} {s.Class.LabelFr,-18} coque {100 * s.Hull / MathF.Max(1, s.HullMax),3:F0} %%  boucliers {100 * shields / shieldMax,3:F0} %%  {order}";
                    ImGui.TextColored(color, line);
                }
            }

            if (ImGui.CollapsingHeader("Journal"u8, ImGuiTreeNodeFlags.DefaultOpen))
                foreach (var line in _feed)
                    ImGui.Text(line);

            if (ImGui.CollapsingHeader("Affichage"u8))
            {
                ImGui.Checkbox("Étiquettes"u8, ref _showLabels);
                ImGui.SameLine();
                ImGui.Checkbox("Portée des puits"u8, ref _showReach);
                ImGui.SameLine();
                ImGui.Checkbox("Grille"u8, ref _showGrid);
                ImGui.Checkbox("Suivre la mêlée (F)"u8, ref _follow);
                var post = _renderer.Post;
                ImGui.Checkbox("Bloom"u8, ref post.BloomEnabled);
                ImGui.SliderFloat("Exposition"u8, ref post.Exposure, 0.1f, 4f);
                ImGui.SliderFloat("Bloom force"u8, ref post.BloomStrength, 0f, 3f);
                ImGui.SliderFloat("Bloom seuil"u8, ref post.BloomThreshold, 0f, 4f);
                ImGui.SliderFloat("Soleil"u8, ref _renderer.SunIntensity, 0f, 3f);
                ImGui.SliderFloat("Ambiance"u8, ref _renderer.AmbientIntensity, 0f, 5f);
            }
            ImGui.TextColored(new float4(0.6f, 0.6f, 0.6f, 1f), "Clic gauche : tourner. Clic droit : déplacer. Molette : zoom."u8);
        }
        ImGui.End();
    }

    /// <summary>Nom, coque (vert) et boucliers (bleu) au-dessus de chaque coque, et nom des puits.</summary>
    void DrawLabels()
    {
        // Calque de fond : par-dessus la scène, mais sous les fenêtres ImGui.
        var draw = ImGui.GetBackgroundDrawList(ImGui.GetMainViewport());
        foreach (ref readonly var g in _world.Wells.AsSpan())
            if (_renderer.Project(g.Pos + new float3(0, g.Core * 1.5f, 0)) is { } p)
                draw.AddText(p, Col(0.8f, 0.6f, 1f), _world.WellName(g));

        foreach (ref readonly var s in _world.Ships.AsSpan())
        {
            if (!s.Alive || s.Withdrawn || _renderer.Project(s.Pos + new float3(0, s.Class.Radius * 1.3f, 0)) is not { } p)
                continue;
            var text = s.Side == Side.Coalition ? Col(0.65f, 0.8f, 1f, 0.9f) : Col(1f, 0.7f, 0.45f, 0.9f);
            draw.AddText(p + new float2(-30, -30), text, s.Name);
            var hull = Math.Clamp(s.Hull / MathF.Max(1, s.HullMax), 0, 1);
            float shields = 0;
            for (var f = 0; f < 6; f++)
                shields += s.Faces[f];
            var shield = Math.Clamp(shields / (6 * MathF.Max(1, s.Class.ShieldFace)), 0, 1);
            var a = p + new float2(-30, -12);
            draw.AddLine(a, a + new float2(60, 0), Col(0.15f, 0.15f, 0.15f, 0.8f), 4);
            draw.AddLine(a, a + new float2(60 * hull, 0), s.Dying is not null ? Col(0.5f, 0.5f, 0.5f) : Col(0.3f, 0.9f, 0.4f), 4);
            var b = a + new float2(0, 5);
            draw.AddLine(b, b + new float2(60 * shield, 0), Col(0.35f, 0.7f, 1f), 2);
        }
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _openGlBackend.Dispose();
        _glfwBackend.Dispose();
        _window.Dispose();
    }
}
