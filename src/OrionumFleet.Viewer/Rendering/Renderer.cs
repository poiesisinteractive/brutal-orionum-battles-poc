using System.Runtime.InteropServices;
using Brutal.Numerics;
using Brutal.OpenGlApi;

namespace OrionumFleet.Viewer.Rendering;

[StructLayout(LayoutKind.Sequential)]
record struct Vertex(float3 Position, float3 Normal);

/// <summary>Sommet d'une ligne lumineuse : position et couleur HDR (linéaire, peut dépasser 1 pour le bloom).</summary>
[StructLayout(LayoutKind.Sequential)]
record struct LineVertex(float3 Position, float3 Color);

/// <summary>Maillage GPU minimal : un VAO et un VBO, dessiné en triangles non indexés.</summary>
readonly record struct Mesh(GlVertexArray VertexArray, GlBuffer VertexBuffer, int VertexCount);

/// <summary>
/// Renderer forward OpenGL 4.1, repris de Brutal Arena et mis à l'échelle d'une bataille (des milliers
/// d'unités) : la scène est rendue en HDR, éclairée par une lumière directionnelle, une ambiance et jusqu'à
/// 64 lumières ponctuelles, puis passée au post-process (bloom, tonemapping). Les tirs, rayons et repères
/// sont des lignes émissives en additif, dessinées après les solides.
/// </summary>
sealed class Renderer : IDisposable
{
    public bool PointLightsEnabled = true;
    public float SunIntensity = 0.9f;
    public float AmbientIntensity = 1f;
    public float EmissiveIntensity = 1f;
    public float3 SunColor = new(0.9f, 0.85f, 0.8f);
    public float3 SkyAmbient = new(0.05f, 0.06f, 0.1f);
    public float3 GroundAmbient = new(0.02f, 0.015f, 0.02f);
    public float3 FogColor = new(0.004f, 0.005f, 0.012f);
    public float FogStart = 4000;
    public float FogRange = 9000;

    static readonly float3 SunDirection = float3.Normalize(new float3(0.5f, 0.8f, -0.3f));

    readonly GlProgram _program;
    readonly GlProgram _lineProgram;
    readonly GlVertexArray _lineArray;
    readonly GlBuffer _lineBuffer;
    LineVertex[] _lines = new LineVertex[16384];
    int _lineCount;

    public float4x4 ViewProjection { get; private set; }
    public int2 Viewport { get; private set; }

    public Mesh Cube { get; }
    public Mesh Sphere { get; }
    public LightBuffer Lights { get; } = new();
    public PostProcess Post { get; } = new();

    public Renderer()
    {
        _program = GlUtil.CreateProgram(SceneVertexShader, SceneFragmentShader);
        OpenGl.UniformBlockBinding(_program, OpenGl.GetUniformBlockIndex(_program, "Lights"u8), LightBuffer.BindingPoint);
        _lineProgram = GlUtil.CreateProgram(LineVertexShader, LineFragmentShader);

        Cube = Upload(BuildCube());
        Sphere = Upload(BuildSphere(16, 24));

        _lineArray = OpenGl.GenVertexArray().Bind();
        _lineBuffer = OpenGl.GenBuffer();
        _lineBuffer.Bind(GlBindTarget.ArrayBuffer);
        OpenGl.BufferData(GlBindTarget.ArrayBuffer, (ReadOnlySpan<LineVertex>)_lines, GlBufferUsage.StreamDraw);
        OpenGl.VertexAttribute(new GlAttribute(0, 3, OpenGl.GL_FLOAT, GlScaling.Float, 6 * sizeof(float), 0));
        OpenGl.VertexAttribute(new GlAttribute(1, 3, OpenGl.GL_FLOAT, GlScaling.Float, 6 * sizeof(float), 3 * sizeof(float)));
        OpenGl.EnableVertexAttribArray(0);
        OpenGl.EnableVertexAttribArray(1);
    }

    /// <summary>Prépare la frame : cible HDR, états GL (l'UI ImGui en modifie), caméra, éclairage.</summary>
    public void BeginFrame(int2 framebufferSize, float3 eye, float3 target)
    {
        Viewport = framebufferSize;
        Post.BeginScene(framebufferSize, FogColor);

        OpenGl.Disable(GlCapability.Blend);
        OpenGl.Disable(GlCapability.ScissorTest);
        OpenGl.Enable(GlCapability.DepthTest);
        OpenGl.DepthMask(true);

        Lights.Upload(PointLightsEnabled ? Lights.Count : 0);

        var aspect = framebufferSize.Y > 0 ? framebufferSize.X / (float)framebufferSize.Y : 1f;
        var view = float4x4.CreateLookAt(eye, target, float3.UnitY);
        var projection = float4x4.CreatePerspectiveFieldOfView(float.DegreesToRadians(50), aspect, 2f, 40000f);
        ViewProjection = view * projection;

        _program.Use();
        _program.Uniform("viewProjection"u8, ViewProjection);
        _program.Uniform("cameraPos"u8, eye);
        _program.Uniform("sunDirection"u8, SunDirection);
        _program.Uniform("sunColor"u8, SunColor * SunIntensity);
        _program.Uniform("skyAmbient"u8, SkyAmbient * AmbientIntensity);
        _program.Uniform("groundAmbient"u8, GroundAmbient * AmbientIntensity);
        _program.Uniform("fogColor"u8, FogColor);
        _program.Uniform("fogStartRange"u8, new float2(FogStart, FogRange));
        _program.Uniform("emissiveScale"u8, EmissiveIntensity);
        _lineCount = 0;
        _eye = eye;
    }

    float3 _eye;

    /// <summary>Lignes en additif (sans écrire la profondeur), puis bloom + tonemapping vers le backbuffer.</summary>
    public void EndFrame()
    {
        if (_lineCount > 0)
        {
            OpenGl.Enable(GlCapability.Blend);
            OpenGl.BlendFunc(OpenGl.GL_ONE, OpenGl.GL_ONE);
            OpenGl.DepthMask(false);
            _lineProgram.Use();
            _lineProgram.Uniform("viewProjection"u8, ViewProjection);
            _lineProgram.Uniform("cameraPos"u8, _eye);
            _lineProgram.Uniform("fogStartRange"u8, new float2(FogStart, FogRange));
            _lineArray.Bind();
            _lineBuffer.Bind(GlBindTarget.ArrayBuffer);
            OpenGl.BufferData(GlBindTarget.ArrayBuffer, (ReadOnlySpan<LineVertex>)_lines.AsSpan(0, _lineCount), GlBufferUsage.StreamDraw);
            OpenGl.Draw(GlTopology.Lines, _lineCount, 1, 0, 0);
            OpenGl.DepthMask(true);
            OpenGl.Disable(GlCapability.Blend);
        }
        Post.Resolve();
    }

    /// <summary>Une ligne émissive (couleur HDR linéaire).</summary>
    public void Line(float3 a, float3 b, float3 color) => Line(a, b, color, color);

    public void Line(float3 a, float3 b, float3 colorA, float3 colorB)
    {
        if (_lineCount + 2 > _lines.Length)
            Array.Resize(ref _lines, _lines.Length * 2);
        _lines[_lineCount++] = new LineVertex(a, colorA);
        _lines[_lineCount++] = new LineVertex(b, colorB);
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

    /// <summary>Projette un point du monde en pixels écran ; null s'il est derrière la caméra.</summary>
    public float2? Project(float3 world)
    {
        var clip = float4.Transform(world, ViewProjection);
        if (clip.W <= 1e-3f)
            return null;
        var ndc = new float2(clip.X / clip.W, clip.Y / clip.W);
        if (MathF.Abs(ndc.X) > 1.2f || MathF.Abs(ndc.Y) > 1.2f)
            return null;
        return new float2((ndc.X * 0.5f + 0.5f) * Viewport.X, (0.5f - ndc.Y * 0.5f) * Viewport.Y);
    }

    /// <param name="emissive">Multiplicateur HDR de la couleur propre de l'objet : au-delà de ~1, il « bloome ».</param>
    public void DrawBox(in float4x4 model, float3 color, float emissive = 0) => Draw(Cube, model, color, emissive);

    public void DrawSphere(float3 position, float radius, float3 color, float emissive = 0) =>
        Draw(Sphere, float4x4.CreateScale(radius) * float4x4.CreateTranslation(position), color, emissive);

    public void Draw(in Mesh mesh, in float4x4 model, float3 color, float emissive)
    {
        mesh.VertexArray.Bind();
        _program.Uniform("model"u8, model);
        _program.Uniform("color"u8, color);
        _program.Uniform("emissive"u8, emissive);
        OpenGl.Draw(GlTopology.Triangles, mesh.VertexCount, 1, 0, 0);
    }

    static Mesh Upload(Vertex[] vertices)
    {
        var vertexArray = OpenGl.GenVertexArray().Bind();
        var vertexBuffer = OpenGl.GenBuffer();
        vertexBuffer.Bind(GlBindTarget.ArrayBuffer);
        OpenGl.BufferData(GlBindTarget.ArrayBuffer, vertices, GlBufferUsage.StaticDraw);
        OpenGl.VertexAttribute(new GlAttribute(0, 3, OpenGl.GL_FLOAT, GlScaling.Float, 6 * sizeof(float), 0));
        OpenGl.VertexAttribute(new GlAttribute(1, 3, OpenGl.GL_FLOAT, GlScaling.Float, 6 * sizeof(float), 3 * sizeof(float)));
        OpenGl.EnableVertexAttribArray(0);
        OpenGl.EnableVertexAttribArray(1);
        return new Mesh(vertexArray, vertexBuffer, vertices.Length);
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
        OpenGl.DeleteVertexArrays([Cube.VertexArray, Sphere.VertexArray, _lineArray]);
        OpenGl.DeleteBuffers([Cube.VertexBuffer, Sphere.VertexBuffer, _lineBuffer]);
        _program.Dispose();
        _lineProgram.Dispose();
        Lights.Dispose();
        Post.Dispose();
    }

    // --- Shaders ---------------------------------------------------------------------------

    static ReadOnlySpan<byte> SceneVertexShader =>
        """
            #version 410 core
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aNormal;
            uniform mat4 model;
            uniform mat4 viewProjection;
            out vec3 normal;
            out vec3 worldPos;
            void main()
            {
                vec4 world = model * vec4(aPos, 1.0);
                worldPos = world.xyz;
                normal = mat3(model) * aNormal;
                gl_Position = viewProjection * world;
            }
            """u8;

    static ReadOnlySpan<byte> SceneFragmentShader =>
        """
            #version 410 core
            in vec3 normal;
            in vec3 worldPos;

            uniform vec3 color;          // sRGB, converti en linéaire ici
            uniform float emissive;
            uniform float emissiveScale;
            uniform vec3 cameraPos;
            uniform vec3 sunDirection;
            uniform vec3 sunColor;
            uniform vec3 skyAmbient;
            uniform vec3 groundAmbient;
            uniform vec3 fogColor;
            uniform vec2 fogStartRange;

            struct PointLight
            {
                vec4 positionRadius;     // xyz = position, w = rayon d'influence
                vec4 colorIntensity;     // rgb = couleur linéaire, a = intensité
            };
            layout(std140) uniform Lights
            {
                ivec4 lightCount;
                PointLight lights[64];
            };

            out vec4 FragColor;

            void main()
            {
                vec3 albedo = pow(color, vec3(2.2));
                vec3 n = normalize(normal);
                vec3 v = normalize(cameraPos - worldPos);

                vec3 lighting = mix(groundAmbient, skyAmbient, n.y * 0.5 + 0.5);
                lighting += sunColor * max(dot(n, sunDirection), 0.0);

                for (int i = 0; i < lightCount.x; i++)
                {
                    vec3 toLight = lights[i].positionRadius.xyz - worldPos;
                    float radius = lights[i].positionRadius.w;
                    float distance2 = dot(toLight, toLight);
                    if (distance2 >= radius * radius)
                        continue;
                    float dist = sqrt(distance2);
                    vec3 l = toLight / dist;
                    // Fenêtre qui ramène l'influence à zéro au rayon ; décroissance en (1 - d/r)² à l'échelle de la bataille.
                    float window = clamp(1.0 - pow(dist / radius, 4.0), 0.0, 1.0);
                    float falloff = window * window * (1.0 - dist / radius) * (1.0 - dist / radius);
                    float diffuse = max(dot(n, l), 0.0);
                    float specular = diffuse > 0.0 ? pow(max(dot(n, normalize(l + v)), 0.0), 32.0) * 0.5 : 0.0;
                    lighting += lights[i].colorIntensity.rgb * lights[i].colorIntensity.a * falloff * (diffuse + specular);
                }

                vec3 lit = albedo * lighting + albedo * emissive * emissiveScale;
                float fog = clamp((distance(cameraPos, worldPos) - fogStartRange.x) / fogStartRange.y, 0.0, 0.8);
                FragColor = vec4(mix(lit, fogColor, fog), 1.0);
            }
            """u8;

    static ReadOnlySpan<byte> LineVertexShader =>
        """
            #version 410 core
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aColor;
            uniform mat4 viewProjection;
            out vec3 color;
            out vec3 worldPos;
            void main()
            {
                color = aColor;
                worldPos = aPos;
                gl_Position = viewProjection * vec4(aPos, 1.0);
            }
            """u8;

    static ReadOnlySpan<byte> LineFragmentShader =>
        """
            #version 410 core
            in vec3 color;
            in vec3 worldPos;
            uniform vec3 cameraPos;
            uniform vec2 fogStartRange;
            out vec4 FragColor;
            void main()
            {
                float fog = clamp((distance(cameraPos, worldPos) - fogStartRange.x) / fogStartRange.y, 0.0, 0.85);
                FragColor = vec4(color * (1.0 - fog), 1.0);
            }
            """u8;
}
