using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class SnowSurface : MonoBehaviour
{
    const float MinScale = 0.001f;
    const int MaxPageCount = 256;

    [SerializeField, Range(4f, 64f)] float tileSize = 16f;
    [SerializeField, Range(1f, 8f)] float pageSize = 4f;

    readonly Dictionary<Vector2Int, SnowTile> tiles = new();
    readonly Dictionary<Vector2Int, SnowStatePage> pages = new();
    readonly List<SnowBrushStroke> brushBuffer = new();

    Renderer targetRenderer;
    Bounds localBounds;
    MaterialPropertyBlock properties;
    Texture2D pageSliceMap;
    float[] pageSlices;

    float ScaleX => Mathf.Max(transform.TransformVector(Vector3.right).magnitude, MinScale);
    float ScaleZ => Mathf.Max(transform.TransformVector(Vector3.forward).magnitude, MinScale);
    float WorldSizeX => localBounds.size.x * ScaleX;
    float WorldSizeZ => localBounds.size.z * ScaleZ;
    float LocalPageSizeX => pageSize / ScaleX;
    float LocalPageSizeZ => pageSize / ScaleZ;

    public int TileCountX => Mathf.Max(1, Mathf.CeilToInt(WorldSizeX / tileSize));
    public int TileCountZ => Mathf.Max(1, Mathf.CeilToInt(WorldSizeZ / tileSize));
    public int PageCountX => Mathf.Clamp(Mathf.CeilToInt(WorldSizeX / pageSize), 1, MaxPageCount);
    public int PageCountZ => Mathf.Clamp(Mathf.CeilToInt(WorldSizeZ / pageSize), 1, MaxPageCount);
    public float TileSize => tileSize;
    public float PageSize => pageSize;
    public IReadOnlyDictionary<Vector2Int, SnowTile> Tiles => tiles;
    public IReadOnlyDictionary<Vector2Int, SnowStatePage> Pages => pages;

    void OnEnable() => Initialize();
    void OnDisable() => ReleasePageMap();

    void Initialize()
    {
        targetRenderer = GetComponent<Renderer>();
        localBounds = targetRenderer.localBounds;
        properties ??= new MaterialPropertyBlock();
        tiles.Clear();
        pages.Clear();
        CreatePageMap();
        ApplyShaderData();
    }

    void CreatePageMap()
    {
        ReleasePageMap();
        pageSlices = new float[PageCountX * PageCountZ];
        for (int i = 0; i < pageSlices.Length; i++) pageSlices[i] = -1f;

        pageSliceMap = new Texture2D(PageCountX, PageCountZ, GraphicsFormat.R32_SFloat, TextureCreationFlags.None)
        {
            name = $"{name}_SnowPageMap",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        pageSliceMap.SetPixelData(pageSlices, 0);
        pageSliceMap.Apply(false, false);
    }

    void ReleasePageMap()
    {
        if (!pageSliceMap) return;
        if (Application.isPlaying) Destroy(pageSliceMap);
        else DestroyImmediate(pageSliceMap);
        pageSliceMap = null;
    }

    void ApplyShaderData()
    {
        targetRenderer.GetPropertyBlock(properties);
        properties.SetTexture("_PageSliceMap", pageSliceMap);
        properties.SetVector("_SurfaceMin", new Vector4(localBounds.min.x, localBounds.min.z, 0f, 0f));
        properties.SetVector("_SurfaceMax", new Vector4(localBounds.max.x, localBounds.max.z, 0f, 0f));
        properties.SetVector("_LocalPageSize", new Vector4(LocalPageSizeX, LocalPageSizeZ, 0f, 0f));
        properties.SetInt("_PageCountX", PageCountX);
        properties.SetInt("_PageCountZ", PageCountZ);
        targetRenderer.SetPropertyBlock(properties);
    }

    public IReadOnlyList<SnowBrushStroke> CreateBrushStrokes(SurfaceBrush brush)
    {
        brushBuffer.Clear();
        float scaleX = ScaleX;
        float scaleZ = ScaleZ;
        float worldSizeX = WorldSizeX;
        float worldSizeZ = WorldSizeZ;

        Vector2 start = SnowSurfaceUtility.WorldToMeter(transform, localBounds, brush.PreviousPosition, scaleX, scaleZ);
        Vector2 end = SnowSurfaceUtility.WorldToMeter(transform, localBounds, brush.Position, scaleX, scaleZ);
        Vector2 direction = SnowSurfaceUtility.WorldDirectionToMeter(transform, brush.Direction, scaleX, scaleZ);
        Vector2 right = new(direction.y, -direction.x);
        Vector2 halfSize = brush.Profile.Size * 0.5f;

        float extentX = Mathf.Abs(right.x) * halfSize.x + Mathf.Abs(direction.x) * halfSize.y;
        float extentZ = Mathf.Abs(right.y) * halfSize.x + Mathf.Abs(direction.y) * halfSize.y;
        Vector2 extent = new(extentX, extentZ);
        Vector2 min = Vector2.Min(start, end) - extent;
        Vector2 max = Vector2.Max(start, end) + extent;

        if (max.x < 0f || max.y < 0f || min.x > worldSizeX || min.y > worldSizeZ) return brushBuffer;

        min.x = Mathf.Clamp(min.x, 0f, worldSizeX);
        min.y = Mathf.Clamp(min.y, 0f, worldSizeZ);
        max.x = Mathf.Clamp(max.x, 0f, worldSizeX);
        max.y = Mathf.Clamp(max.y, 0f, worldSizeZ);

        Vector2Int minPage = SnowSurfaceUtility.MeterToPage(min, pageSize, PageCountX, PageCountZ);
        Vector2Int maxPage = SnowSurfaceUtility.MeterToPage(max, pageSize, PageCountX, PageCountZ);

        for (int z = minPage.y; z <= maxPage.y; z++)
        {
            for (int x = minPage.x; x <= maxPage.x; x++)
            {
                Vector2Int coordinate = new(x, z);
                SnowStatePage page = GetOrCreatePage(coordinate);
                SnowSurfaceUtility.GetPageBounds(coordinate, pageSize, worldSizeX, worldSizeZ, out Vector2 pageMin, out Vector2 pageSizeMeters);
                brushBuffer.Add(new SnowBrushStroke(page, start, end, direction, brush.Profile.Size, pageMin, pageSizeMeters, Mathf.Clamp01(brush.Pressure), brush.Profile));
            }
        }

        return brushBuffer;
    }

    SnowStatePage GetOrCreatePage(Vector2Int coordinate)
    {
        if (pages.TryGetValue(coordinate, out SnowStatePage page)) return page;

        Vector2Int tileCoordinate = SnowSurfaceUtility.PageToTile(coordinate, pageSize, tileSize, TileCountX, TileCountZ);
        if (!tiles.ContainsKey(tileCoordinate)) tiles.Add(tileCoordinate, new SnowTile(tileCoordinate));

        page = new SnowStatePage(this, coordinate, tileCoordinate);
        pages.Add(coordinate, page);
        return page;
    }

    public void SyncPageSlice(SnowStatePage page)
    {
        int index = page.Coordinate.y * PageCountX + page.Coordinate.x;
        if (Mathf.Approximately(pageSlices[index], page.SliceIndex)) return;

        pageSlices[index] = page.SliceIndex;
        pageSliceMap.SetPixelData(pageSlices, 0);
        pageSliceMap.Apply(false, false);
    }
}
