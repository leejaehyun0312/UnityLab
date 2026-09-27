using UnityEngine;

[System.Serializable]
public sealed class SnowHeightField
{
    [SerializeField] Vector2 minXZ;
    [SerializeField] Vector2 maxXZ;
    [SerializeField] int width;
    [SerializeField] int height;
    [SerializeField] float[] heights;
    [SerializeField] bool[] valid;

    public bool IsValid => heights != null && valid != null && width > 1 && height > 1 && heights.Length == width * height && valid.Length == width * height;

    public void Set(Bounds bounds, int fieldWidth, int fieldHeight, float[] sourceHeights, bool[] sourceValid)
    {
        minXZ = new Vector2(bounds.min.x, bounds.min.z);
        maxXZ = new Vector2(bounds.max.x, bounds.max.z);
        width = fieldWidth;
        height = fieldHeight;
        heights = (float[])sourceHeights.Clone();
        valid = (bool[])sourceValid.Clone();
    }

    public bool TrySample(Vector3 worldPosition, out float result)
    {
        result = 0f;
        if (!IsValid) return false;
        float sizeX = maxXZ.x - minXZ.x;
        float sizeZ = maxXZ.y - minXZ.y;
        if (sizeX <= 0.001f || sizeZ <= 0.001f) return false;
        float tx = (worldPosition.x - minXZ.x) / sizeX;
        float tz = (worldPosition.z - minXZ.y) / sizeZ;
        if (tx < 0f || tx > 1f || tz < 0f || tz > 1f) return false;

        float gx = tx * (width - 1);
        float gz = tz * (height - 1);
        int x0 = Mathf.FloorToInt(gx);
        int z0 = Mathf.FloorToInt(gz);
        int x1 = Mathf.Min(x0 + 1, width - 1);
        int z1 = Mathf.Min(z0 + 1, height - 1);
        float fx = gx - x0;
        float fz = gz - z0;
        float sum = 0f;
        float weight = 0f;
        Accumulate(x0, z0, (1f - fx) * (1f - fz), ref sum, ref weight);
        Accumulate(x1, z0, fx * (1f - fz), ref sum, ref weight);
        Accumulate(x0, z1, (1f - fx) * fz, ref sum, ref weight);
        Accumulate(x1, z1, fx * fz, ref sum, ref weight);
        if (weight <= 0.0001f) return false;
        result = sum / weight;
        return true;
    }

    public void Clear()
    {
        minXZ = maxXZ = default;
        width = height = 0;
        heights = null;
        valid = null;
    }

    void Accumulate(int x, int z, float sampleWeight, ref float sum, ref float weight)
    {
        if (sampleWeight <= 0f) return;
        int index = z * width + x;
        if (!valid[index]) return;
        sum += heights[index] * sampleWeight;
        weight += sampleWeight;
    }
}

public readonly struct SurfaceBrush
{
    public Vector3 PreviousPosition { get; }
    public Vector3 Position { get; }
    public Vector3 Direction { get; }
    public float Pressure { get; }
    public SnowStampProfile Profile { get; }
    public int Priority { get; }

    public SurfaceBrush(Vector3 previousPosition, Vector3 position, Vector3 direction, float pressure, SnowStampProfile profile, int priority = 0)
    {
        PreviousPosition = previousPosition;
        Position = position;
        Direction = direction;
        Pressure = pressure;
        Profile = profile;
        Priority = priority;
    }

    public SurfaceBrush(Vector3 position, Vector3 direction, float pressure, SnowStampProfile profile, int priority = 0)
        : this(position, position, direction, pressure, profile, priority) { }
}

public readonly struct SnowBrushStroke
{
    public SnowStatePage Page { get; }
    public Vector2 Start { get; }
    public Vector2 End { get; }
    public Vector2 Direction { get; }
    public Vector2 Size { get; }
    public Vector2 PageMin { get; }
    public Vector2 PageSize { get; }
    public float Pressure { get; }
    public SnowStampProfile Profile { get; }
    public int Priority { get; }

    public SnowBrushStroke(SnowStatePage page, Vector2 start, Vector2 end, Vector2 direction, Vector2 size, Vector2 pageMin, Vector2 pageSize, float pressure, SnowStampProfile profile, int priority)
    {
        Page = page;
        Start = start;
        End = end;
        Direction = direction;
        Size = size;
        PageMin = pageMin;
        PageSize = pageSize;
        Pressure = pressure;
        Profile = profile;
        Priority = priority;
    }
}

public sealed class SnowStatePage
{
    public SnowSurface Surface { get; }
    public Vector2Int Coordinate { get; }
    public int SliceIndex { get; set; } = -1;
    public float LastUsedTime { get; set; }
    public float ProtectedUntil { get; set; }
    public float RecoveryEndTime { get; set; }
    public int Priority { get; set; }
    public bool IsAllocated => SliceIndex >= 0;

    public SnowStatePage(SnowSurface surface, Vector2Int coordinate)
    {
        Surface = surface;
        Coordinate = coordinate;
    }
}

public readonly struct SnowHeightSettings
{
    public readonly float BaseHeight, MinimumHeight, NoiseHeight, HeightDiversity;
    public readonly float MacroScale, MediumScale, DetailScale, DetailHeight;
    public readonly float AmplitudeScale, AmplitudeVariation, WarpScale, WarpStrength;
    public float MaximumHeight => Mathf.Max(MinimumHeight, BaseHeight + NoiseHeight + DetailHeight);

    public SnowHeightSettings(Material material)
    {
        BaseHeight = Get(material, "_SnowHeight", 0.15f);
        MinimumHeight = Get(material, "_MinimumHeight", 0.02f);
        NoiseHeight = Get(material, "_HeightVariation", 1.2f);
        HeightDiversity = Get(material, "_HeightContrast", 1.6f);
        MacroScale = Get(material, "_MacroScale", 0.03f);
        MediumScale = Get(material, "_MediumScale", 0.085f);
        DetailScale = Get(material, "_DetailScale", 0.45f);
        DetailHeight = Get(material, "_DetailHeight", 0.003f);
        AmplitudeScale = Get(material, "_AmplitudeScale", 0.012f);
        AmplitudeVariation = Get(material, "_AmplitudeVariation", 0.65f);
        WarpScale = Get(material, "_WarpScale", 0.018f);
        WarpStrength = Get(material, "_WarpStrength", 5f);
    }

    static float Get(Material material, string name, float fallback) => material && material.HasProperty(name) ? material.GetFloat(name) : fallback;
}

public static class SnowUtility
{
    public static Vector2 WorldToMeter(Transform transform, Bounds bounds, Vector3 position, float scaleX, float scaleZ)
    {
        Vector3 local = transform.InverseTransformPoint(position);
        return new Vector2((local.x - bounds.min.x) * scaleX, (local.z - bounds.min.z) * scaleZ);
    }

    public static Vector2 WorldDirectionToMeter(Transform transform, Vector3 direction, float scaleX, float scaleZ)
    {
        Vector3 local = transform.InverseTransformDirection(direction);
        Vector2 meterDirection = new(local.x * scaleX, local.z * scaleZ);
        return meterDirection.sqrMagnitude > 0.001f ? meterDirection.normalized : Vector2.up;
    }

    public static Vector2Int MeterToPage(Vector2 position, float pageSize, int pageCountX, int pageCountZ) => new(
        Mathf.Clamp(Mathf.FloorToInt(position.x / pageSize), 0, pageCountX - 1),
        Mathf.Clamp(Mathf.FloorToInt(position.y / pageSize), 0, pageCountZ - 1));

    public static void GetPageBounds(Vector2Int coordinate, float pageSize, float surfaceSizeX, float surfaceSizeZ, out Vector2 min, out Vector2 size)
    {
        min = new Vector2(coordinate.x * pageSize, coordinate.y * pageSize);
        Vector2 max = new(Mathf.Min(min.x + pageSize, surfaceSizeX), Mathf.Min(min.y + pageSize, surfaceSizeZ));
        size = max - min;
    }

    public static float SampleHeight(Vector2 worldXZ, SnowHeightSettings settings)
    {
        Vector2 warp = new(ValueNoise(worldXZ * settings.WarpScale + new Vector2(3.17f, 7.31f)), ValueNoise(worldXZ * settings.WarpScale + new Vector2(11.61f, 2.29f)));
        Vector2 warped = worldXZ + (warp * 2f - Vector2.one) * settings.WarpStrength;
        float macro = Fbm3(warped * settings.MacroScale);
        float medium = Fbm3(Rotate(warped, new Vector2(0.7986f, 0.6018f)) * settings.MediumScale + new Vector2(13.37f, 7.19f));
        float detail = ValueNoise(Rotate(warped, new Vector2(0.5446f, -0.8387f)) * settings.DetailScale + new Vector2(31.71f, 19.43f));
        float amplitude = Fbm3(worldXZ * settings.AmplitudeScale + new Vector2(47.17f, 23.73f));
        float height = macro * 0.62f + medium * 0.25f + amplitude * 0.13f;
        height = Mathf.Clamp01((height - 0.5f) * settings.HeightDiversity + 0.5f);
        height = height * height * (3f - 2f * height);
        float regionalVariation = Mathf.Lerp(1f - settings.AmplitudeVariation * 0.5f, 1f, amplitude);
        return Mathf.Max(settings.MinimumHeight, settings.BaseHeight + height * regionalVariation * settings.NoiseHeight + (detail * 2f - 1f) * settings.DetailHeight);
    }

    static float Fbm3(Vector2 position)
    {
        float value = ValueNoise(position) * 0.5714f;
        position = Rotate(position, new Vector2(0.7986f, 0.6018f)) * 2.03f + new Vector2(17.13f, 9.71f);
        value += ValueNoise(position) * 0.2857f;
        position = Rotate(position, new Vector2(0.5446f, -0.8387f)) * 2.01f + new Vector2(5.37f, 21.19f);
        return value + ValueNoise(position) * 0.1429f;
    }

    static float ValueNoise(Vector2 position)
    {
        Vector2 cell = new(Mathf.Floor(position.x), Mathf.Floor(position.y));
        Vector2 local = new(Frac(position.x), Frac(position.y));
        local = new Vector2(Fade(local.x), Fade(local.y));
        float bottom = Mathf.Lerp(Hash(cell), Hash(cell + Vector2.right), local.x);
        float top = Mathf.Lerp(Hash(cell + Vector2.up), Hash(cell + Vector2.one), local.x);
        return Mathf.Lerp(bottom, top, local.y);
    }

    static float Hash(Vector2 value)
    {
        value = new Vector2(Frac(value.x * 123.34f), Frac(value.y * 345.45f));
        value += Vector2.one * Vector2.Dot(value, value + Vector2.one * 34.345f);
        return Frac(value.x * value.y);
    }

    static Vector2 Rotate(Vector2 value, Vector2 direction) => new(value.x * direction.x - value.y * direction.y, value.x * direction.y + value.y * direction.x);
    static float Fade(float value) => value * value * value * (value * (value * 6f - 15f) + 10f);
    static float Frac(float value) => value - Mathf.Floor(value);
}
