## 2026-03-21 21:30 — MapGenParams.Apply() 유닛 테스트 인프라 + 버그 재현

- 요청: Apply()의 hills/elevation_shapes 버그를 게임 없이 재현
- 수행: Unity/Verse shim + 소스 링크 + 7개 시나리오 테스트 작성
- 결과: 4 PASS / 3 FAIL — 버그 3건 재현 (#2 hills 변경, #5 빈 배열, #6 hills=none 전환)
- 산출물: `Tests/UnityShim.cs`, `VerseShim.cs`, `MdpApplyTests.cs`, csproj 수정

---

## 2026-03-21 — SDF 유닛 테스트 인프라 구축

- 요청: 게임 없이 SDF 부호/형태/블렌딩을 수학적으로 검증
- 수행: Unity/Verse shim + 소스 링크 + 34개 테스트 + ASCII 시각화
- 결과: 3건 FAIL로 회전 버그 식별 → 수정 후 34/34 통과
- 산출물: `Tests/UnityShim.cs`, `VerseShim.cs`, `SdfTestData.cs`, `SdfTests.cs`

---
