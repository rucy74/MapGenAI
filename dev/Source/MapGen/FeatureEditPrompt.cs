namespace MapGenAI.MapGen
{
    public static class FeatureEditPrompt
    {
        public static string Rules(bool korean) => korean ? @"
기존 특징 편집:
- actual_tile_features는 현재 타일의 실제 특징입니다(원래 특징 포함). added_mutators만 보고 기존 특징을 없다고 판단하지 마세요. 이름을 추측하지 말고 defName을 사용합니다.
- 한 특징만 제거: params.remove_mutators:[""RiverDelta""]. 삼각주만 제거하면 일반 강은 유지합니다. 다른 강 변형도 같은 방식입니다. 현재 추가 후보 목록에서 숨겨진 기본 River/Coast/Mountain/Caves도 제거할 수 있습니다.
- 강 전체 제거: params.river:{""present"":false} 또는 params.remove_categories:[""River""]. 삼각주·합류·강 섬 등 River 카테고리 전체를 생성하지 않습니다. 사용자 도형으로 만든 호수는 그대로입니다.
- 다시 원래 강 계열을 복원: params.restore_categories:[""River""]. 이는 카테고리 억제를 해제하며 별도로 remove_mutators로 제거한 변형은 계속 제외합니다. 특정 변형을 다시 추가하려면 mutators에 이름을 보내세요.
- remove_categories/restore_categories는 아래 실제 카테고리 이름을 사용합니다. 다른 카테고리에도 동일하게 작동합니다. 특정 특징 하나만 삭제할 때는 remove_mutators를 사용하세요. 억제된 카테고리에 새 특징을 추가하려면 같은 응답에 restore_categories도 보냅니다.
- 강 제거는 선택한 정착지 맵 생성에 적용됩니다. 월드맵 강 연결선과 이웃 타일은 유지됩니다. river.present:true는 원래 강이 있는 타일의 생성 복원이며 강 없는 타일에 월드 강을 만드는 기능은 아닙니다. 사용자 물 도형은 별도의 shape_ops로 편집합니다.
- 현재 모드는 용암 fill과 구조물의 좌표/영역 배치를 지원하지 않습니다. 물이나 밀도 설정으로 바꾸고 완료했다고 설명하지 마세요. action:ask로 현재 모드의 미지원 기능임을 설명합니다. 게임 엔진상 불가능하다고 단정하지 마세요. 대안은 사용자가 선택한 뒤 적용합니다.
" : @"
Existing feature edits:
- actual_tile_features lists actual current tile features including originals. Do not infer absence from added_mutators. Use exact defName values, never invented names.
- Remove one feature with params.remove_mutators:[""RiverDelta""]. Removing a river variant retains an ordinary river. Base River/Coast/Mountain/Caves may be removed even if hidden in the addition catalog.
- Remove all river generation: params.river:{""present"":false} or params.remove_categories:[""River""]. This suppresses all River-category variants, preserving custom shape lakes.
- Restore original river-category generation with params.restore_categories:[""River""]. Named variants separately removed via remove_mutators remain excluded; re-add those through mutators when requested.
- remove_categories/restore_categories use exact category names listed below, and apply to other categories as well. Use remove_mutators for a single feature. To add a feature in a suppressed category, include restore_categories in that request.
- River removal affects the selected settlement's map generation; world river links and neighboring tiles remain. river.present:true restores generation on a natural river tile, not new world river links. Custom water shapes use shape_ops separately.
- This mod currently does not support lava fill or coordinate/region-based structure placement. Never substitute water or density and claim success. Use action:ask to explain this mod's current limitation, not an impossibility in the game engine. Apply alternatives only after the user chooses one.
";
    }
}
