using System.Collections.Generic;
using Unity.Profiling;
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
    [SerializeField, Min(0f)] float depressionRecovery = 0.7f;
    [SerializeField, Min(0f)] float compressionRecovery = 1f;
    [SerializeField, Min(0f)] float displacementRecovery = 2f;

    [Header("Page Lifetime")]
    [SerializeField, Min(0.05f)] float activeLease = 0.5f;

    static readonly ProfilerMarker BrushMarker = new("ReactiveSnow.ApplyBrush");
    static readonly ProfilerMarker RecoveryMarker = new("ReactiveSnow.RecoverActivePages");

    readonly Stack<int> freeSlices = new();
    readonly List<SnowStatePage> allocatedPages = new();

    RenderTexture stateTexture;
    RenderTexture ageTexture;
    ComputeBuffer activeSliceBuffer;
    int[] activeSlices;

    int clearKernel;
    int brushKernel;
    int recoveryKernel;
    float recoveryTimer;

    public int AllocatedPageCount => allocatedPages.Count;
    public int FreePageCount => freeSlices.Count;
    public int RejectedStampCount { get; private set; }
    public int LastRecoveryPageCount { get; private set; }
    public int BrushDispatchCount { get; private set; }

    void Awake()
    {
        if (!stateCompute)
        {
            Debug.LogError("SnowStateStorage에 Compute Shader를 지정해야 합니다.", this);
            enabled = false;
            return;
        }

        clearKernel = stateCompute.FindKernel("ClearSlice");
        brushKernel = stateCompute.FindKernel("ApplyBrush");
        recoveryKernel = stateCompute.FindKernel("RecoverState");

        stateTexture = CreateArrayTexture(GraphicsFormat.R8G8B8A8_UNorm, "SnowState");
        ageTexture = CreateArrayTexture(GraphicsFormat.R16_SFloat, "SnowAge");
        activeSliceBuffer = new ComputeBuffer(pageCapacity, sizeof(int), ComputeBufferType.Structured);
        activeSlices = new int[pageCapacity];

        Shader.SetGlobalTexture("_SnowState", stateTexture);
        Shader.SetGlobalFloat("_SnowStateResolution", pageResolution);

        for (int i = pageCapacity - 1; i >= 0; i--) freeSlices.Push(i);
    }

    void Update()
    {
        recoveryTimer += Time.deltaTime;
        if (recoveryTimer < recoveryInterval) return;

        Recover(recoveryTimer);
        recoveryTimer = 0f;
    }

    void OnDestroy()
    {
        ReleaseTexture(stateTexture);
        ReleaseTexture(ageTexture);
        activeSliceBuffer?.Release();
    }

    public void ApplyBrush(SnowSurface surface, SurfaceBrush brush)
    {
        IReadOnlyList<SnowBrushStroke> strokes = surface.CreateBrushStrokes(brush);

        for (int i = 0; i < strokes.Count; i++)
        {
            SnowBrushStroke stroke = strokes[i];
            if (!TryAcquire(stroke.Page, stroke.Priority))
            {
                RejectedStampCount++;
                continue;
            }

            surface.SyncPageSlice(stroke.Page);
            ApplyStroke(stroke);
        }
    }

    bool TryAcquire(SnowStatePage page, int incomingPriority)
    {
        if (page.IsAllocated)
        {
            Touch(page, incomingPriority);
            return true;
        }

        if (!freeSlices.TryPop(out int slice))
        {
            SnowStatePage oldest = FindEvictionCandidate(incomingPriority);
            if (oldest == null) return false;

            slice = Evict(oldest);
        }

        page.SliceIndex = slice;
        allocatedPages.Add(page);

        ClearSlice(slice);
        Touch(page, incomingPriority);
        return true;
    }

    void Release(SnowStatePage page)
    {
        if (!page.IsAllocated) return;
        freeSlices.Push(Evict(page));
    }

    int Evict(SnowStatePage page)
    {
        int slice = page.SliceIndex;
        page.SliceIndex = -1;
        page.LastUsedTime = 0f;
        page.ProtectedUntil = 0f;
        page.RecoveryEndTime = 0f;
        page.Priority = 0;
        page.Surface.SyncPageSlice(page);
        allocatedPages.Remove(page);
        return slice;
    }

    void ApplyStroke(SnowBrushStroke stroke)
    {
        using ProfilerMarker.AutoScope _ = BrushMarker.Auto();

        Touch(stroke.Page, stroke.Priority);
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

        GetBrushPixelBounds(stroke, out Vector2Int min, out Vector2Int max);
        int width = max.x - min.x;
        int height = max.y - min.y;
        if (width <= 0 || height <= 0) return;

        stateCompute.SetInt("_DispatchOffsetX", min.x);
        stateCompute.SetInt("_DispatchOffsetY", min.y);
        stateCompute.Dispatch(brushKernel, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
        BrushDispatchCount++;
    }

    void Recover(float deltaTime)
    {
        if (allocatedPages.Count == 0) return;
        using ProfilerMarker.AutoScope _ = RecoveryMarker.Auto();

        int activeCount = allocatedPages.Count;
        for (int i = 0; i < activeCount; i++) activeSlices[i] = allocatedPages[i].SliceIndex;
        activeSliceBuffer.SetData(activeSlices, 0, 0, activeCount);

        stateCompute.SetInt("_Resolution", pageResolution);
        stateCompute.SetFloat("_DeltaTime", deltaTime);
        stateCompute.SetFloat("_FootprintLifetime", footprintLifetime);
        stateCompute.SetFloat("_FadeDuration", fadeDuration);
        stateCompute.SetFloat("_DepressionRecovery", depressionRecovery);
        stateCompute.SetFloat("_CompressionRecovery", compressionRecovery);
        stateCompute.SetFloat("_DisplacementRecovery", displacementRecovery);
        stateCompute.SetTexture(recoveryKernel, "_SnowState", stateTexture);
        stateCompute.SetTexture(recoveryKernel, "_SnowAge", ageTexture);
        stateCompute.SetBuffer(recoveryKernel, "_ActiveSlices", activeSliceBuffer);

        int groups = Mathf.CeilToInt(pageResolution / 8f);
        stateCompute.Dispatch(recoveryKernel, groups, groups, activeCount);
        LastRecoveryPageCount = activeCount;

        float now = Time.time;
        for (int i = allocatedPages.Count - 1; i >= 0; i--)
        {
            SnowStatePage page = allocatedPages[i];
            if (now < page.ProtectedUntil || now < page.RecoveryEndTime) continue;
            Release(page);
        }
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

    void Touch(SnowStatePage page, int incomingPriority)
    {
        float now = Time.time;
        page.LastUsedTime = now;
        page.ProtectedUntil = now + activeLease;
        page.RecoveryEndTime = now + GetMaximumRecoveryTime();
        page.Priority = Mathf.Max(page.Priority, incomingPriority);
    }

    SnowStatePage FindEvictionCandidate(int incomingPriority)
    {
        SnowStatePage cold = null;
        SnowStatePage warm = null;
        SnowStatePage emergency = null;
        float now = Time.time;

        for (int i = 0; i < allocatedPages.Count; i++)
        {
            SnowStatePage candidate = allocatedPages[i];
            if (candidate.Priority > incomingPriority) continue;

            if (now >= candidate.RecoveryEndTime)
            {
                SelectOlderPage(ref cold, candidate);
                continue;
            }

            if (candidate.Priority >= incomingPriority) continue;
            if (now >= candidate.ProtectedUntil) SelectOlderPage(ref warm, candidate);
            else SelectOlderPage(ref emergency, candidate);
        }

        return cold ?? warm ?? emergency;
    }

    void SelectOlderPage(ref SnowStatePage selected, SnowStatePage candidate)
    {
        if (selected == null ||
            candidate.Priority < selected.Priority ||
            candidate.Priority == selected.Priority && candidate.LastUsedTime < selected.LastUsedTime)
            selected = candidate;
    }

    float GetMaximumRecoveryTime()
    {
        float slowest = Mathf.Min(depressionRecovery, Mathf.Min(compressionRecovery, displacementRecovery));
        return slowest <= 0.0001f ? float.PositiveInfinity : footprintLifetime + fadeDuration / slowest;
    }

    void GetBrushPixelBounds(SnowBrushStroke stroke, out Vector2Int min, out Vector2Int max)
    {
        float radius = stroke.Size.magnitude * 0.5f;

        Vector2 worldMin = Vector2.Min(stroke.Start, stroke.End) - Vector2.one * radius;
        Vector2 worldMax = Vector2.Max(stroke.Start, stroke.End) + Vector2.one * radius;

        Vector2 uvMin = new(
            (worldMin.x - stroke.PageMin.x) / stroke.PageSize.x,
            (worldMin.y - stroke.PageMin.y) / stroke.PageSize.y);
        Vector2 uvMax = new(
            (worldMax.x - stroke.PageMin.x) / stroke.PageSize.x,
            (worldMax.y - stroke.PageMin.y) / stroke.PageSize.y);

        min = new Vector2Int(
            Mathf.Clamp(Mathf.FloorToInt(uvMin.x * pageResolution), 0, pageResolution),
            Mathf.Clamp(Mathf.FloorToInt(uvMin.y * pageResolution), 0, pageResolution));
        max = new Vector2Int(
            Mathf.Clamp(Mathf.CeilToInt(uvMax.x * pageResolution), 0, pageResolution),
            Mathf.Clamp(Mathf.CeilToInt(uvMax.y * pageResolution), 0, pageResolution));
    }
}
