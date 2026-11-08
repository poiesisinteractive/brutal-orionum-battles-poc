using Brutal.Numerics;
using Brutal.OpenGlApi;

namespace OrionumFleet.Viewer.Rendering;

/// <summary>
/// Cible de rendu HDR + bloom + tonemapping.
///
/// 1. La scène est rendue dans une texture RGBA16F (valeurs > 1 autorisées).
/// 2. Bloom façon Call of Duty (Jimenez, SIGGRAPH 2014) : on réduit l'image en une chaîne de mips
///    (filtre 13 taps, seuil doux et moyenne de Karis sur la première passe), puis on remonte
///    la chaîne avec un filtre tente 3x3 en additionnant chaque niveau au précédent.
/// 3. Composition : scène + bloom, exposition, tonemapping ACES, vignette, gamma -> backbuffer.
/// </summary>
sealed unsafe class PostProcess : IDisposable
{
    const int MaxBloomMips = 6;
    const int MinBloomMipSize = 8;

    // Réglages exposés dans le panneau debug.
    public bool BloomEnabled = true;
    public float Exposure = 1.1f;
    public float BloomStrength = 0.8f;
    public float BloomThreshold = 0.9f;
    public float BloomKnee = 0.5f;
    public float BloomRadius = 0.005f;
    public float Vignette = 0.35f;

    readonly GlProgram _downsample;
    readonly GlProgram _upsample;
    readonly GlProgram _composite;
    readonly GlVertexArray _emptyVertexArray;
    readonly GlFramebuffer _sceneFramebuffer;
    readonly GlFramebuffer _bloomFramebuffer;

    GlTexture _sceneColor;
    GlRenderbuffer _sceneDepth;
    readonly GlTexture[] _bloomMips = new GlTexture[MaxBloomMips];
    readonly int2[] _bloomSizes = new int2[MaxBloomMips];
    int _bloomMipCount;
    int2 _size;

    public int2 Size => _size;
    public int BloomMipCount => _bloomMipCount;

    public PostProcess()
    {
        _downsample = GlUtil.CreateProgram(GlUtil.FullscreenVertexShader, DownsampleShader);
        _upsample = GlUtil.CreateProgram(GlUtil.FullscreenVertexShader, UpsampleShader);
        _composite = GlUtil.CreateProgram(GlUtil.FullscreenVertexShader, CompositeShader);

        _emptyVertexArray = OpenGl.GenVertexArray();

        var framebuffers = new GlFramebuffer[2];
        OpenGl.GenFramebuffers(framebuffers);
        _sceneFramebuffer = framebuffers[0];
        _bloomFramebuffer = framebuffers[1];
    }

    /// <summary>Active la cible HDR (la recrée si la fenêtre a changé de taille) et l'efface.</summary>
    public void BeginScene(int2 size, float3 clearColor)
    {
        if (size != _size)
            Resize(size);

        OpenGl.BindFramebuffer(OpenGl.GL_FRAMEBUFFER, _sceneFramebuffer);
        OpenGl.Viewport(0, 0, size.X, size.Y);
        OpenGl.ClearColor(new float4(clearColor, 1f));
        OpenGl.Clear(GlClearBit.Color | GlClearBit.Depth);
    }

    /// <summary>Calcule le bloom puis compose l'image finale dans le backbuffer (framebuffer 0).</summary>
    public void Resolve()
    {
        OpenGl.Disable(GlCapability.DepthTest);
        OpenGl.Disable(GlCapability.Blend);
        _emptyVertexArray.Bind();

        var bloomActive = BloomEnabled && _bloomMipCount > 0;
        if (bloomActive)
            RenderBloom();

        OpenGl.BindFramebuffer(OpenGl.GL_FRAMEBUFFER, default);
        OpenGl.Viewport(0, 0, _size.X, _size.Y);

        _composite.Use();
        _composite.Uniform("scene"u8, 0);
        _composite.Uniform("bloom"u8, 1);
        _composite.Uniform("exposure"u8, Exposure);
        _composite.Uniform("bloomStrength"u8, bloomActive ? BloomStrength : 0f);
        _composite.Uniform("vignette"u8, Vignette);
        GlUtil.BindTexture(0, _sceneColor);
        GlUtil.BindTexture(1, bloomActive ? _bloomMips[0] : _sceneColor);
        DrawFullscreen();

        // On rend l'unité 0 active pour ImGui, qui dessine juste après.
        OpenGl.ActiveTexture(OpenGl.GL_TEXTURE0);
    }

    void RenderBloom()
    {
        OpenGl.BindFramebuffer(OpenGl.GL_FRAMEBUFFER, _bloomFramebuffer);

        // Descente : scène -> mip 0 -> mip 1 -> ...
        _downsample.Use();
        _downsample.Uniform("source"u8, 0);
        _downsample.Uniform("threshold"u8, new float2(BloomThreshold, MathF.Max(BloomKnee, 1e-3f)));

        var source = _sceneColor;
        var sourceSize = _size;
        for (var i = 0; i < _bloomMipCount; i++)
        {
            GlUtil.AttachColor(_bloomMips[i]);
            OpenGl.Viewport(0, 0, _bloomSizes[i].X, _bloomSizes[i].Y);
            GlUtil.BindTexture(0, source);
            _downsample.Uniform("texelSize"u8, new float2(1f / sourceSize.X, 1f / sourceSize.Y));
            _downsample.Uniform("firstPass"u8, i == 0 ? 1 : 0);
            DrawFullscreen();

            source = _bloomMips[i];
            sourceSize = _bloomSizes[i];
        }

        // Remontée additive : chaque mip est flouté et ajouté au mip juste au-dessus.
        OpenGl.Enable(GlCapability.Blend);
        OpenGl.BlendFunc(OpenGl.GL_ONE, OpenGl.GL_ONE);

        _upsample.Use();
        _upsample.Uniform("source"u8, 0);
        _upsample.Uniform("radius"u8, new float2(BloomRadius, BloomRadius * _size.X / _size.Y));
        for (var i = _bloomMipCount - 1; i > 0; i--)
        {
            GlUtil.AttachColor(_bloomMips[i - 1]);
            OpenGl.Viewport(0, 0, _bloomSizes[i - 1].X, _bloomSizes[i - 1].Y);
            GlUtil.BindTexture(0, _bloomMips[i]);
            DrawFullscreen();
        }

        OpenGl.Disable(GlCapability.Blend);
    }

    static void DrawFullscreen() => OpenGl.Draw(GlTopology.Triangles, 3, 1, 0, 0);

    void Resize(int2 size)
    {
        DeleteTargets();
        _size = size;

        // Cible de la scène : couleur HDR + profondeur.
        _sceneColor = GlUtil.CreateHdrTexture(size);
        var depth = new GlRenderbuffer[1];
        OpenGl.GenRenderbuffers(depth);
        _sceneDepth = depth[0];
        OpenGl.BindRenderbuffer(OpenGl.GL_RENDERBUFFER, _sceneDepth);
        OpenGl.RenderbufferStorage(OpenGl.GL_RENDERBUFFER, OpenGl.GL_DEPTH_COMPONENT24, size.X, size.Y);

        OpenGl.BindFramebuffer(OpenGl.GL_FRAMEBUFFER, _sceneFramebuffer);
        GlUtil.AttachColor(_sceneColor);
        OpenGl.FramebufferRenderbuffer(
            OpenGl.GL_FRAMEBUFFER,
            OpenGl.GL_DEPTH_ATTACHMENT,
            OpenGl.GL_RENDERBUFFER,
            _sceneDepth
        );
        GlUtil.CheckFramebuffer("scene");

        // Chaîne de mips du bloom : 1/2, 1/4, 1/8... tant que c'est assez grand.
        _bloomMipCount = 0;
        var mipSize = size;
        for (var i = 0; i < MaxBloomMips; i++)
        {
            mipSize = new int2(Math.Max(1, mipSize.X / 2), Math.Max(1, mipSize.Y / 2));
            if (mipSize.X < MinBloomMipSize || mipSize.Y < MinBloomMipSize)
                break;

            _bloomSizes[i] = mipSize;
            _bloomMips[i] = GlUtil.CreateHdrTexture(mipSize);
            _bloomMipCount++;
        }

        if (_bloomMipCount > 0)
        {
            OpenGl.BindFramebuffer(OpenGl.GL_FRAMEBUFFER, _bloomFramebuffer);
            GlUtil.AttachColor(_bloomMips[0]);
            GlUtil.CheckFramebuffer("bloom");
        }
    }

    void DeleteTargets()
    {
        if (_size.X == 0)
            return;

        OpenGl.DeleteTextures([_sceneColor]);
        OpenGl.DeleteRenderbuffers([_sceneDepth]);
        OpenGl.DeleteTextures(_bloomMips.AsSpan(0, _bloomMipCount));
        _bloomMipCount = 0;
        _size = default;
    }

    public void Dispose()
    {
        DeleteTargets();
        OpenGl.DeleteFramebuffers([_sceneFramebuffer, _bloomFramebuffer]);
        OpenGl.DeleteVertexArrays([_emptyVertexArray]);
        _composite.Dispose();
        _upsample.Dispose();
        _downsample.Dispose();
    }

    // --- Shaders ---------------------------------------------------------------------------

    static ReadOnlySpan<byte> DownsampleShader =>
        """
            #version 410 core
            in vec2 uv;
            uniform sampler2D source;
            uniform vec2 texelSize;   // taille d'un texel de la SOURCE
            uniform int firstPass;
            uniform vec2 threshold;   // x = seuil, y = genou (transition douce)
            out vec4 FragColor;

            vec3 Prefilter(vec3 c)
            {
                float brightness = max(c.r, max(c.g, c.b));
                float soft = clamp(brightness - threshold.x + threshold.y, 0.0, 2.0 * threshold.y);
                soft = soft * soft / (4.0 * threshold.y);
                float contribution = max(soft, brightness - threshold.x) / max(brightness, 1e-4);
                return c * contribution;
            }

            float KarisWeight(vec3 c)
            {
                return 1.0 / (1.0 + dot(c, vec3(0.2126, 0.7152, 0.0722)));
            }

            void main()
            {
                vec2 t = texelSize;
                vec3 a = texture(source, uv + t * vec2(-2.0,  2.0)).rgb;
                vec3 b = texture(source, uv + t * vec2( 0.0,  2.0)).rgb;
                vec3 c = texture(source, uv + t * vec2( 2.0,  2.0)).rgb;
                vec3 d = texture(source, uv + t * vec2(-2.0,  0.0)).rgb;
                vec3 e = texture(source, uv).rgb;
                vec3 f = texture(source, uv + t * vec2( 2.0,  0.0)).rgb;
                vec3 g = texture(source, uv + t * vec2(-2.0, -2.0)).rgb;
                vec3 h = texture(source, uv + t * vec2( 0.0, -2.0)).rgb;
                vec3 i = texture(source, uv + t * vec2( 2.0, -2.0)).rgb;
                vec3 j = texture(source, uv + t * vec2(-1.0,  1.0)).rgb;
                vec3 k = texture(source, uv + t * vec2( 1.0,  1.0)).rgb;
                vec3 l = texture(source, uv + t * vec2(-1.0, -1.0)).rgb;
                vec3 m = texture(source, uv + t * vec2( 1.0, -1.0)).rgb;

                vec3 result;
                if (firstPass == 1)
                {
                    // Seuil doux puis moyenne de Karis par groupe de 4 échantillons :
                    // un pixel isolé très brillant ne « scintille » pas d'une frame à l'autre.
                    vec3 g0 = Prefilter((a + b + d + e) * 0.25);
                    vec3 g1 = Prefilter((b + c + e + f) * 0.25);
                    vec3 g2 = Prefilter((d + e + g + h) * 0.25);
                    vec3 g3 = Prefilter((e + f + h + i) * 0.25);
                    vec3 g4 = Prefilter((j + k + l + m) * 0.25);
                    float w0 = KarisWeight(g0) * 0.125;
                    float w1 = KarisWeight(g1) * 0.125;
                    float w2 = KarisWeight(g2) * 0.125;
                    float w3 = KarisWeight(g3) * 0.125;
                    float w4 = KarisWeight(g4) * 0.5;
                    result = (g0 * w0 + g1 * w1 + g2 * w2 + g3 * w3 + g4 * w4) / (w0 + w1 + w2 + w3 + w4);
                }
                else
                {
                    result = e * 0.125
                        + (a + c + g + i) * 0.03125
                        + (b + d + f + h) * 0.0625
                        + (j + k + l + m) * 0.125;
                }
                FragColor = vec4(max(result, vec3(0.0)), 1.0);
            }
            """u8;

    static ReadOnlySpan<byte> UpsampleShader =>
        """
            #version 410 core
            in vec2 uv;
            uniform sampler2D source;
            uniform vec2 radius;      // rayon du filtre en UV (corrigé du ratio d'aspect)
            out vec4 FragColor;

            void main()
            {
                float x = radius.x;
                float y = radius.y;
                vec3 a = texture(source, uv + vec2(-x,  y)).rgb;
                vec3 b = texture(source, uv + vec2( 0,  y)).rgb;
                vec3 c = texture(source, uv + vec2( x,  y)).rgb;
                vec3 d = texture(source, uv + vec2(-x,  0)).rgb;
                vec3 e = texture(source, uv).rgb;
                vec3 f = texture(source, uv + vec2( x,  0)).rgb;
                vec3 g = texture(source, uv + vec2(-x, -y)).rgb;
                vec3 h = texture(source, uv + vec2( 0, -y)).rgb;
                vec3 i = texture(source, uv + vec2( x, -y)).rgb;

                // Filtre tente 3x3.
                vec3 result = e * 4.0 + (b + d + f + h) * 2.0 + (a + c + g + i);
                FragColor = vec4(result / 16.0, 1.0);
            }
            """u8;

    static ReadOnlySpan<byte> CompositeShader =>
        """
            #version 410 core
            in vec2 uv;
            uniform sampler2D scene;
            uniform sampler2D bloom;
            uniform float exposure;
            uniform float bloomStrength;
            uniform float vignette;
            out vec4 FragColor;

            // Approximation ACES de Krzysztof Narkowicz (2015).
            vec3 Aces(vec3 x)
            {
                return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0);
            }

            void main()
            {
                vec3 hdr = texture(scene, uv).rgb + texture(bloom, uv).rgb * bloomStrength;
                vec3 color = Aces(hdr * exposure);

                vec2 d = uv - 0.5;
                color *= clamp(1.0 - vignette * dot(d, d) * 2.0, 0.0, 1.0);

                FragColor = vec4(pow(color, vec3(1.0 / 2.2)), 1.0);
            }
            """u8;
}
