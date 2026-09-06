Unity Lab

실제 게임에서 흥미롭게 느낀 그래픽 및 게임플레이 기술을 분석하고, Unity 환경에서 직접 구현한 기술 데모 모음입니다.

기능을 단순히 재현하는 데 그치지 않고 동작 구조와 성능 비용을 분석하여, 다른 콘텐츠에도 확장할 수 있는 형태로 설계하는 것을 목표로 합니다.

Projects

Project

Summary

WorldView Box

Cubemap Bake로 구현한 이공간 표현과 런타임 렌더링 최적화

Reactive Snow

GPU State로 기록하는 눈 표면 변형과 시간 기반 복원

WorldView Box



정적인 이공간을 Cubemap으로 Bake하여 실시간 6방향 렌더링 비용을 제거하고, 하나의 Cube Renderer에서 내부 월드와 유리 표면을 함께 표현했습니다.

Technical Details →
Demo Video →

Reactive Snow Surface System



캐릭터와 오브젝트의 접촉을 Brush 데이터로 변환하고, 이동 궤적과 압력에 따른 표면 변형을 GPU Surface State에 누적합니다. 기록된 흔적은 픽셀별 경과 시간에 따라 자연스럽게 복원됩니다.

Technical Details & Demo Images →

Environment

Unity 6

Universal Render Pipeline

C#

HLSL

Compute Shader
