using UnityEngine;

public static class SnowSurfaceUtility
{
    public static Vector2 WorldToMeter(Transform transform, Bounds bounds, Vector3 worldPosition, float scaleX, float scaleZ)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        return new Vector2((local.x - bounds.min.x) * scaleX, (local.z - bounds.min.z) * scaleZ);
    }

    public static Vector2 WorldDirectionToMeter(Transform transform, Vector3 worldDirection, float scaleX, float scaleZ)
    {
        Vector3 local = transform.InverseTransformDirection(worldDirection);
        Vector2 direction = new(local.x * scaleX, local.z * scaleZ);
        return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.up;
    }

    public static Vector2Int MeterToPage(Vector2 position, float pageSize, int pageCountX, int pageCountZ)
    {
        return new Vector2Int(
            Mathf.Clamp(Mathf.FloorToInt(position.x / pageSize), 0, pageCountX - 1),
            Mathf.Clamp(Mathf.FloorToInt(position.y / pageSize), 0, pageCountZ - 1));
    }

    public static Vector2Int PageToTile(Vector2Int page, float pageSize, float tileSize, int tileCountX, int tileCountZ)
    {
        Vector2 center = new((page.x + 0.5f) * pageSize, (page.y + 0.5f) * pageSize);
        return new Vector2Int(
            Mathf.Clamp(Mathf.FloorToInt(center.x / tileSize), 0, tileCountX - 1),
            Mathf.Clamp(Mathf.FloorToInt(center.y / tileSize), 0, tileCountZ - 1));
    }

    public static void GetPageBounds(Vector2Int coordinate, float pageSize, float surfaceSizeX, float surfaceSizeZ, out Vector2 min, out Vector2 size)
    {
        min = new Vector2(coordinate.x * pageSize, coordinate.y * pageSize);
        Vector2 max = new(Mathf.Min(min.x + pageSize, surfaceSizeX), Mathf.Min(min.y + pageSize, surfaceSizeZ));
        size = max - min;
    }
}
