using System.Buffers.Binary;
using System.IO.Compression;
using Brutal.OpenGlApi;

namespace OrionumFleet.Viewer;

/// <summary>Capture du backbuffer en PNG (sans dépendance) : sert à vérifier le rendu sans regarder l'écran.</summary>
static unsafe class Capture
{
    public static void SavePng(string path, int width, int height)
    {
        var pixels = new byte[width * height * 3];
        OpenGl.PixelStorei(OpenGl.GL_PACK_ALIGNMENT, 1);
        fixed (byte* p = pixels)
            OpenGl.ReadPixels(0, 0, width, height, OpenGl.GL_RGB, OpenGl.GL_UNSIGNED_BYTE, p);

        // Lignes PNG : un octet de filtre (0) puis les pixels, de haut en bas (OpenGL lit de bas en haut).
        var raw = new byte[height * (width * 3 + 1)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 3 + 1);
            raw[row] = 0;
            Buffer.BlockCopy(pixels, (height - 1 - y) * width * 3, raw, row + 1, width * 3);
        }

        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // 8 bits par canal
        header[9] = 2; // RGB
        Chunk(file, "IHDR", header);
        using (var compressed = new MemoryStream())
        {
            using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                z.Write(raw);
            Chunk(file, "IDAT", compressed.ToArray());
        }
        Chunk(file, "IEND", []);
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        s.Write(buffer);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc32(typeBytes, data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        s.Write(buffer);
    }

    static readonly uint[] Table = BuildTable();

    static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    static uint Crc32(byte[] a, byte[] b)
    {
        var c = 0xFFFFFFFFu;
        foreach (var x in a)
            c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in b)
            c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
