using System.Runtime.InteropServices;
using Brutal.Numerics;
using Brutal.OpenGlApi;

namespace OrionumFleet.Viewer.Rendering;

/// <summary>
/// Une lumière ponctuelle telle que le shader la lit (layout std140).
/// Uniquement des vec4 : c'est le moyen le plus simple d'éviter les pièges d'alignement std140.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
struct GpuPointLight
{
    public float4 PositionRadius;
    public float4 ColorIntensity;
}

/// <summary>
/// Uniform buffer des lumières ponctuelles, rempli à chaque frame.
/// Côté GLSL : <c>layout(std140) uniform Lights { ivec4 lightCount; PointLight lights[64]; };</c>
/// </summary>
sealed unsafe class LightBuffer : IDisposable
{
    /// <summary>Doit correspondre à la taille du tableau dans le shader de scène.</summary>
    public const int MaxLights = 64;
    public const uint BindingPoint = 0;

    // ivec4 lightCount : 16 octets, puis le tableau (aligné sur 16).
    const int HeaderSize = 16;

    readonly GlBuffer _buffer;
    readonly GpuPointLight[] _lights = new GpuPointLight[MaxLights];

    public int Count { get; private set; }

    public LightBuffer()
    {
        _buffer = OpenGl.GenBuffer();
        _buffer.Bind(GlBindTarget.UniformBuffer);
        OpenGl.BufferData(
            GlBindTarget.UniformBuffer,
            new byte[HeaderSize + MaxLights * sizeof(GpuPointLight)],
            GlBufferUsage.DynamicDraw
        );
    }

    public void Clear() => Count = 0;

    /// <summary>Ajoute une lumière ; les lumières au-delà de <see cref="MaxLights"/> sont ignorées.</summary>
    public void Add(float3 position, float radius, float3 color, float intensity)
    {
        if (Count == MaxLights)
            return;

        _lights[Count++] = new GpuPointLight
        {
            PositionRadius = new float4(position, radius),
            ColorIntensity = new float4(color, intensity),
        };
    }

    /// <summary>Envoie les lumières au GPU et attache le buffer au point de binding des shaders.</summary>
    public void Upload(int count)
    {
        _buffer.Bind(GlBindTarget.UniformBuffer);

        var header = new int4(count, 0, 0, 0);
        OpenGl.BufferSubData(OpenGl.GL_UNIFORM_BUFFER, 0, HeaderSize, &header);
        fixed (GpuPointLight* lights = _lights)
            OpenGl.BufferSubData(OpenGl.GL_UNIFORM_BUFFER, HeaderSize, count * sizeof(GpuPointLight), lights);

        OpenGl.BindBufferBase(OpenGl.GL_UNIFORM_BUFFER, BindingPoint, _buffer);
    }

    public void Upload() => Upload(Count);

    public void Dispose() => OpenGl.DeleteBuffers([_buffer]);
}
