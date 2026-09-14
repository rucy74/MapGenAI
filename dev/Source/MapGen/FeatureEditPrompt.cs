namespace MapGenAI.MapGen
{
    public static class FeatureEditPrompt
    {
        public static string Rules(bool korean) => korean ? @"
기존 특징 편집과 월드 지리 조건:
- actual_tile_features는 원래 특징을 포함한 현재 실제 특징입니다. added_mutators만 보고 기존 특징을 없다고 판단하지 마세요. 정확한 defName을 사용합니다.
- 월드의 바다·호수 해안 연결과 강 연결은 보존합니다. 내륙에 해안, 강 없는 타일에 월드 강을 추가하거나 기존 강·해안을 통째로 지우는 요청은 action:ask로 제한을 설명합니다. river.present:false, remove_categories:[""River""], [""Coast""]로 연결을 삭제하지 마세요.
- 삼각주만 제거: params.remove_mutators:[""RiverDelta""]. 일반 강과 해안은 남습니다. 다른 강·해안 변형도 개별 제거하며, 원래 연결을 대신할 기본 강·해안은 자동 유지합니다. 기존 강의 direction_angle/x_position/z_position과 기존 해안의 coast_direction 변경은 가능합니다.
- 삼각주는 강과 해안 하구가 모두 필요합니다. 합류점·발원지·호숫가는 각각 실제 강 연결 수·월드 호수 인접 조건을 따릅니다. Available 목록은 현재 설치된 Odyssey/확장 모드의 바이옴·언덕·온도·강·도로·해안 접면·생성기 조건을 반영합니다.
- 내부 호수·오아시스·분화구 등은 해안이 없어도 해당 특징의 조건이 맞으면 mutators로 추가할 수 있습니다. 월드 지형을 바꾸어 조건을 억지로 맞추지 마세요. Unavailable 목록의 특징 요청은 이유를 설명하고 action:ask를 사용합니다. 지원되지 않는 요청을 다른 특징으로 바꾸거나 성공했다고 말하지 마세요.
- 다른 내부 특징은 remove_mutators로 하나씩, remove_categories로 해당 종류 전체를 제거할 수 있습니다. restore_categories는 종류 억제만 해제합니다. 개별 remove_mutators로 제거했던 특징은 mutators로 다시 추가합니다. 억제 중인 종류에 추가하려면 같은 응답에 restore_categories도 보냅니다.
- mutators는 실제 타일의 지형 특징을 바꾸지만 월드 랜드마크의 이름·아이콘을 새로 배치하는 명령은 아닙니다. 사용자 물 도형은 shape_ops로 따로 수정합니다.
- 채움 재료와 구조물 위치는 아래 텍스트 영역 규칙을 따릅니다. 지형 특징 이름과 fill 재료 이름을 혼동하지 마세요.
- 후보는 현재 특징 유지 가능/교체 필요/불가로 구분됩니다. 대안을 제안하기 전에도 이 구분을 따르세요. 교체 필요 항목을 단순 추가 대안으로 제안하지 마세요. 동일 categories 또는 어느 쪽 overrideCategories에 걸리는 특징은 함께 추가하지 않습니다. 기존 특징 교체는 사용자가 대상을 명확히 선택한 경우만 remove_mutators로 수행합니다.
- 두 대안을 한 번에 제시하고 '그래'를 받았다고 둘 다 적용하지 마세요. 하나의 구체적인 호환 대안을 제안하거나 어느 쪽인지 질문하세요. 기존 온천 등 내부 특징을 유지하며 새 물/모래를 원하면 특징 추가와 직접 도형 편집을 구분하세요.
- 온천(HotSprings)은 자체 온천수/암반을 만드는 내부 특징이므로 평지와 자연발생 목록 밖 바이옴에서도 직접 추가할 수 있습니다. 산악이어야 한다고 추측하지 말고 Available/Unavailable 결과를 따르세요. 기존 강·해안과의 충돌 제한은 유지됩니다.
- 현재 활성 목록에 없는 이름은 '이 게임에 로드되지 않음'입니다. 모드가 꺼져 있을 수 있으므로 그 기능 자체가 존재하지 않는다고 단정하지 마세요. 확인/설명 답변도 반드시 action:ask JSON입니다. 사용자가 '없어?', '찾아봐'라고 물어도 자유 형식 문장으로 응답하지 마세요.
" : @"
Feature editing and world geography:
- actual_tile_features includes original and current features. Use exact defName values, not guessed names or just added_mutators.
- Preserve world river and ocean/lake shore connections. No inland coast, new world river on a riverless tile, or complete removal of an existing river/shore. Explain with action:ask. Do not delete connections with river.present:false or remove_categories:[""River""]/[""Coast""].
- Remove a variant with remove_mutators:[""RiverDelta""]. Ordinary river and shore remain automatically. Other river/coast variants work similarly. Existing river direction_angle/x_position/z_position and coast_direction may be changed.
- Delta requires both a world river and coastal outlet. Confluence, headwater and lakeshore require appropriate river links or an adjacent world lake. The Available catalog uses resolved Odyssey/mod biome, hilliness, temperature, river, road, coastal-side and worker conditions.
- Internal lakes, oases and craters can be added via mutators on inland tiles when their own requirements match. Do not alter world geography to force eligibility. For an Unavailable feature explain its reason with action:ask, never silently substitute or claim success.
- Remove an internal feature with remove_mutators, or its whole category with remove_categories. restore_categories only clears category suppression; named removed features require re-adding through mutators. To add in a suppressed category also include restore_categories.
- Feature edits update actual tile mutators, not the world's named landmark identity/icon. Custom water shapes are edited separately with shape_ops.
- Materials and structure placement follow the text-region rules below. Feature names and fill material names are different catalogs.
- Candidate groups distinguish additive, replacement-required and unavailable against the current plan. Respect these groups before proposing alternatives. Shared categories or overrides conflict. Never present a replacement-required feature as a harmless addition; remove_mutators requires the user to explicitly choose what to replace.
- A vague yes after several alternatives neither selects one nor authorizes all. Offer one concrete compatible alternative or ask which option. Distinguish direct water/sand shapes from feature additions when retaining an existing spring or other internal feature.
- Native HotSprings creates its own pools/rock bed: explicit additions may use flat tiles and biomes outside its natural-spawn whitelist. Follow Available/Unavailable rather than inventing a mountain requirement. River/shore conflict restrictions remain.
- Missing from the active catalogs means not loaded in this game, not that the feature cannot exist. A supplying mod may be disabled. Every explanation or follow-up such as 'does it exist?' or 'look again' must still be action:ask JSON, never plain prose.
";
    }
}
