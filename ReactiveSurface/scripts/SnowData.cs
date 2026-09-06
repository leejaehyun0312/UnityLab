using UnityEngine;

public readonly struct SurfaceBrush
{
    public Vector3 PreviousPosition { get; }
    public Vector3 Position { get; }
    public Vector3 Direction { get; }
    public float Pressure { get; }
    public SnowStampProfile Profile { get; }

    public SurfaceBrush(Vector3 previousPosition, Vector3 position, Vector3 direction, float pressure, SnowStampProfile profile)
    {
        PreviousPosition = previousPosition;
        Position = position;
        Direction = direction;
        Pressure = pressure;
        Profile = profile;
    }

    public SurfaceBrush(Vector3 position, Vector3 direction, float pressure, SnowStampProfile profile)
        : this(position, position, direction, pressure, profile) { }
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

    public SnowBrushStroke(SnowStatePage page, Vector2 start, Vector2 end, Vector2 direction, Vector2 size, Vector2 pageMin, Vector2 pageSize, float pressure, SnowStampProfile profile)
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
    }
}

public class SnowTile
{
    public Vector2Int Coordinate { get; }

    public SnowTile(Vector2Int coordinate)
    {
        Coordinate = coordinate;
    }
}

public class SnowStatePage
{
    public SnowSurface Surface { get; }
    public Vector2Int Coordinate { get; }
    public Vector2Int TileCoordinate { get; }
    public int SliceIndex { get; internal set; } = -1;
    public float LastUsedTime { get; internal set; }

    public bool IsAllocated => SliceIndex >= 0;

    public SnowStatePage(SnowSurface surface, Vector2Int coordinate, Vector2Int tileCoordinate)
    {
        Surface = surface;
        Coordinate = coordinate;
        TileCoordinate = tileCoordinate;
    }
}