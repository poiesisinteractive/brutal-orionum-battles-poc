using System.Runtime.InteropServices;
using Brutal.Numerics;
using Brutal.OpenGlApi;
using OrionumFleet.Sim;

namespace OrionumFleet.Viewer.Rendering;

[StructLayout(LayoutKind.Sequential)]
record struct Vertex(float3 Position, float3 Normal);

/// <summary>Sommet d'une ligne lumineuse : position (relative à la caméra) et couleur HDR.</summary>
[StructLayout(LayoutKind.Sequential)]
record struct LineVertex(float3 Position, float3 Color);

/// <summary>Une instance de maillage : matrice du modèle (relative à la caméra, convention ligne de Brutal.Numerics) et couleur + émissif.</summary>
[StructLayout(LayoutKind.Sequential)]
struct Instance
{
    public float4x4 Model;
    /// <summary>rgb = couleur sRGB, a = multiplicateur émissif HDR (négatif : surface d'astre, voir le shader).</summary>
    public float4 ColorEmissive;
}

/// <summary>
/// Maillage GPU dessiné en instances : un VAO qui lie les sommets (attributs 0-1) et un tampon d'instances
/// (attributs 2-6, diviseur 1). Les instances de la frame s'accumulent côté CPU puis partent en un seul appel.
/// </summary>
sealed class Mesh
{
    public required GlVertexArray VertexArray { get; init; }
    public required GlBuffer VertexBuffer { get; init; }
    public required GlBuffer InstanceBuffer { get; init; }
    public required int VertexCount { get; init; }
    public Instance[] Instances = new Instance[1024];
    public int InstanceCount;

    public void Add(in float4x4 model, float3 color, float emissive)
    {
        if (InstanceCount == Instances.Length)
            Array.Resize(ref Instances, Instances.Length * 2);
        Instances[InstanceCount++] = new Instance { Model = model, ColorEmissive = new float4(color, emissive) };
    }
}

/// <summary>Style de surface d'un astre (shader) : bandes de géante gazeuse ou sol rocheux.</summary>
enum Surface
{
    GasGiant = 1,
    Rock = 2,
}

/// <summary>
/// Renderer forward OpenGL 4.1, repris de Brutal Arena et mis à l'échelle d'un système planétaire : HDR, une
/// lumière directionnelle (l'étoile), une ambiance et jusqu'à 64 lumières ponctuelles, puis bloom et
/// tonemapping. Les solides partent en instances (un appel par maillage et par frame) ; tirs, rayons et repères
/// sont des lignes émissives en additif.
///
/// Grande échelle : tout est dessiné <b>relativement à la caméra</b> (les positions du monde, à des centaines de
/// km de l'origine, perdraient leur précision dans une matrice de vue), et la profondeur est
/// <b>logarithmique</b> (de 0,5 m à 10⁹ m sans scintillement : un vaisseau de 30 m devant une planète de 5 000 km).
/// </summary>
sealed class Renderer : IDisposable
{
    public bool PointLightsEnabled = true;
    public float SunIntensity = 1.6f;
    public float AmbientIntensity = 1f;
    public float EmissiveIntensity = 1f;
    /// <summary>Lumière de l'étoile (naine rouge : chaude, orangée).</summary>
    public float3 SunColor = new(1f, 0.62f, 0.42f);
    public float3 SkyAmbient = new(0.03f, 0.035f, 0.06f);
    public float3 GroundAmbient = new(0.015f, 0.01f, 0.012f);
    public float3 SpaceColor = new(0.002f, 0.0025f, 0.006f);
    /// <summary>Direction vers l'étoile (unitaire).</summary>
    public float3 SunDirection = float3.Normalize(new float3(0.55f, 0.35f, -0.75f));

    const float Near = 0.5f;
    const float Far = 1e9f;

    readonly GlProgram _program;
    readonly GlProgram _lineProgram;
    readonly GlVertexArray _lineArray;
    readonly GlBuffer _lineBuffer;
    LineVertex[] _lines = new LineVertex[16384];
    int _lineCount;

    /// <summary>Vue-projection relative à la caméra (la caméra est à l'origine).</summary>
    public float4x4 ViewProjection { get; private set; }
    public int2 Viewport { get; private set; }
    /// <summary>Position de la caméra dans le monde : l'origine du rendu.</summary>
    public float3 Origin { get; private set; }
    /// <summary>Pixels par unité à une unité de distance (taille apparente : rayon × ceci / distance).</summary>
    public float PixelsPerRadian { get; private set; }

    public Mesh Cube { get; }
    public Mesh Sphere { get; }
    /// <summary>Sphère fine pour les astres (leur bord se voit de près).</summary>
    public Mesh Globe { get; }
    public LightBuffer Lights { get; } = new();
    public PostProcess Post { get; } = new();

    public Renderer()
    {
        _program = GlUtil.CreateProgram(SceneVertexShader, SceneFragmentShader);
        OpenGl.UniformBlockBinding(_program, OpenGl.GetUniformBlockIndex(_program, "Lights"u8), LightBuffer.BindingPoint);
        _lineProgram = GlUtil.CreateProgram(LineVertexShader, LineFragmentShader);

        Cube = Upload(BuildCube());
        Sphere = Upload(BuildSphere(16, 24));
        Globe = Upload(BuildSphere(128, 192));

        _lineArray = OpenGl.GenVertexArray().Bind();
        _lineBuffer = OpenGl.GenBuffer();
        _lineBuffer.Bind(GlBindTarget.ArrayBuffer);
        OpenGl.BufferData(GlBindTarget.ArrayBuffer, (ReadOnlySpan<LineVertex>)_lines, GlBufferUsage.StreamDraw);
        OpenGl.VertexAttribute(new GlAttribute(0, 3, OpenGl.GL_FLOAT, GlScaling.Float, 6 * sizeof(float), 0));
        OpenGl.VertexAttribute(new GlAttribute(1, 3, OpenGl.GL_FLOAT, GlScaling.Float, 6 * sizeof(float), 3 * sizeof(float)));
        OpenGl.EnableVertexAttribArray(0);
        OpenGl.EnableVertexAttribArray(1);
    }

    /// <summary>
    /// Prépare la frame : cible HDR, états GL (l'UI ImGui en modifie), caméra, éclairage. Les lumières ponctuelles
    /// doivent déjà être dans <see cref="Lights"/>, en coordonnées relatives (<see cref="Relative"/>).
    /// </summary>
    public void BeginFrame(int2 framebufferSize, float3 eye, float3 target)
    {
        Viewport = framebufferSize;
        Origin = eye;
        Post.BeginScene(framebufferSize, SpaceColor);

        OpenGl.Disable(GlCapability.Blend);
        OpenGl.Disable(GlCapability.ScissorTest);
        OpenGl.Enable(GlCapability.DepthTest);
        OpenGl.DepthMask(true);

        Lights.Upload(PointLightsEnabled ? Lights.Count : 0);

        var aspect = framebufferSize.Y > 0 ? framebufferSize.X / (float)framebufferSize.Y : 1f;
        const float fov = 50 * MathF.PI / 180;
        var forward = target - eye;
        var up = MathF.Abs(Vec.Dot(Vec.Normalize(forward), float3.UnitY)) > 0.999f ? float3.UnitZ : float3.UnitY;
        var view = float4x4.CreateLookAt(float3.Zero, forward, up);
        var projection = float4x4.CreatePerspectiveFieldOfView(fov, aspect, Near, Far);
        ViewProjection = view * projection;
        PixelsPerRadian = framebufferSize.Y * 0.5f / MathF.Tan(fov / 2);

        _program.Use();
        _program.Uniform("viewProjection"u8, ViewProjection);
        _program.Uniform("sunDirection"u8, SunDirection);
        _program.Uniform("sunColor"u8, SunColor * SunIntensity);
        _program.Uniform("skyAmbient"u8, SkyAmbient * AmbientIntensity);
        _program.Uniform("groundAmbient"u8, GroundAmbient * AmbientIntensity);
        _program.Uniform("emissiveScale"u8, EmissiveIntensity);
        _program.Uniform("logDepth"u8, 2f / MathF.Log2(Far + 1));
        _lineCount = 0;
    }

    /// <summary>Position relative à la caméra (tout ce qui part au GPU passe par là).</summary>
    public float3 Relative(float3 world) => world - Origin;

    /// <summary>Nombre d'instances solides et de segments de la dernière frame (panneau de débug).</summary>
    public int LastInstances { get; private set; }
    public int LastLines { get; private set; }

    /// <summary>Solides en instances, lignes en additif (sans écrire la profondeur), puis bloom + tonemapping.</summary>
    public void EndFrame()
    {
        _program.Use();
        LastInstances = 0;
        foreach (var mesh in (ReadOnlySpan<Mesh>)[Globe, Cube, Sphere])
        {
            if (mesh.InstanceCount == 0)
                continue;
            mesh.VertexArray.Bind();
            mesh.InstanceBuffer.Bind(GlBindTarget.ArrayBuffer);
            OpenGl.BufferData(GlBindTarget.ArrayBuffer, (ReadOnlySpan<Instance>)mesh.Instances.AsSpan(0, mesh.InstanceCount), GlBufferUsage.StreamDraw);
            OpenGl.Draw(GlTopology.Triangles, mesh.VertexCount, mesh.InstanceCount, 0, 0);
            LastInstances += mesh.InstanceCount;
            mesh.InstanceCount = 0;
        }
        LastLines = _lineCount / 2;

        if (_lineCount > 0)
        {
            OpenGl.Enable(GlCapability.Blend);
            OpenGl.BlendFunc(OpenGl.GL_ONE, OpenGl.GL_ONE);
            OpenGl.DepthMask(false);
            _lineProgram.Use();
            _lineProgram.Uniform("viewProjection"u8, ViewProjection);
            _lineProgram.Uniform("logDepth"u8, 2f / MathF.Log2(Far + 1));
            _lineArray.Bind();
            _lineBuffer.Bind(GlBindTarget.ArrayBuffer);
            OpenGl.BufferData(GlBindTarget.ArrayBuffer, (ReadOnlySpan<LineVertex>)_lines.AsSpan(0, _lineCount), GlBufferUsage.StreamDraw);
            OpenGl.Draw(GlTopology.Lines, _lineCount, 1, 0, 0);
            OpenGl.DepthMask(true);
            OpenGl.Disable(GlCapability.Blend);
        }
        Post.Resolve();
    }

    /// <summary>Une ligne émissive (couleur HDR linéaire), en coordonnées du monde.</summary>
    public void Line(float3 a, float3 b, float3 color) => Line(a, b, color, color);

    public void Line(float3 a, float3 b, float3 colorA, float3 colorB)
    {
        if (_lineCount + 2 > _lines.Length)
            Array.Resize(ref _lines, _lines.Length * 2);
        _lines[_lineCount++] = new LineVertex(a - Origin, colorA);
        _lines[_lineCount++] = new LineVertex(b - Origin, colorB);
    }

    /// <summary>Cercle horizontal (plan XZ) autour de <paramref name="center"/>.</summary>
    public void Circle(float3 center, float radius, float3 color, int segments = 64)
    {
        var prev = center + new float3(radius, 0, 0);
        for (var i = 1; i <= segments; i++)
        {
            var a = MathF.Tau * i / segments;
            var p = center + new float3(MathF.Cos(a) * radius, 0, MathF.Sin(a) * radius);
            Line(prev, p, color);
            prev = p;
        }
    }

    /// <summary>Projette un point du monde en pixels écran ; null s'il est derrière la caméra ou loin hors champ.</summary>
    public float2? Project(float3 world, float margin = 1.2f)
    {
        var clip = float4.Transform(world - Origin, ViewProjection);
        if (clip.W <= 1e-3f)
            return null;
        var ndc = new float2(clip.X / clip.W, clip.Y / clip.W);
        if (MathF.Abs(ndc.X) > margin || MathF.Abs(ndc.Y) > margin)
            return null;
        return new float2((ndc.X * 0.5f + 0.5f) * Viewport.X, (0.5f - ndc.Y * 0.5f) * Viewport.Y);
    }

    /// <summary>Taille apparente (pixels) d'un objet de rayon donné à une position du monde.</summary>
    public float PixelRadius(float3 world, float radius) => radius * PixelsPerRadian / MathF.Max(1e-3f, Vec.Len(world - Origin));

    /// <param name="model">Matrice du modèle en coordonnées du monde (sa translation est ramenée à la caméra).</param>
    /// <param name="emissive">Multiplicateur HDR de la couleur propre de l'objet : au-delà de ~1, il « bloome ».</param>
    public void DrawBox(in float4x4 model, float3 color, float emissive = 0) => Cube.Add(ToCamera(model), color, emissive);

    public void DrawSphere(float3 position, float radius, float3 color, float emissive = 0) =>
        Sphere.Add(float4x4.CreateScale(radius) * float4x4.CreateTranslation(position - Origin), color, emissive);

    /// <summary>Un astre : sphère fine, surface procédurale (bandes ou roche) et liseré d'atmosphère.</summary>
    public void DrawGlobe(float3 position, float radius, float3 color, Surface surface) =>
        Globe.Add(float4x4.CreateScale(radius) * float4x4.CreateTranslation(position - Origin), color, -(float)surface);

    float4x4 ToCamera(in float4x4 model)
    {
        var m = model;
        m.W = new float4(m.W.X - Origin.X, m.W.Y - Origin.Y, m.W.Z - Origin.Z, m.W.W);
        return m;
    }

    static unsafe Mesh Upload(Vertex[] vertices)
    {
        var vertexArray = OpenGl.GenVertexArray().Bind();
        var vertexBuffer = OpenGl.GenBuffer();
        vertexBuffer.Bind(GlBindTarget.ArrayBuffer);
        OpenGl.BufferData(GlBindTarget.ArrayBuffer, vertices, GlBufferUsage.StaticDraw);
        OpenGl.VertexAttribute(new GlAttribute(0, 3, OpenGl.GL_FLOAT, GlScaling.Float, 6 * sizeof(float), 0));
        OpenGl.VertexAttribute(new GlAttribute(1, 3, OpenGl.GL_FLOAT, GlScaling.Float, 6 * sizeof(float), 3 * sizeof(float)));
        OpenGl.EnableVertexAttribArray(0);
        OpenGl.EnableVertexAttribArray(1);

        // Tampon d'instances : la matrice occupe les attributs 2 à 5 (une colonne GLSL chacun), la couleur le 6.
        var instanceBuffer = OpenGl.GenBuffer();
        instanceBuffer.Bind(GlBindTarget.ArrayBuffer);
        var stride = sizeof(Instance);
        for (uint a = 0; a < 5; a++)
        {
            OpenGl.VertexAttribute(new GlAttribute(2 + a, 4, OpenGl.GL_FLOAT, GlScaling.Float, stride, (int)(a * 4 * sizeof(float))));
            OpenGl.EnableVertexAttribArray(2 + a);
            OpenGl.VertexAttribDivisor(2 + a, 1);
        }
        return new Mesh { VertexArray = vertexArray, VertexBuffer = vertexBuffer, InstanceBuffer = instanceBuffer, VertexCount = vertices.Length };
    }

    /// <summary>Cube de -1 à 1, deux triangles par face, normales par face.</summary>
    static Vertex[] BuildCube()
    {
        var vertices = new List<Vertex>(36);
        float2[] corners = [new(-1, -1), new(1, -1), new(1, 1), new(-1, -1), new(1, 1), new(-1, 1)];
        foreach (var normal in new[] { float3.UnitX, -float3.UnitX, float3.UnitY, -float3.UnitY, float3.UnitZ, -float3.UnitZ })
        {
            var u = new float3(normal.Y, normal.Z, normal.X);
            var v = float3.Cross(normal, u);
            foreach (var corner in corners)
                vertices.Add(new Vertex(normal + u * corner.X + v * corner.Y, normal));
        }
        return [.. vertices];
    }

    /// <summary>Sphère UV de rayon 1.</summary>
    static Vertex[] BuildSphere(int stacks, int slices)
    {
        var vertices = new List<Vertex>(stacks * slices * 6);

        static float3 Point(int stack, int slice, int stacks, int slices)
        {
            var theta = MathF.PI * stack / stacks;
            var phi = 2 * MathF.PI * slice / slices;
            return new float3(MathF.Sin(theta) * MathF.Cos(phi), MathF.Cos(theta), MathF.Sin(theta) * MathF.Sin(phi));
        }

        for (var i = 0; i < stacks; i++)
        for (var j = 0; j < slices; j++)
        {
            var p00 = Point(i, j, stacks, slices);
            var p10 = Point(i + 1, j, stacks, slices);
            var p11 = Point(i + 1, j + 1, stacks, slices);
            var p01 = Point(i, j + 1, stacks, slices);
            foreach (var p in (ReadOnlySpan<float3>)[p00, p10, p11, p00, p11, p01])
                vertices.Add(new Vertex(p, p));
        }
        return [.. vertices];
    }

    public void Dispose()
    {
        OpenGl.DeleteVertexArrays([Cube.VertexArray, Sphere.VertexArray, Globe.VertexArray, _lineArray]);
        OpenGl.DeleteBuffers([Cube.VertexBuffer, Sphere.VertexBuffer, Globe.VertexBuffer, Cube.InstanceBuffer, Sphere.InstanceBuffer, Globe.InstanceBuffer, _lineBuffer]);
        _program.Dispose();
        _lineProgram.Dispose();
        Lights.Dispose();
        Post.Dispose();
    }

    // --- Shaders ---------------------------------------------------------------------------
    // Profondeur logarithmique : gl_FragDepth = log2(1 + w) · logDepth / 2, avec w la distance de vue.

    static ReadOnlySpan<byte> SceneVertexShader =>
        """
            #version 410 core
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aNormal;
            layout (location = 2) in mat4 iModel;          // attributs 2 à 5, un par instance (relatif à la caméra)
            layout (location = 6) in vec4 iColorEmissive;  // rgb sRGB, a = émissif (négatif : surface d'astre)
            uniform mat4 viewProjection;
            out vec3 normal;
            out vec3 localNormal;
            out vec3 viewPos;                              // position relative à la caméra
            out float logZ;
            flat out vec3 color;
            flat out float emissive;
            void main()
            {
                vec4 world = iModel * vec4(aPos, 1.0);
                viewPos = world.xyz;
                normal = mat3(iModel) * aNormal;
                localNormal = aNormal;
                color = iColorEmissive.rgb;
                emissive = iColorEmissive.a;
                gl_Position = viewProjection * world;
                logZ = 1.0 + gl_Position.w;
            }
            """u8;

    static ReadOnlySpan<byte> SceneFragmentShader =>
        """
            #version 410 core
            in vec3 normal;
            in vec3 localNormal;
            in vec3 viewPos;
            in float logZ;
            flat in vec3 color;          // sRGB, converti en linéaire ici
            flat in float emissive;

            uniform float emissiveScale;
            uniform vec3 sunDirection;
            uniform vec3 sunColor;
            uniform vec3 skyAmbient;
            uniform vec3 groundAmbient;
            uniform float logDepth;

            struct PointLight
            {
                vec4 positionRadius;     // xyz = position relative à la caméra, w = rayon d'influence
                vec4 colorIntensity;     // rgb = couleur linéaire, a = intensité
            };
            layout(std140) uniform Lights
            {
                ivec4 lightCount;
                PointLight lights[64];
            };

            out vec4 FragColor;

            float hash(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }
            float noise(vec3 p)
            {
                vec3 i = floor(p); vec3 f = fract(p); f = f * f * (3.0 - 2.0 * f);
                return mix(mix(mix(hash(i), hash(i + vec3(1,0,0)), f.x), mix(hash(i + vec3(0,1,0)), hash(i + vec3(1,1,0)), f.x), f.y),
                           mix(mix(hash(i + vec3(0,0,1)), hash(i + vec3(1,0,1)), f.x), mix(hash(i + vec3(0,1,1)), hash(i + vec3(1,1,1)), f.x), f.y), f.z);
            }

            void main()
            {
                gl_FragDepth = log2(logZ) * logDepth * 0.5;
                vec3 albedo = pow(color, vec3(2.2));
                vec3 n = normalize(normal);
                vec3 v = normalize(-viewPos);

                if (emissive < 0.0)
                {
                    // Surface d'astre : bandes de géante gazeuse (-1) ou sol rocheux (-2), terminateur doux,
                    // liseré d'atmosphère côté jour.
                    vec3 p = normalize(localNormal);
                    float detail;
                    if (emissive > -1.5)
                    {
                        float lat = p.y + 0.08 * noise(p * 6.0) + 0.03 * noise(p * 24.0);
                        detail = 0.78 + 0.22 * sin(lat * 38.0 + 2.0 * sin(lat * 9.0)) + 0.08 * noise(p * 60.0);
                    }
                    else
                    {
                        detail = 0.65 + 0.35 * noise(p * 8.0) + 0.15 * noise(p * 40.0) - 0.2 * smoothstep(0.62, 0.7, noise(p * 14.0));
                    }
                    // Plus sombre que les coques : un astre immense ne doit pas saturer l'image.
                    float sun = dot(n, sunDirection);
                    vec3 lit = albedo * detail * (sunColor * 0.42 * smoothstep(-0.08, 0.6, sun) + skyAmbient * 0.25);
                    float rim = pow(1.0 - max(dot(n, v), 0.0), 4.0) * smoothstep(-0.2, 0.4, sun);
                    lit += sunColor * albedo * rim * 0.35;
                    FragColor = vec4(lit, 1.0);
                    return;
                }

                vec3 lighting = mix(groundAmbient, skyAmbient, n.y * 0.5 + 0.5);
                lighting += sunColor * max(dot(n, sunDirection), 0.0);

                for (int i = 0; i < lightCount.x; i++)
                {
                    vec3 toLight = lights[i].positionRadius.xyz - viewPos;
                    float radius = lights[i].positionRadius.w;
                    float distance2 = dot(toLight, toLight);
                    if (distance2 >= radius * radius)
                        continue;
                    float dist = sqrt(distance2);
                    vec3 l = toLight / dist;
                    float window = clamp(1.0 - pow(dist / radius, 4.0), 0.0, 1.0);
                    float falloff = window * window * (1.0 - dist / radius) * (1.0 - dist / radius);
                    float diffuse = max(dot(n, l), 0.0);
                    float specular = diffuse > 0.0 ? pow(max(dot(n, normalize(l + v)), 0.0), 32.0) * 0.5 : 0.0;
                    lighting += lights[i].colorIntensity.rgb * lights[i].colorIntensity.a * falloff * (diffuse + specular);
                }

                FragColor = vec4(albedo * lighting + albedo * emissive * emissiveScale, 1.0);
            }
            """u8;

    static ReadOnlySpan<byte> LineVertexShader =>
        """
            #version 410 core
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aColor;
            uniform mat4 viewProjection;
            out vec3 color;
            out float logZ;
            void main()
            {
                color = aColor;
                gl_Position = viewProjection * vec4(aPos, 1.0);
                logZ = 1.0 + gl_Position.w;
            }
            """u8;

    static ReadOnlySpan<byte> LineFragmentShader =>
        """
            #version 410 core
            in vec3 color;
            in float logZ;
            uniform float logDepth;
            out vec4 FragColor;
            void main()
            {
                gl_FragDepth = log2(logZ) * logDepth * 0.5;
                FragColor = vec4(color, 1.0);
            }
            """u8;
}
