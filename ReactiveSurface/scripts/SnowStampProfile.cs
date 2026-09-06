using UnityEngine;

[CreateAssetMenu(menuName = "Reactive Snow/Brush Profile", fileName = "SnowBrushProfile")]
public class SnowStampProfile : ScriptableObject
{
    [SerializeField] Vector2 size = new(0.11f, 0.26f);
    [SerializeField, Range(0.01f, 1f)] float falloff = 0.3f;

    [Header("Response")]
    [SerializeField, Range(0f, 1f)] float depressionResponse = 1f;
    [SerializeField, Range(0f, 1f)] float compressionResponse = 0.6f;
    [SerializeField, Range(0f, 1f)] float displacementResponse = 0.15f;

    public Vector2 Size => size;
    public float Falloff => falloff;
    public float DepressionResponse => depressionResponse;
    public float CompressionResponse => compressionResponse;
    public float DisplacementResponse => displacementResponse;
}