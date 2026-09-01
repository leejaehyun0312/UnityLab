# WorldView Box

> **Visual Reference:** Genshin Impact - 공간의 신전 지역의 거울/이공간 표현  
> 원본 게임의 내부 구현을 복제한 것이 아니라, 시각적 특징을 분석해 Unity URP에서 Cubemap 기반으로 재구성한 기술 프로토타입입니다.

![Final Result](Docs/Images/result.png)

## Goal

거울 또는 경계면 너머에 별도의 공간이 존재하는 듯한 표현을 구현하고,  
실시간 Cubemap 방식과 정적 Baked Cubemap 방식을 비교해 품질과 런타임 비용의 균형점을 찾는 것이 목표였습니다.

## Reference

| Visual Reference | Unity Implementation |
|---|---|
| ![](Docs/Images/reference_genshin.png) | ![](Docs/Images/result.png) |

- Reference: **Genshin Impact - 공간의 신전**
- 관찰 포인트: 경계면 너머의 공간감, 내부 월드 표현, 유리/거울 계열 표면
- 실제 게임의 렌더링 구조를 단정하지 않고, 동일한 시각적 목적을 Cubemap 기반으로 재설계

## Implementation

### 1. Runtime Cubemap Prototype

초기 구현은 별도 Camera에서 `Camera.RenderToCubemap()`을 호출해 Alternate World를 실시간 캡처했습니다.

```text
Main Camera
    +
Alternative Camera
    ↓
RenderToCubemap
    ↓
Cubemap RenderTexture
    ↓
WorldView Shader
```

동적인 월드를 그대로 표현할 수 있었지만, Alternate World를 Cubemap의 6방향으로 반복 렌더링하는 비용이 발생했습니다.

### 2. Static Cubemap Baking

정적 환경은 런타임에서 반복 렌더링할 필요가 없다고 판단해 Editor Bake 방식으로 전환했습니다.

![Cubemap Baker](Docs/Images/baker_inspector.png)

```text
Alternate World
      ↓
CubemapBaker
      ↓
1024 Cubemap
      ↓
BC1 / BC7 Import
      ↓
CubemapSurface
```

`CubemapBaker`는 Editor에서 Camera를 6방향으로 캡처하고 PNG Cubemap으로 저장합니다.  
런타임에서는 `Camera.RenderToCubemap()` 없이 미리 생성한 Cubemap만 샘플링합니다.

### 3. GlassWorld Shader

하나의 Cube Mesh에서 두 개의 Pass를 사용합니다.

- **World Pass**: `Cull Front`로 내부 Back Face에 Cubemap을 출력
- **Glass Pass**: `Cull Back`으로 Front Face에 Fresnel 기반 유리 표현
- **MaterialPropertyBlock**: 공유 Material을 유지하면서 오브젝트별 Cubemap 적용

## Memory Optimization

512 해상도에서는 근거리에서 공간 디테일 손실이 크게 보였기 때문에 1024 해상도를 유지했습니다.  
대신 Alpha가 필요 없는 정적 Cubemap에 **BC1(DXT1) 4bpp 블록 압축**을 적용했습니다.

![BC1 Import](Docs/Images/bc1_import.png)

| Format | Approx. Memory |
|---|---:|
| RGBA32 + Mipmap | ~32 MB |
| BC1 + Mipmap | **~4 MB** |
| BC7 + Mipmap | ~8 MB |

BC1 압축 손실이 눈에 띄는 월드는 `CubemapBaker`의 Compression을 `BC7`으로 변경할 수 있습니다.

## Profiling

동일한 테스트 장면을 Development Build에서 측정하고 안정 구간을 비교했습니다.

| Metric | Runtime Cubemap | Baked Cubemap |
|---|---:|---:|
| Median Frame Time | **5.913 ms** | **0.760 ms** |
| Rendering CPU Mean | **2.169 ms** | **0.169 ms** |
| Runtime Capture | `Camera.RenderToCubemap()` | None |
| Static Cubemap Memory | RenderTexture required | **~4 MB (1024 BC1)** |
| Dynamic World | Supported | Not supported |

### Runtime Cubemap

![Runtime Profile](Docs/Images/profile_runtime.png)

### Baked Cubemap

![Baked Profile](Docs/Images/profile_baked.png)

> 측정 결과는 현재 테스트 장면과 하드웨어 기준이며, 장면 복잡도에 따라 달라질 수 있습니다. GPU Frame Time은 이번 비교에서 제외했습니다.

## Dynamic Cubemap Experiment

동적 환경을 유지하기 위해 Temporal Cubemap Sequence도 테스트했습니다.

```text
Base Cubemap          ~ 4 MB
8 Dynamic Frames      ~ 32 MB
Total / Dynamic World ~ 36 MB
```

8프레임만 사용해도 월드 하나당 약 36MB가 필요했고, 프레임 수를 늘릴수록 메모리가 선형 증가했습니다.  
근거리 움직임 품질 역시 실시간 Cubemap보다 낮아 최종 구조에서는 제외했습니다.

## Final Decision

```text
Static World
→ Baked Cubemap
→ 1024 BC1
→ Runtime 6-direction capture removed

Dynamic World
→ Runtime Rendering / Actual Geometry
```

정적 환경에서는 약 4MB의 텍스처 메모리를 사용하는 대신 반복 렌더링 비용을 제거하는 편이 효율적이라고 판단했습니다.

## Project Structure

```text
Runtime/
└─ CubemapSurface.cs

Editor/
├─ CubemapBaker.cs
└─ CubemapBakerEditor.cs

Shaders/
└─ GlassWorld.shader

Docs/
├─ IMAGE_GUIDE.md
└─ Images/
```

프로토타이핑 과정에서 사용했던 Runtime World Renderer, View Mapping, Dynamic Cubemap Sequence 관련 코드는 최종 포트폴리오 구조에서 제외했습니다.

## Environment

- Unity 6
- Universal Render Pipeline
- Standalone
- BC1(DXT1) / BC7
