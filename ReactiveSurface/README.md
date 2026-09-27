# Reactive Snow Surface System

Unity 6 URP 환경에서 구현한 GPU 기반 반응형 눈 표면 시스템입니다.

캐릭터와 눈 표면의 접촉을 `SurfaceBrush`로 변환하고, 이동 궤적과 압력에 따른 변형을 Compute Shader로 누적합니다. 기록된 흔적은 일정 시간 유지된 뒤 픽셀별 경과 시간에 따라 자연스럽게 복원됩니다.

넓은 설원 전체에 고해상도 Render Texture를 할당하지 않고, 실제 상호작용이 발생한 영역에만 GPU Page를 할당하는 구조를 목표로 설계했습니다.

![Reactive Snow Surface](Docs/Images/00_overview.png)

---

## Demo

### Continuous Trail

`SurfaceContactAgent`가 캐릭터 Collider 하단과 눈 표면의 접촉을 확인하고, `SnowInteractor`가 이전 접촉 위치와 현재 위치를 하나의 Brush Stroke로 연결합니다. 빠른 이동이나 낮은 프레임에서도 개별 Stamp 사이가 끊어지는 현상을 줄였습니다.

### Procedural Snow Surface

![Procedural Snow Surface](Docs/Images/02_surface_detail.png)

여러 크기의 절차적 Noise를 조합하여 낮은 영역과 높은 적설 영역이 한 표면 안에서 자연스럽게 달라지도록 구성했습니다. 눈 표면의 Sparkle은 플레이어 주변 거리에서만 표현하여 넓은 지형 전체가 과도하게 반짝이는 문제를 줄였습니다.

### Surface Recovery

| Recorded | Recovering | Recovered |
| :---: | :---: | :---: |
| ![Recorded State](Docs/Images/03_recovery_before.png) | ![Recovering State](Docs/Images/03_recovery_progress.png) | ![Recovered State](Docs/Images/03_recovery_after.png) |

각 픽셀은 별도의 Age 값을 가집니다. 서로 다른 시점에 생성된 흔적이 한꺼번에 사라지지 않고, Lifetime 이후 채널별 복원 속도에 따라 점진적으로 감소합니다.

### Multi-Agent Test

![Multi-Agent Test](Docs/Images/04_multi_agent.png)

플레이어의 접촉 갱신과 흔적 우선순위를 보장하고, NPC는 플레이어와의 거리 및 이동 여부에 따라 접촉 검사를 조절합니다. 제한된 Page Capacity 안에서는 우선순위와 사용 시점을 기준으로 보존할 Page를 결정합니다.

---

## Core Architecture

```text
Collider Contact
      ↓
SurfaceContactAgent
      ↓
SurfaceContactSystem
      ↓
SnowInteractor → SurfaceBrush
      ↓
SnowSurface → Affected Pages
      ↓
SnowStateStorage
      ↓
Compute Shader → GPU Surface State
      ↓
Snow Surface Shader
```

접촉 검출, 흔적 생성, Page 관리, GPU 갱신을 분리했습니다.

- `SurfaceContactAgent`는 Collider 기반의 접촉 위치와 현재 접촉 결과를 제공합니다.
- `SurfaceContactSystem`은 Spatial Hash와 플레이어 중심 relevance를 사용하여 다수 Agent의 접촉 검사를 관리합니다.
- `SnowInteractor`는 접촉 결과를 연속적인 `SurfaceBrush`로 변환합니다.
- `SnowSurface`는 Brush가 영향을 주는 Page를 계산하고 Surface와 GPU Page의 연결 정보를 관리합니다.
- `SnowStateStorage`는 제한된 Texture Slice의 할당, 회수, Brush Dispatch와 복원을 담당합니다.

현재 기능에 필요한 책임만 분리하고, Page Pool이나 GPU Dispatcher를 별도 계층으로 과도하게 나누지는 않았습니다.

---

## Surface Brush

`SurfaceBrush`는 표면에 전달하는 최소 단위의 상호작용 데이터입니다.

```csharp
public readonly struct SurfaceBrush
{
    public Vector3 PreviousPosition { get; }
    public Vector3 Position { get; }
    public Vector3 Direction { get; }
    public float Pressure { get; }
    public SnowStampProfile Profile { get; }
    public int Priority { get; }
}
```

이전 위치와 현재 위치를 함께 전달하여 두 지점 사이를 Sweep합니다. `SnowStampProfile`은 Brush 크기, Falloff와 각 State Channel의 반응 강도를 정의하며, `Priority`는 Page가 부족할 때 플레이어 흔적을 NPC 흔적보다 우선하여 보존하는 데 사용합니다.

---

## GPU Surface State

눈의 변형 상태는 `Texture2DArray`로 관리합니다.

| Channel | State |
| :---: | --- |
| R | Depression |
| G | Compression |
| B | Displacement |
| A | Reserved |

- Surface State: `R8G8B8A8_UNorm`
- Pixel Age: `R16_SFloat`
- 기본 Page Resolution: `512 × 512`

Compute Shader는 Brush가 실제로 차지하는 픽셀 범위만 Dispatch합니다. Surface Shader는 State 값을 읽어 눌림과 밀려난 눈을 Vertex 변위, 색상과 Normal 표현에 반영합니다.

---

## Page-based State Management

설원의 전체 크기와 GPU 상태 용량을 분리하기 위해 논리 Page와 Texture Slice를 별도로 관리합니다.

```text
Default Page Size   : 8 m
Page Resolution     : 512 × 512
Default Capacity    : 32 Slices
Maximum Page Count  : 256 × 256 / Surface
```

`SnowSurface`는 Brush 범위를 기준으로 영향을 받는 Page만 계산합니다. Page에 처음 흔적이 기록될 때 `SnowStateStorage`가 Texture Slice를 할당하며, Brush가 Page 경계를 넘으면 동일한 Stroke를 각 Page 좌표에 맞춰 Dispatch합니다.

Capacity가 가득 찬 경우 단순한 LRU만 적용하지 않습니다.

1. 복원이 끝난 Page를 먼저 회수합니다.
2. 입력보다 Priority가 낮고 보호 시간이 끝난 Page를 선택합니다.
3. 필요한 경우에만 Priority가 낮은 보호 Page를 긴급 회수합니다.
4. 입력보다 Priority가 높은 Page는 회수하지 않고 해당 Stamp를 거절합니다.

이를 통해 NPC가 밀집된 상황에서도 플레이어 주변의 최근 흔적이 먼저 사라지는 문제를 줄였습니다.

---

## Contact Optimization

`SurfaceContactSystem`은 등록된 Surface Bounds를 Spatial Hash에 배치하고, Agent가 위치한 Cell의 후보 Surface만 검사합니다.

- 플레이어 Agent는 항상 활성 상태로 매 프레임 접촉을 갱신합니다.
- NPC는 플레이어 주변의 진입·이탈 반경으로 활성 여부를 결정합니다.
- Hysteresis를 사용하여 경계에서 활성 상태가 반복 전환되는 현상을 방지합니다.
- NPC가 충분히 이동했거나 Sample Interval이 지난 경우에만 다시 검사합니다.
- CharacterController가 공중에 있는 동안에는 흔적 생성을 중단합니다.

접촉 스케줄 상태는 Agent에 공개하지 않고 `SurfaceContactSystem` 내부에서 관리합니다. Agent는 Collider와 접촉 결과만 담당합니다.

---

## Surface Recovery

Brush가 픽셀을 갱신하면 해당 위치의 Age가 초기화됩니다. 이후 활성 Page만 일정 주기로 Compute Shader에 전달하고, Lifetime을 지난 State를 채널별 복원 속도에 따라 감소시킵니다.

```text
Footprint Lifetime  : 5 sec
Fade Duration       : 2 sec
Recovery Interval   : 0.25 sec
```

흔적이 완전히 복원되고 보호 시간이 끝난 Page는 자동으로 Slice를 반환합니다. 따라서 오래된 흔적이 Capacity를 계속 점유하지 않습니다.

---

## Optimization

- 실제 Brush가 닿은 Page에만 GPU Slice 할당
- `Texture2DArray`를 사용한 Page State 공유
- Priority, 보호 시간과 사용 시점을 반영한 Slice 재사용
- Spatial Hash를 통한 접촉 Surface 후보 축소
- 플레이어 중심 NPC relevance와 hysteresis
- 최소 이동 거리 이상의 Stroke만 기록
- Brush가 차지하는 픽셀 범위만 Compute Dispatch
- 활성 Page만 Recovery Dispatch
- 픽셀별 Age를 사용한 점진적 복원
- 프레임 중 컬렉션 재사용으로 관리 코드의 GC Allocation 억제

---

## Profiling

![200 Agent Profiling](Docs/Images/05_profiler_200_agents.png)

Unity Editor에서 200 Agent를 200 × 200 영역에 배치하고 Page 기반 흔적 기록과 접촉 최적화를 함께 측정했습니다.

| Metric | Result |
| --- | ---: |
| Scripts Mean | 약 `0.484 ms` |
| Frame Median | 약 `2.226 ms` |
| Frame Max | 약 `3.261 ms` |
| GC Collect | `0 ms` |

측정값에는 Editor와 VSync 대기 시간이 포함되어 있으므로 절대적인 빌드 성능이 아니라 구조 변경 전후의 상대 비교 기준으로 사용했습니다.

---

## Main Components

### [`SnowStateStorage`](Scripts/SnowStateStorage.cs)

GPU Texture Array, Slice 할당과 회수, 우선순위 기반 Page 보존, Brush Dispatch와 Recovery를 관리합니다.

### [`SurfaceContactSystem`](Scripts/SurfaceContactSystem.cs)

Spatial Hash와 플레이어 중심 relevance를 사용하여 다수 Agent의 Surface 접촉 검사를 관리합니다.

### [`SnowSurface`](Scripts/SnowSurface.cs)

Surface 좌표를 Page 좌표로 변환하고, Brush가 영향을 주는 Page와 Shader Page Map을 관리합니다.

### [`SnowState.compute`](Shaders/SnowState.compute)

연속 Brush Stroke를 Surface State에 기록하고 활성 Page의 픽셀별 복원을 처리합니다.

### [`SnowSurfaceState.shader`](Shaders/SnowSurfaceState.shader)

절차적 적설 높이와 GPU Surface State를 결합하여 눈의 변위, 색상, Normal과 Sparkle을 표현합니다.

### `SnowInteractor` · `SurfaceContactAgent`

Collider 접촉 결과를 연속적인 Brush 입력으로 변환하며, 플레이어 Focus와 흔적 Priority를 설정합니다.

### `SnowData` · `SnowStampProfile`

Brush, Page, 높이 설정 데이터와 접촉 대상별 반응 설정을 제공합니다.

---

## Project Structure

```text
ReactiveSnow/
├─ README.md
├─ Scripts/
│  ├─ SnowData.cs
│  ├─ SnowInteractor.cs
│  ├─ SnowStampProfile.cs
│  ├─ SnowStateStorage.cs
│  ├─ SnowSurface.cs
│  ├─ SurfaceContactAgent.cs
│  └─ SurfaceContactSystem.cs
├─ Shaders/
│  ├─ SnowState.compute
│  └─ SnowSurfaceState.shader
├─ Settings/
│  ├─ HumanoidStamp.asset
│  └─ SnowSurface.mat
├─ Textures/
│  ├─ SnowNoise.png
│  └─ SnowSparkle.png
└─ Docs/
   └─ Images/
```

---

## Implemented Features

- Collider-based Surface Contact
- Continuous Brush Stroke
- Pressure-based Surface Deformation
- Procedural Snow Height
- Page Boundary Handling
- Sparse GPU Page Allocation
- Priority-aware Page Reuse
- Pixel-based Surface Recovery
- Player-focused Multi-Agent Optimization
- Distance-limited Snow Sparkle
- CharacterController Jump Filtering

---

## Environment

- Unity 6
- Universal Render Pipeline
- Compute Shader
- HLSL
- C#

---

## Design Scope

본 프로젝트는 넓은 설원에서 다수의 접촉 흔적을 제한된 GPU 상태 공간으로 처리하는 방법을 연구한 구현입니다.

모든 접촉 유형을 미리 추상화하지 않고 현재 필요한 Collider 접촉과 눈 표면 처리에 집중했습니다. 다만 입력을 `SurfaceBrush`로 전달하고 Surface 접촉을 `IContactSurface`로 분리하여, 바퀴나 Drag Object처럼 실제 요구가 생겼을 때 기존 Page와 Compute 처리 구조를 변경하지 않고 확장할 수 있도록 구성했습니다.
