# Reactive Snow Surface System

Unity 6 URP에서 구현한 GPU 기반 반응형 눈 표면 시스템입니다. 캐릭터의 접촉과 이동 경로를 Compute Shader로 기록하고, 제한된 GPU Page를 실제 상호작용 영역에만 할당합니다.

![Reactive Snow Surface](Docs/Images/00_overview.png)

## Preview

### Procedural Snow Surface

![Procedural Snow Surface](Docs/Images/02_surface_detail.png)

여러 크기의 절차적 Noise로 적설 높이를 구성하고, Sparkle은 플레이어와의 거리에 따라 제한합니다.

### Surface Recovery

| Recorded | Recovering | Recovered |
| :---: | :---: | :---: |
| ![Recorded State](Docs/Images/03_recovery_before.png) | ![Recovering State](Docs/Images/03_recovery_progress.png) | ![Recovered State](Docs/Images/03_recovery_after.png) |

흔적은 픽셀별 Age를 기준으로 유지되며, Lifetime 이후 점진적으로 복원됩니다.

### Multi-Agent Test

![Multi-Agent Test](Docs/Images/04_multi_agent.png)

플레이어의 흔적을 우선 보존하고, NPC 접촉 검사는 플레이어와의 거리 및 이동 여부에 따라 조절합니다.

## How It Works

```text
SurfaceContactAgent
        ↓
SurfaceContactSystem
        ↓
SnowInteractor → SurfaceBrush
        ↓
SnowSurface → SnowStateStorage
        ↓
SnowState.compute → SnowSurfaceState.shader
```

접촉 검출, Brush 생성, Page 관리와 GPU 상태 갱신을 분리했습니다. `SnowInteractor`는 이전 위치와 현재 위치를 하나의 Stroke로 전달하여 빠른 이동에서도 흔적이 끊기지 않도록 처리합니다.

## Key Features

- 절차적 Noise 기반 적설 높이와 거리 제한 Sparkle
- 접촉 영역에만 할당되는 `Texture2DArray` Page
- Page 경계를 잇는 연속 Brush Stroke
- 우선순위와 보호 시간을 반영한 Page 재사용
- 픽셀별 Age 기반 흔적 복원
- Spatial Hash와 거리 단계화를 이용한 다중 Agent 접촉 처리
- 공중 상태에서 흔적 생성을 중단하는 Jump Filtering

## GPU State

| Channel | Value |
| :---: | --- |
| R | Depression |
| G | Compression |
| B | Displacement |
| A | Reserved |

| Setting | Default |
| --- | ---: |
| Page Size | `8 m` |
| Page Resolution | `512 × 512` |
| Page Capacity | `32 slices` |
| State Format | `R8G8B8A8_UNorm` |
| Age Format | `R16_SFloat` |
| Footprint Lifetime | `5 sec` |
| Fade Duration | `2 sec` |
| Recovery Interval | `0.25 sec` |

Compute Shader는 전체 Page가 아닌 Brush가 차지하는 픽셀 범위만 Dispatch합니다. 복원이 끝난 Page는 Slice를 반환해 장기적인 상태 누적을 방지합니다.

## Profiling

![200 Agent Profiling](Docs/Images/05_profiler_200_agents.png)

Unity Editor에서 200 Agent를 200 × 200 영역에 배치한 측정 결과입니다.

| Metric | Result |
| --- | ---: |
| Scripts Mean | `0.484 ms` |
| Frame Median | `2.226 ms` |
| Frame Max | `3.261 ms` |
| GC Collect | `0 ms` |

측정값에는 Editor와 VSync 대기 시간이 포함되어 있으며, 구조 변경 전후의 상대 비교 기준으로 사용했습니다.

## Core Code

- [`SnowStateStorage.cs`](Scripts/SnowStateStorage.cs) — GPU Page 할당, 재사용, Brush Dispatch와 복원
- [`SurfaceContactSystem.cs`](Scripts/SurfaceContactSystem.cs) — Spatial Hash와 다중 Agent 접촉 갱신
- [`SnowSurface.cs`](Scripts/SnowSurface.cs) — Surface 좌표, 영향 Page와 Shader Page Map 관리
- [`SnowState.compute`](Shaders/SnowState.compute) — Stroke 기록과 픽셀별 상태 복원
- [`SnowSurfaceState.shader`](Shaders/SnowSurfaceState.shader) — 적설 변위, 색상, Normal과 Sparkle 표현

## Setup

1. Scene에 `SurfaceContactSystem`과 `SnowStateStorage`를 추가하고 Compute Shader를 지정합니다.
2. 눈 Mesh에 Snow Material과 `SnowSurface`를 추가합니다.
3. 접촉 대상에 Collider, `SurfaceContactAgent`, `SnowInteractor`를 추가합니다.
4. `SnowInteractor`에 Storage와 `SnowStampProfile`을 지정합니다.

## Environment

- Unity 6
- Universal Render Pipeline
- C# / HLSL / Compute Shader
