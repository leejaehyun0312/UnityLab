using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class CubemapSurface : MonoBehaviour
{
    [SerializeField] Cubemap cubemap;
    [SerializeField] string propertyName = "_WorldCube";

    Renderer targetRenderer;
    MaterialPropertyBlock block;
    int propertyId;

    public Cubemap Cubemap => cubemap;

    void Awake()
    {
        targetRenderer = GetComponent<Renderer>();
        block = new MaterialPropertyBlock();
        propertyId = Shader.PropertyToID(propertyName);
        Apply();
    }

    public void SetCubemap(Cubemap value)
    {
        if (cubemap == value) return;
        cubemap = value;
        Apply();
    }

    void Apply()
    {
        targetRenderer.GetPropertyBlock(block);
        block.SetTexture(propertyId, cubemap);
        targetRenderer.SetPropertyBlock(block);
    }
}
