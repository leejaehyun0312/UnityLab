# Reactive Snow Surface System

Unity URP 환경에서 구현한 **GPU 기반 반응형 눈 표면 시스템**입니다.

캐릭터의 발과 같이 표면에 접촉하는 대상을 `SurfaceBrush` 데이터로 변환하고, 접촉 위치와 이동 궤적, 압력에 따른 변형을 GPU Surface State에 누적합니다. 기록된 흔적은 일정 시간 유지된 뒤 픽셀별 경과 시간에 따라 자연스럽게 복원됩니다.

단순한 발자국 이펙트를 출력하는 방식이 아니라, 다양한 접촉 대상을 수용할 수 있는 **범용 Reactive Surface 구조**를 목표로 설계했습니다.

---

## Demo

### Walking / Foot Contact

![Walking](Docs/Images/01_walking.png)

Humanoid 발 위치에서 눈 표면과의 접촉을 감지하고, 접촉 지점에 Brush를 생성합니다. 좌우 발의 실제 위치를 기준으로 흔적이 기록되므로 캐릭터의 보행 애니메이션과 자연스럽게 연결됩니다.

### Continuous Brush Stroke

![Continuous Brush Stroke](Docs/Images/02_drag.png)

접촉 중인 발이 움직이면 이전 위치와 현재 위치 사이를 하나의 Brush Stroke로 처리합니다. 제자리 회전이나 발 끌기에서도 개별 Stamp가 끊어지지 않고 연속적인 흔적으로 이어집니다.

### Surface Recovery

![Surface Recovery](Docs/Images/03_recovery.png)

기록된 흔적은 일정 시간 유지된 뒤 오래된 영역부터 점진적으로 감소합니다. 각각의 픽셀이 개별적인 경과 시간을 가지므로 서로 다른 시점에 생성된 자국이 동시에 사라지지 않습니다.

### Sloped / Elevated Surface

![Sloped Surface](Docs/Images/04_slope.png)

표면의 로컬 좌표계를 기준으로 Brush 위치와 State Page를 계산하여 평지뿐 아니라 경사면과 높이가 다른 지붕에서도 동일한 반응 구조가 동작합니다.

---

## Core Architecture

```text
Surface Contact
      ↓
 SurfaceBrush
      ↓
  SnowSurface
      ↓
Logical Tile / State Page
      ↓
SnowStateStorage
      ↓
 Compute Shader
      ↓
GPU Surface State
      ↓
 Surface Shader
```

핵심은 **접촉 검출과 표면 변형을 분리한 구조**입니다.

`SnowInteractor`는 Humanoid 발 접촉을 `SurfaceBrush`로 변환하는 입력 계층입니다. 실제 Surface 시스템은 Brush가 캐릭터, 바퀴, 무기 중 어떤 대상으로부터 생성되었는지 알 필요 없이 전달받은 데이터만 처리합니다.

이를 통해 접촉 대상을 추가하더라도 GPU State 관리와 렌더링 구조를 변경하지 않고 동일한 표면 반응을 재사용할 수 있습니다.

---

## Surface Brush

`SurfaceBrush`는 접촉 대상이 표면에 전달하는 최소 단위의 상호작용 데이터입니다.

```csharp
public readonly struct SurfaceBrush
{
    public Vector3 PreviousPosition { get; }
    public Vector3 Position { get; }
    public Vector3 Direction { get; }
    public float Pressure { get; }
    public SnowStampProfile Profile { get; }
}
```

현재 위치뿐 아니라 이전 위치를 함께 전달하여 Compute Shader에서 두 지점 사이의 구간 전체를 Sweep합니다. 프레임마다 독립적인 Stamp를 반복해서 찍는 방식보다 빠른 이동과 낮은 프레임 환경에서도 흔적이 끊어지는 현상을 줄일 수 있습니다.

`SnowStampProfile`은 흔적의 크기, 회전, 강도, 부드러움과 같은 Brush 특성을 정의합니다. 접촉 대상마다 서로 다른 Profile을 사용하여 같은 처리 흐름 안에서 다양한 형태의 변형을 표현할 수 있습니다.

---

## GPU Surface State

눈의 상태는 CPU Mesh가 아닌 `Texture2DArray` 기반의 GPU State로 관리합니다.

| Channel | State        |
| :-----: | ------------ |
|    R    | Depression   |
|    G    | Compression  |
|    B    | Displacement |
|    A    | Reserved     |

기본 포맷은 `R8G8B8A8_UNorm`이며 Compute Shader가 Brush 영역의 State를 갱신합니다. Surface Shader는 저장된 값을 읽어 눈의 깊이, 색상과 Normal 표현에 반영합니다.

고밀도 Mesh의 Vertex를 CPU에서 직접 수정하지 않기 때문에 작은 발자국과 연속적인 이동 흔적을 GPU 중심으로 처리할 수 있습니다.

---

## Page-based State Management

넓은 표면 전체에 하나의 고해상도 Render Texture를 할당하면 실제 접촉이 없는 영역까지 GPU 메모리를 점유하게 됩니다. 이를 방지하기 위해 표면의 논리적인 Tile과 GPU State Page를 분리했습니다.

```text
Logical Tile : 16 m
State Page   : 4 m
Resolution   : 512 × 512 / Page
```

하나의 Tile은 여러 State Page로 구성되며, **실제로 Brush가 닿은 Page에만 GPU Slice를 할당**합니다.

`SnowStateStorage`는 제한된 수의 Texture Slice를 Pool로 관리합니다. 사용 가능한 Slice가 없으면 가장 오랫동안 사용되지 않은 Page를 선택하여 재사용하는 LRU 정책을 적용했습니다.

이 구조를 통해 표면의 전체 크기와 GPU State 용량을 분리하고, 넓은 공간에서도 실제 상호작용이 발생한 영역을 중심으로 메모리를 사용할 수 있습니다.

---

## Page Boundary Handling

Brush Stroke가 하나의 Page 경계를 넘어갈 경우 영향을 받는 모든 Page를 계산하여 각각 Compute Dispatch를 수행합니다.

각 Page는 독립적인 Texture Slice를 사용하지만 Brush 위치는 동일한 Surface 로컬 좌표계를 기준으로 변환됩니다. 이를 통해 Page 경계에서도 흔적이 잘리거나 위치가 어긋나지 않고 하나의 연속된 Stroke로 표현됩니다.

---

## Surface Recovery

각 State Page는 Surface State와 별도로 픽셀별 Age Texture를 관리합니다.

Brush가 픽셀을 갱신하면 해당 위치의 Age가 초기화됩니다. 이후 일정한 주기로 경과 시간을 누적하고, 설정된 Lifetime을 지난 픽셀의 State를 Fade Duration 동안 점진적으로 감소시킵니다.

```text
Lifetime          : 5 sec
Fade Duration     : 2 sec
Recovery Interval : 0.25 sec
```

Page 전체를 하나의 시간값으로 복구하지 않기 때문에 같은 Page 안에서도 최근에 밟힌 영역과 오래된 영역이 서로 다른 시점에 복원됩니다.

Recovery 연산은 매 프레임 실행하지 않고 지정된 Interval에 맞춰 처리하여 불필요한 Compute Dispatch를 줄였습니다.

---

## Optimization

* 접촉이 발생한 State Page만 GPU Slice 할당
* `Texture2DArray` 기반의 공유 State Storage
* 제한된 Page Capacity와 LRU Slice 재사용
* Foot Contact 검출에 `RaycastNonAlloc` 사용
* 최소 이동 거리 이상일 때만 Brush Stroke 적용
* CPU Vertex 변형 없이 Compute Shader에서 State 갱신
* Recovery 연산을 일정한 Interval 단위로 실행
* 오래된 Surface State를 자동 복원하여 장기 누적 방지
* 상태를 가지지 않는 좌표 계산을 Utility로 분리

Snow Cover Baker는 눈 표면을 구성하는 Editor Authoring Tool이며 런타임 State 처리 구조와 분리되어 있습니다.

---

## Main Components

### `SnowSurface`

Reactive Surface의 좌표 공간과 논리 Tile, State Page를 관리합니다. 전달받은 Brush가 영향을 주는 Page를 계산하고 `SnowStateStorage`에 변형을 요청합니다.

### `SnowStateStorage`

GPU `Texture2DArray`, Page Slice Pool, LRU 재사용, Brush Compute Dispatch와 시간 복원을 관리합니다.

### `SnowState.compute`

Brush의 이전 위치와 현재 위치 사이를 Sweep하여 Surface State에 기록하고, Age Texture를 기반으로 시간에 따른 복원을 처리합니다.

### `SnowSurfaceState.shader`

GPU Surface State를 읽어 눈 표면의 깊이감과 색상, Normal 표현에 반영합니다.

### `SnowInteractor`

Humanoid 좌우 발의 접촉을 감지하고 접촉 위치, 이동 방향과 압력을 포함한 `SurfaceBrush`를 생성합니다.

### `SnowStampProfile`

Brush의 크기, 회전, 강도, 부드러움과 Offset을 정의합니다.

### `SnowSurfaceUtility`

World·Local 좌표 변환과 Tile·Page Index 계산처럼 상태를 가지지 않는 수학 로직을 담당합니다.

### `SnowSurfaceBaker`

대상 Mesh 위에 눈 표면을 생성하고 데모 환경을 구성하는 Editor Authoring Tool입니다.

---

## Project Structure

```text
ReactiveSurface/
├─ SnowData.cs
├─ SnowSurface.cs
├─ SnowSurfaceUtility.cs
├─ SnowStateStorage.cs
├─ SnowStampProfile.cs
├─ SnowInteractor.cs
├─ SnowSurfaceBaker.cs
├─ Shader/
│  ├─ SnowState.compute
│  └─ SnowSurfaceState.shader
├─ Docs/
│  └─ Images/
│     ├─ 01_walking.png
│     ├─ 02_drag.png
│     ├─ 03_recovery.png
│     └─ 04_slope.png
└─ README.md
```

---

## Implemented Features

* Humanoid Foot Contact
* Continuous Brush Stroke
* Pressure-based Surface Deformation
* Page Boundary Handling
* Sparse GPU Page Allocation
* Texture Slice Pooling
* LRU Page Reuse
* Pixel-based Surface Recovery
* Sloped and Elevated Surface Support
* Runtime Footprint Rendering
* Snow Surface Baking Tool

---

## Environment

* Unity 6
* Universal Render Pipeline
* Compute Shader
* HLSL
* C#

---

## Design Scope

본 프로젝트는 눈 위에서 발생하는 상호작용 표현을 참고하여 직접 설계한 GPU 기반 Reactive Surface 구현입니다.

캐릭터의 발자국을 시작점으로 구현했지만 접촉 입력을 `SurfaceBrush`로 추상화하여 무기 궤적, 바퀴, Rigidbody Drag와 같은 다른 Contact Source도 동일한 State 처리 구조에 연결할 수 있도록 구성했습니다.
