using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Rendering;
#endif

[RequireComponent(typeof(Camera))]
public class CubemapBaker : MonoBehaviour
{
    [SerializeField] int resolution = 1024;
    [SerializeField] Compression compression = Compression.BC1;

    enum Compression
    {
        BC1,
        BC7
    }

#if UNITY_EDITOR
    static readonly CubemapFace[] Faces =
    {
        CubemapFace.PositiveX, CubemapFace.NegativeX,
        CubemapFace.PositiveY, CubemapFace.NegativeY,
        CubemapFace.PositiveZ, CubemapFace.NegativeZ
    };

    Camera TargetCamera => GetComponent<Camera>();

    public void Bake(string path)
    {
        Texture2D strip = Capture();
        File.WriteAllBytes(path, strip.EncodeToPNG());
        DestroyImmediate(strip);

        AssetDatabase.Refresh();
        Import(path);
    }

    Texture2D Capture()
    {
        RenderTexture target = new(resolution, resolution, 24, RenderTextureFormat.ARGB32)
        {
            dimension = TextureDimension.Cube
        };
        target.Create();

        if (!TargetCamera.RenderToCubemap(target))
            Debug.LogError("Cubemap capture failed.", this);

        Texture2D strip = new(resolution * Faces.Length, resolution, TextureFormat.RGBA32, false);
        RenderTexture previous = RenderTexture.active;

        for (int i = 0; i < Faces.Length; i++)
        {
            Graphics.SetRenderTarget(target, 0, Faces[i]);

            Texture2D face = ReadFace();
            strip.SetPixels(i * resolution, 0, resolution, resolution, FlipY(face.GetPixels()));
            DestroyImmediate(face);
        }

        RenderTexture.active = previous;
        strip.Apply();

        target.Release();
        DestroyImmediate(target);
        return strip;
    }

    Texture2D ReadFace()
    {
        Texture2D face = new(resolution, resolution, TextureFormat.RGBA32, false);
        face.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        face.Apply();
        return face;
    }

    Color[] FlipY(Color[] pixels)
    {
        Color[] result = new Color[pixels.Length];

        for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
                result[(resolution - 1 - y) * resolution + x] = pixels[y * resolution + x];

        return result;
    }

    void Import(string path)
    {
        path = path.Replace('\\', '/');

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (!importer) return;

        importer.textureShape = TextureImporterShape.TextureCube;
        importer.generateCubemap = TextureImporterGenerateCubemap.FullCubemap;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.mipmapEnabled = true;
        importer.streamingMipmaps = true;
        importer.isReadable = false;

        TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
        standalone.overridden = true;
        standalone.maxTextureSize = resolution;
        standalone.format = compression == Compression.BC1 ? TextureImporterFormat.DXT1 : TextureImporterFormat.BC7;

        importer.SetPlatformTextureSettings(standalone);
        importer.SaveAndReimport();
    }
#endif
}
