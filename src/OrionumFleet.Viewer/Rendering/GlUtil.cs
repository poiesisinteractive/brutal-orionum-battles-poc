using Brutal.Numerics;
using Brutal.OpenGlApi;

namespace OrionumFleet.Viewer.Rendering;

/// <summary>Petits utilitaires OpenGL partagés par les passes de rendu.</summary>
static unsafe class GlUtil
{
    /// <summary>Triangle plein écran généré depuis gl_VertexID : aucun vertex buffer, juste un VAO vide.</summary>
    public static ReadOnlySpan<byte> FullscreenVertexShader =>
        """
            #version 410 core
            out vec2 uv;
            void main()
            {
                vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
                uv = p;
                gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
            }
            """u8;

    public static GlProgram CreateProgram(ReadOnlySpan<byte> vertexSource, ReadOnlySpan<byte> fragmentSource)
    {
        using var vertexShader = OpenGl.CreateShader(GlShaderType.Vertex).SetSource(vertexSource).Compile();
        using var fragmentShader = OpenGl.CreateShader(GlShaderType.Fragment).SetSource(fragmentSource).Compile();
        return OpenGl.CreateProgram().Attach(vertexShader, fragmentShader).Link();
    }

    /// <summary>Texture 2D flottante RGBA16F, filtrage linéaire, bords clampés : le format de travail HDR.</summary>
    public static GlTexture CreateHdrTexture(int2 size)
    {
        var ids = new GlTexture[1];
        OpenGl.GenTextures(ids);
        var texture = ids[0];

        OpenGl.BindTexture(OpenGl.GL_TEXTURE_2D, texture.Id);
        OpenGl.TexImage2D(
            OpenGl.GL_TEXTURE_2D,
            0,
            (int)OpenGl.GL_RGBA16F,
            size.X,
            size.Y,
            0,
            OpenGl.GL_RGBA,
            OpenGl.GL_HALF_FLOAT,
            null
        );
        OpenGl.TexParameterFloat(OpenGl.GL_TEXTURE_2D, OpenGl.GL_TEXTURE_MIN_FILTER, (int)OpenGl.GL_LINEAR);
        OpenGl.TexParameterFloat(OpenGl.GL_TEXTURE_2D, OpenGl.GL_TEXTURE_MAG_FILTER, (int)OpenGl.GL_LINEAR);
        OpenGl.TexParameterFloat(OpenGl.GL_TEXTURE_2D, OpenGl.GL_TEXTURE_WRAP_S, (int)OpenGl.GL_CLAMP_TO_EDGE);
        OpenGl.TexParameterFloat(OpenGl.GL_TEXTURE_2D, OpenGl.GL_TEXTURE_WRAP_T, (int)OpenGl.GL_CLAMP_TO_EDGE);
        return texture;
    }

    public static void BindTexture(int unit, GlTexture texture)
    {
        OpenGl.ActiveTexture(OpenGl.GL_TEXTURE0 + (uint)unit);
        OpenGl.BindTexture(OpenGl.GL_TEXTURE_2D, texture.Id);
    }

    public static void AttachColor(GlTexture texture)
    {
        OpenGl.FramebufferTexture2D(
            OpenGl.GL_FRAMEBUFFER,
            OpenGl.GL_COLOR_ATTACHMENT0,
            OpenGl.GL_TEXTURE_2D,
            texture.Id,
            0
        );
    }

    public static void CheckFramebuffer(string name)
    {
        var status = OpenGl.CheckFramebufferStatus(OpenGl.GL_FRAMEBUFFER);
        if (status.Id != OpenGl.GL_FRAMEBUFFER_COMPLETE)
            throw new InvalidOperationException($"Framebuffer {name} incomplet : 0x{status.Id:X}");
    }
}
