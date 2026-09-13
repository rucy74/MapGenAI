# Fable: 이미지 경로 재개 설계 반대 검토

사용자가 과거 포기한 이미지/그림→맵과 복잡한 자연어 둘 다 다시 개선하도록 승인했다. 이번에는 아래 설계만 검토하고 파일 탐색/수정/위임 없이 한국어 600단어 이내, 가장 중요한 문제 최대3개와 검증법을 답해 달라.

사용자 기존 결정: 재현 우선, 불가능하면 합리적 해석을 명시. 영역 클릭·채팅·팔레트 수정. 위에서 본 지도는 직접 해석, 사선 사진은 위에서 본 후보2개로 고른다. 붓 편집·전용 imagegen 공급자 선정은 보류돼 있다. 이전의 grayscale-only는 모델 제안이었다. 과거 LLM 문자그리드는 반복행/부족한칸반복채움으로 틀린 결과도 통과했으므로 재사용하지 않는다.

현재 기반: strict parser/대상ID shape_ops/타일별상태와정확UI복원/실제설정diff를 구현. 43 offline assertions와 실제게임 Scribe·UI함수경로·호수맵생성 10개검사통과(실API이미지호출은 아직없음). 형식통과를의미품질로주장하지않음.

이미지 단계안:
1. ImageMapData(width,height,labels-string; 최대256²)를TileMapState optional로저장. Clone/preset/Scribe/Undo통합. Label은산,물,얕은물,흙,비옥토,모래,습지,진흙,얼음,미지정(기존게임유지). 격자→고도/비옥도변환은과거GridBuilder의nearest neighbor/terrain매핑을재사용하되 산/물경계확장으로원형상을변형시키지않음. 바탕격자적용후기존SDFshape오버레이.
2. 색상범례가있는팔레트그림은로컬nearest-color분류. 알수없는색은미지정으로남기고비율표시. 일반그림/사진은현재설정공급자의vision입력으로보내 labelled polygons를받음: {view:top_down|oblique,candidates:[{title,notes,background,regions:[{id,label,vertices:[[x,z],...]}]}]}. region최대32/vertices64/좌표0..1/전체출력제한. 뒤region이앞을덮는규칙(호수속섬도표현). 모델이셀을나열하지않고C#이확정래스터라이즈. 잘림/이상좌표/빈영역/심한퇴화는거부하거나품질경고. 여러셀임의반복없음.
3. top_down은1안,oblique는2개의명시적해석안+불확실성/대체설명. **이번단계2안은선택가능한지형미리보기이며AI가생성한사진품질이미지라고부르지않음.** 별도bitmap imagegen provider선정은계속보류. 후보를게임맵에자동적용하지않고유저가미리보기를고른후적용.
4. 이미지대화창: 파일경로/PNG,JPEG,원본+분류미니맵,AI해석버튼,후보선택,영역클릭(4연결 flood-fill)→팔레트교정,선택영역을JSON컨텍스트로주는채팅교정. AI채팅은선택영역 label/필요시연결영역의외곽이아닌안전한label교체부터. 수정은후보로컬에한정,Apply한번에부모Undo한항목. 기존개별산/호수SDF는보존.
5. 실제공급자설정은programmatically읽고키는출력/문서/산출물에남기지않음. SyntheticpalettePNG 2개로좌우/상하/물속섬/산비율IoU평가,실제API호출은최소표본;실게임마스크좌표검증후새미리보기표시. 새모델이과거실패를해결했다고일반화금지.

핵심질문: 1) labelpolygon형식의가장큰맹점 2) user기존결정과충돌하는부분 3) 최저합격조건. 모델동의는완료근거아님.
