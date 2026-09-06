using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class SnowStateStorage : MonoBehaviour
{
    [SerializeField] ComputeShader stateCompute;
    [SerializeField, Min(64)] int pageResolution = 512;
    [SerializeField, Min(1)] int pageCapacity = 32;

    [Header("Recovery")]
    [SerializeField, Min(0f)] float footprintLifetime = 5f;
    [SerializeField, Min(0.1f)] float fadeDuration = 2f;
    [SerializeField, Min(0.05f)] float recoveryInterval = 0.25f;

    readonly Stack<int> freeSlices = new();
    readonly List<SnowStatePage> allocatedPages = new();

    RenderTexture stateTexture;
    RenderTexture ageTexture;

    int clearKernel;
    int brushKernel;
    int recoveryKernel;

    float recoveryTimer;

    void Awake()
    {
        clearKernel = stateCompute.FindKernel("ClearSlice");
        brushKernel = stateCompute.FindKernel("ApplyBrush");
        recoveryKernel = stateCompute.FindKernel("RecoverState");

        stateTexture = CreateArrayTexture(GraphicsFormat.R8G8B8A8_UNorm, "SnowState");
        ageTexture = CreateArrayTexture(GraphicsFormat.R16_SFloat, "SnowAge");

        Shader.SetGlobalTexture("_SnowState", stateTexture);
        Shader.SetGlobalFloat("_SnowStateResolution", pageResolution);

        for (int i = pageCapacity - 1; i >= 0; i--)
            freeSlices.Push(i);
    }

    void Update()
    {
        recoveryTimer += Time.deltaTime;

        if (recoveryTimer < recoveryInterval)
            return;

        Recover(recoveryTimer);
        recoveryTimer = 0f;
    }

    void OnDestroy()
    {
        ReleaseTexture(stateTexture);
        ReleaseTexture(ageTexture);
    }

    RenderTexture CreateArrayTexture(GraphicsFormat format, string textureName)
    {
        RenderTexture texture = new(pageResolution, pageResolution, 0)
        {
            name = textureName,
            dimension = TextureDimension.Tex2DArray,
            volumeDepth = pageCapacity,
            graphicsFormat = format,
            enableRandomWrite = true,
            useMipMap = false,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        texture.Create();
        return texture;
    }

    void ReleaseTexture(RenderTexture texture)
    {
        if (!texture) return;
        texture.Release();
    }

    public void ApplyBrush(SnowSurface surface, SurfaceBrush brush)
    {
        if (!surface || !brush.Profile) return;

        var strokes = surface.CreateBrushStrokes(brush);

        for (int i = 0; i < strokes.Count; i++)
        {
            SnowBrushStroke stroke = strokes[i];

            if (!TryAcquire(stroke.Page))
                continue;

            surface.SyncPageSlice(stroke.Page);
            ApplyStroke(stroke);
        }
    }

    bool TryAcquire(SnowStatePage page)
    {
        if (page.IsAllocated)
        {
            Touch(page);
            return true;
        }

        int slice;

        if (!freeSlices.TryPop(out slice))
        {
            SnowStatePage oldest = FindOldestPage();

            if (oldest == null)
                return false;

            slice = oldest.SliceIndex;

            oldest.SliceIndex = -1;
            oldest.Surface.SyncPageSlice(oldest);

            allocatedPages.Remove(oldest);
        }

        page.SliceIndex = slice;
        page.LastUsedTime = Time.time;

        allocatedPages.Add(page);

        ClearSlice(slice);
        return true;
    }

    public void Release(SnowStatePage page)
    {
        if (!page.IsAllocated) return;

        int slice = page.SliceIndex;

        page.SliceIndex = -1;
        page.LastUsedTime = 0f;

        page.Surface.SyncPageSlice(page);

        allocatedPages.Remove(page);
        freeSlices.Push(slice);
    }

    void ApplyStroke(SnowBrushStroke stroke)
    {
        Touch(stroke.Page);

        SnowStampProfile profile = stroke.Profile;

        stateCompute.SetInt("_Resolution", pageResolution);
        stateCompute.SetInt("_SliceIndex", stroke.Page.SliceIndex);

        stateCompute.SetVector("_PageMin", stroke.PageMin);
        stateCompute.SetVector("_PageSize", stroke.PageSize);

        stateCompute.SetVector("_BrushStart", stroke.Start);
        stateCompute.SetVector("_BrushEnd", stroke.End);
        stateCompute.SetVector("_BrushDirection", stroke.Direction);
        stateCompute.SetVector("_BrushSize", stroke.Size);

        stateCompute.SetFloat("_BrushPressure", stroke.Pressure);
        stateCompute.SetFloat("_BrushFalloff", profile.Falloff);

        stateCompute.SetFloat("_DepressionStrength", profile.DepressionResponse);
        stateCompute.SetFloat("_CompressionStrength", profile.CompressionResponse);
        stateCompute.SetFloat("_DisplacementStrength", profile.DisplacementResponse);

        stateCompute.SetTexture(brushKernel, "_SnowState", stateTexture);
        stateCompute.SetTexture(brushKernel, "_SnowAge", ageTexture);

        Dispatch(brushKernel);
    }

    void Recover(float deltaTime)
    {
        if (allocatedPages.Count == 0)
            return;

        stateCompute.SetInt("_Resolution", pageResolution);
        stateCompute.SetFloat("_DeltaTime", deltaTime);
        stateCompute.SetFloat("_FootprintLifetime", footprintLifetime);
        stateCompute.SetFloat("_FadeDuration", fadeDuration);

        stateCompute.SetTexture(recoveryKernel, "_SnowState", stateTexture);
        stateCompute.SetTexture(recoveryKernel, "_SnowAge", ageTexture);

        int groups = Mathf.CeilToInt(pageResolution / 8f);

        stateCompute.Dispatch(
            recoveryKernel,
            groups,
            groups,
            pageCapacity);
    }

    void Touch(SnowStatePage page)
    {
        page.LastUsedTime = Time.time;
    }

    SnowStatePage FindOldestPage()
    {
        if (allocatedPages.Count == 0)
            return null;

        SnowStatePage oldest = allocatedPages[0];

        for (int i = 1; i < allocatedPages.Count; i++)
        {
            if (allocatedPages[i].LastUsedTime < oldest.LastUsedTime)
                oldest = allocatedPages[i];
        }

        return oldest;
    }

    void ClearSlice(int slice)
    {
        stateCompute.SetInt("_Resolution", pageResolution);
        stateCompute.SetInt("_SliceIndex", slice);

        stateCompute.SetTexture(clearKernel, "_SnowState", stateTexture);
        stateCompute.SetTexture(clearKernel, "_SnowAge", ageTexture);

        Dispatch(clearKernel);
    }

    void Dispatch(int kernel)
    {
        int groups = Mathf.CeilToInt(pageResolution / 8f);
        stateCompute.Dispatch(kernel, groups, groups, 1);
    }
}