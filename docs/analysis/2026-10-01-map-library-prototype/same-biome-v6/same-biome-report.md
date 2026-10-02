# 같은 바이옴 actual static 결과

원본 8개 전체 중 엄격 native admission 6개, 증거 포함 6개. 검증 가능한 원본 6개, 격리 2개이며 격리 사례도 전체 분모에 남긴다.

다른 biome adaptive 보존 0/16, 기존 core 보존 0/6. 증거 번들 확인: False. 원본 8개 전체 exact 목표 달성: False.

실제 native PNG 188개를 HTML에 포함하고 디코딩했다. 브라우저 렌더는 미검증이다. PNG는 static 필드/edifice/roof 안전 검사를 대신하지 않는다. 등록·인덱스 확정은 별도이며 제품 추천 UI는 미연결이다. save/pawn/plant/quest의 생존 상태, 모든 타일 및 미관의 동일성은 범위 밖이다.

| 원본 | 실제 결과 런 | 필드 일치 | strict native/완료 | 격리 |
|---|---|---|---|---|
| gl-lake | exact-a-native-r1 | True | True |  |
| gl-valley | exact-a-native-r1 | True | True |  |
| gl-lone-mountain | exact-a-native-r1 | True | True |  |
| gl-cliff | exact-a-native-r1 | True | True |  |
| gl-archipelago | exact-a-native-r1 | True | True |  |
| gl-oasis | exact-a-native-r1 | True | True |  |
| gl-cave-entrance | exact-a-native-r1 | False | False | Unsafe original loaded roof; reject replica |
| gl-secluded-valley | exact-a-native-r1 | False | False | Unsafe original loaded roof; reject replica |

원본 99999 → PostInit PNG는 아래와 같이 실제 두 capture를 직접 비교한 진단이며 원본을 바꾸거나 같은 phase라고 가정하지 않았다.

| 원본 | true 차이 픽셀 | default 차이 픽셀 | native/projected 미지지 collapsing roof |
|---|---:|---:|---|
| gl-lake | 0 | 0 | 0/0 |
| gl-valley | 0 | 0 | 0/0 |
| gl-lone-mountain | 0 | 0 | 0/0 |
| gl-cliff | 0 | 0 | 0/0 |
| gl-archipelago | 0 | 0 | 0/0 |
| gl-oasis | 0 | 0 | 0/0 |
| gl-cave-entrance | 0 | 0 | 742/742 |
| gl-secluded-valley | 0 | 0 | 15/15 |

| 보존 대조 | 사례 | terrain+PNG 동일 | 전체 geology 동일 | 전체 raw/증거 PASS | 변경 필드 |
|---|---|---|---|---|---|
| cave-b-native-r1→adaptive-b-native-r1 | gl-lake | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-b-native-r1→adaptive-b-native-r1 | gl-valley | False | False | False | constructed_floor, edifice_indices, edifice_table, nonrock_edifice, source_terrain_sha256, surface_indices, terrain_defs, terrain_indices, terrain_table |
| cave-b-native-r1→adaptive-b-native-r1 | gl-lone-mountain | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-b-native-r1→adaptive-b-native-r1 | gl-cliff | False | False | False | cells, constructed_floor, edifice_indices, edifice_table, nonrock_edifice, regular_rock_cells, rock_cells, rock_defs, rock_mask, source_terrain_sha256, surface_indices, terrain_defs, terrain_indices, terrain_table, walkable |
| cave-b-native-r1→adaptive-b-native-r1 | gl-archipelago | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-b-native-r1→adaptive-b-native-r1 | gl-oasis | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-b-native-r1→adaptive-b-native-r1 | gl-cave-entrance | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-b-native-r1→adaptive-b-native-r1 | gl-secluded-valley | False | False | False | edifice_indices, edifice_table, nonrock_edifice, source_terrain_sha256, surface_indices, terrain_defs, terrain_indices, terrain_table |
| cave-c-native-r1→adaptive-c-native-r1 | gl-lake | True | False | False | edifice_indices, edifice_table, nonrock_edifice, walkable |
| cave-c-native-r1→adaptive-c-native-r1 | gl-valley | True | False | False | edifice_indices, edifice_table, nonrock_edifice, walkable |
| cave-c-native-r1→adaptive-c-native-r1 | gl-lone-mountain | False | False | False | cells, constructed_floor, edifice_indices, edifice_table, fertile_cells, native_roof_supported, nonrock_edifice, projected_roof_supported, regular_rock_cells, rock_cells, rock_defs, rock_mask, roof_indices, source_terrain_sha256, surface_indices, terrain_defs, terrain_indices, terrain_table, walkable |
| cave-c-native-r1→adaptive-c-native-r1 | gl-cliff | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-c-native-r1→adaptive-c-native-r1 | gl-archipelago | True | False | False | edifice_indices, edifice_table, nonrock_edifice, walkable |
| cave-c-native-r1→adaptive-c-native-r1 | gl-oasis | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-c-native-r1→adaptive-c-native-r1 | gl-cave-entrance | False | False | False | edifice_indices, edifice_table, nonrock_edifice, source_terrain_sha256, surface_indices, terrain_defs, terrain_indices, walkable |
| cave-c-native-r1→adaptive-c-native-r1 | gl-secluded-valley | False | False | False | constructed_floor, edifice_indices, edifice_table, nonrock_edifice, source_terrain_sha256, surface_indices, terrain_defs, terrain_indices, terrain_table |
| cave-a-native-r3→exact-a-native-r1 | core-foothills | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-a-native-r3→exact-a-native-r1 | core-dry-clearing | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-b-native-r1→adaptive-b-native-r1 | core-foothills | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-b-native-r1→adaptive-b-native-r1 | core-dry-clearing | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-c-native-r1→adaptive-c-native-r1 | core-foothills | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-c-native-r1→adaptive-c-native-r1 | core-dry-clearing | True | False | False | edifice_indices, edifice_table, nonrock_edifice, walkable |
| cave-b-native-r1→baseline-b-native-r1 | gl-lake | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-b-native-r1→baseline-b-native-r1 | gl-valley | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-b-native-r1→baseline-b-native-r1 | gl-lone-mountain | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-b-native-r1→baseline-b-native-r1 | gl-cliff | False | False | False | cells, constructed_floor, edifice_indices, edifice_table, nonrock_edifice, regular_rock_cells, rock_cells, rock_defs, rock_mask, source_terrain_sha256, surface_indices, terrain_defs, terrain_indices, terrain_table, walkable |
| cave-b-native-r1→baseline-b-native-r1 | gl-archipelago | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-b-native-r1→baseline-b-native-r1 | gl-oasis | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-b-native-r1→baseline-b-native-r1 | image-lake | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-b-native-r1→baseline-b-native-r1 | core-foothills | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-b-native-r1→baseline-b-native-r1 | core-dry-clearing | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-b-native-r1→baseline-b-native-r1 | gl-cave-entrance | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-b-native-r1→baseline-b-native-r1 | gl-secluded-valley | False | False | False | edifice_indices, edifice_table, nonrock_edifice, source_terrain_sha256, surface_indices, terrain_defs, terrain_indices, terrain_table |
| cave-c-native-r1→baseline-c-causal-native-r1 | gl-lake | True | False | False | edifice_indices, edifice_table, nonrock_edifice |
| cave-c-native-r1→baseline-c-causal-native-r1 | gl-valley | True | False | False | edifice_indices, edifice_table, nonrock_edifice, walkable |
| cave-c-native-r1→baseline-c-causal-native-r1 | gl-lone-mountain | False | False | False | cells, constructed_floor, edifice_indices, edifice_table, fertile_cells, native_roof_supported, nonrock_edifice, projected_roof_supported, regular_rock_cells, rock_cells, rock_defs, rock_mask, roof_indices, source_terrain_sha256, surface_indices, terrain_defs, terrain_indices, terrain_table, walkable |

전체 raw가 다르면 terrain/PNG가 같아도 strict 보존 PASS로 집계하지 않는다. 과거 런과 추가로 확보한 동일 환경 causal 대조는 각 pair로 표시되며 자동으로 실패 결과를 제외하지 않는다.
