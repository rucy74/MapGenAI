# CHANGELOG — MapGenAI 버전 히스토리

버전별 변경사항. `git checkout v1.X`로 해당 버전으로 돌아갈 수 있음.

---

## v1.5 (2026-03-22) — 프롬프트 최적화 + 통로 + UI

**롤백**: `git checkout v1.5 && dotnet build`

- 프롬프트 50% 축소 (예외처리 → 코드로 이동)
- ValidateRiver: 강 없는 타일에서 river 파라미터 자동 차단
- 통로: bump(negative)로 산벽 자연스럽게 깎기
- composite 통로 덮어쓰기 (e < 0.1 → elevation 강제 대입)
- radial 요새: size=small로 두꺼운 산벽
- 초승달: 원 중심 간격 확대
- 환영 예시 업데이트 (4개 언어)
- 온천 defName 안내 복원
- docs 정리: deprecated 폴더로 이동

---

## v1.2 (2026-03-22) — MDP + WorldComponent

**롤백**: `git checkout v1.2 && dotnet build`

### 핵심 변경
- **MDP 아키텍처**: WorldComponent에 타일별 상태 영구 저장. LLM이 안 보낸 파라미터는 기존 값 유지.
  - "강을 가로로" → "온천 추가" → 강 방향 유지됨
  - "왼쪽에 산" → "오른쪽에도 산" → 왼쪽 산 유지됨
- **explicitKeys**: LLM JSON에 실제 존재하는 키만 추적하여 부분 업데이트
- **프리셋 전체 직렬화**: elevation_shapes, 강 방향/위치, straight_river, 석재 종류 등 모든 필드 저장/로드
- **ApplySlope/ApplySplit 워크샵 원본 복원**: 대각선 산맥 다시 동작

### 새 파일
- `MapGenAIWorldComponent.cs` — 타일별 상태 영구 저장
- `TileMapState.cs` — 타일 상태 데이터 (IExposable)
- `BiomeDensityPatch.cs` — 식생/동물 밀도 > 1.0 지원
- `GeyserPatch.cs` — 간헐천 개수 제어

### 기타
- ridge shape 추가 (Smoothstep + Perlin, slope보다 자연스러운 산)
- ring fill=water 지원 (링 호수)
- LLM 대화 히스토리 분리 (generate 후 초기화, ask 후 유지)
- hill_amount 프롬프트 상한 1.3으로 조정
- 강 `river: none` 표시 제거 (LLM 혼동 방지)
- bump 크기 축소 (radiusScale 0.5→0.3)
- 광석 패치 Prefix/Postfix 재작성

### 알려진 이슈
- 하트/별/육각형 등 자유 형태 미구현 (CSG/SDF 설계만 있음)
- OpenRouter 경유 시 LLM 성능 저하 (system prompt 변환 문제)
- 링 호수에 노이즈 없음 (완벽한 도넛 형태)

---

## v1.1 (2026-03-21) — 워크샵 출시 버전

**롤백**: `git checkout v1.1 && dotnet build`

### 기능
- LLM 기반 맵 파라미터 생성 (채팅 UI)
- elevation_shapes: slope, split, radial, bump, noise, ring
- 강 방향/위치/일자 강 제어
- 해안 방향 제어
- 동굴/온천/간헐천 추가
- 석재 종류/수량 제어
- 광석/폐허/위험 밀도 제어
- 돌덩어리 on/off
- 산 크기/부드러움 Transpiler
- Undo/Reset/프리셋 저장/로드
- Map Preview 연동
- 다국어 지원 (한/영/일/중)
- OpenRouter + Gemini + 로컬 LLM 지원

### 알려진 이슈
- 프리셋에 elevation_shapes/강 방향 저장 안 됨
- "왼쪽 산 → 오른쪽 산" 시 왼쪽 산 사라짐 (MDP 미구현)
- hill_amount=1.4 시 맵 90% 산
