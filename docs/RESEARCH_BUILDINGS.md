# RimWorld 건물/가구 시스템 조사 보고서

> 조사일: 2026-03-15
> 참조: RimWorld Wiki, GitHub 디컴파일 소스, Steam Workshop, Ludeon 공식 번역 저장소

---

## 1. ThingDef 건물/가구 카탈로그

### 1.1 벽 (Structure > Wall)

| defName | Label | Size | Stuff 사용 | 특이사항 |
|---|---|---|---|---|
| `Wall` | 벽 | 1x1 | Yes (Metallic/Woody/Stony) | costStuffCount=5, 패시빌리티=Impassable |
| `WallConduit` | 전선벽 | 1x1 | Yes | 벽 + 전력 전도 기능 내장 |

**재질별 벽 HP 참고:**
- Wood: 195 HP, 100% 가연성
- Steel: 300 HP, 40% 가연성
- Granite Blocks: 510 HP, 0% 가연성 (가장 견고한 일반 석재)
- Plasteel: 840 HP, 0% 가연성 (최고 HP)
- Gold: 180 HP, Beauty 높음
- Jade: 150 HP, Beauty 높음
- Uranium: 750 HP

**자연벽 (mineable):**
- `MineableSteel`, `MineableSilver`, `MineableGold`, `MineablePlasteel`
- Granite 자연벽: 900 HP / Limestone: 700 HP / Slate: 500 HP / Marble: 450 HP / Sandstone: 400 HP

**특수 벽:**
- Smoothed wall (자연석 벽을 "Smooth Surface" 작업으로 가공) — Beauty +1~+2
- Fortified wall (Odyssey DLC) — 7,500 HP, 건설/해체 불가
- Gravship hull (Odyssey DLC) — 기밀(airtight), 불연

**추가 구조물:**
- `Column` — 1x1, 통과 가능, 시야 차단 없음, 지붕 지지 기능, Beauty 높음 (Gold Column: Beauty 40)
- `Fence` / `FenceGate` — 동물 차단, 사람 통과 가능

### 1.2 문 (Structure > Door)

| defName | Label | Size | 비용 | 특이사항 |
|---|---|---|---|---|
| `Door` | 문 | 1x1 | Stuff 25 | Door Speed 100% |
| `Autodoor` | 자동문 | 1x1 | Stuff 25 + Steel 40 + Component 2 | Door Speed 400% (전력 시), 50W 소비 |
| `OrnateDoor` | 장식문 | 1x1 | - | Ideology DLC |
| `SecurityDoor` | 보안문 | 1x1 | - | Anomaly DLC |

- 문은 Stuff 사용 가능 (Metallic/Woody/Stony)
- 석재 자동문은 다른 재질보다 느리지만 여전히 일반 문보다 빠름
- `AnimalFlap` — 동물만 통과 가능한 문

### 1.3 바닥/지형 (TerrainDef)

#### 기본 바닥 (연구 불필요)

| defName 패턴 | Label | 비용 | Beauty | Cleanliness | 가연성 |
|---|---|---|---|---|---|
| `WoodPlankFloor` | 나무 바닥 | Wood 3 | 0 | 0 | 22% |
| `Bridge` | 다리 | Wood 12 | 0 | 0 | 80% |
| `StrawMatting` | 짚 깔개 | Hay 2 | -1 | -0.1 | 150% |

#### 연구 필요 바닥

| defName 패턴 | Label | 비용 | Beauty | Cleanliness | 특이사항 |
|---|---|---|---|---|---|
| `TileSandstone` / `TileGranite` 등 | 석재 타일 | Stone blocks 4 | +1 | 0 | 석재 종류별 존재 |
| `FlagstoneGranite` 등 | 석판 | Stone blocks 4 | 0 | 0 | 석재 종류별 존재 |
| `Concrete` | 콘크리트 | Steel 1 | -1 | 0 | 가장 저렴 |
| `PavedTile` | 포장 타일 | Steel 2 | 0 | 0 | 불연 |
| `SteelTile` | 강철 타일 | Steel 7 | 0 | +0.2 | 청소 시간 60% |
| `SilverTile` | 은 타일 | Silver 70 | +4 | +0.2 | 불연 |
| `GoldTile` | 금 타일 | Gold 70 | +11 | +0.2 | 최고 Beauty |
| `SterileTile` | 멸균 타일 | Steel 3 + Silver 12 | -1 | +0.6 | 병원/연구실 필수 |
| `CarpetRed` / `CarpetDark` 등 | 카펫 | Cloth 7 | +2 | 0 | 63색, 32% 가연, 청소 200% |

#### DLC 바닥
- **Ideology**: `HexTile`, `HexCarpet`, `MindbendCarpet`, `MorbidCarpet`, `SpikecorePlates`, `TotemicBoards` 등
- **Royalty**: `FineCarpet`, `FineStoneTile` 등
- **Anomaly**: `BioferritePlate`, `Flesh`, `VoidMetal`
- **Odyssey**: `GravshipSubstructure`

### 1.4 침대 (Furniture > Bed)

| defName | Label | Size | 비용 | Stuff |
|---|---|---|---|---|
| `SleepingSpot` | 수면 자리 | 1x2 | 무비용 | No |
| `DoubleSleepingSpot` | 더블 수면 자리 | 2x2 | 무비용 | No |
| `Bedroll` | 침낭 | 1x2 | 40 | Fabric/Leathery |
| `DoubleBedroll` | 더블 침낭 | 2x2 | 85 | Fabric/Leathery |
| `SlabBed` | 석판 침대 | 1x2 | 30 | Metallic/Woody/Stony |
| `SlabDoubleBed` | 석판 더블 침대 | 2x2 | 85 | Metallic/Woody/Stony |
| `Bed` | 침대 | 1x2 | 45 | Metallic/Woody/Stony |
| `DoubleBed` | 더블 침대 | 2x2 | 85 | Metallic/Woody/Stony |
| `HospitalBed` | 병원 침대 | 1x2 | Metallic 40 + Steel 80 + Component 5 | 부분 Stuff |
| `RoyalBed` | 왕실 침대 | 2x2 | Stuff 100 + Gold 50 | Metallic/Woody/Stony |
| `AnimalSleepingSpot` | 동물 수면 자리 | 1x1 | 무비용 | No |
| `AnimalSleepingBox` | 동물 수면 박스 | 1x1 | 25 | Metallic/Woody/Stony |
| `AnimalBed` | 동물 침대 | 1x1 | 40 | Fabric/Leathery |
| `BabySleepingSpot` | 아기 수면 자리 | 1x1 | 무비용 | No |
| `Crib` | 유아용 침대 | 1x1 | 25 | Woody/Stony/Metallic |

### 1.5 테이블/의자 (Furniture > Table/Chair)

**테이블:**

| defName | Label | Size | 비용 | Stuff |
|---|---|---|---|---|
| `Table1x2c` | 테이블 (1x2) | 1x2 | 28 | Metallic/Woody/Stony |
| `Table2x2c` | 테이블 (2x2) | 2x2 | 50 | Metallic/Woody/Stony |
| `Table2x4c` | 테이블 (2x4) | 2x4 | 95 | Metallic/Woody/Stony |
| `Table3x3c` | 테이블 (3x3) | 3x3 | 100 | Metallic/Woody/Stony |
| `EndTable` | 협탁 | 1x1 | 30 | Metallic/Woody/Stony |

**의자:**

| defName | Label | Size | 비용 | Stuff |
|---|---|---|---|---|
| `Stool` | 스툴 | 1x1 | 25 | Metallic/Woody/Stony |
| `DiningChair` | 식탁 의자 | 1x1 | 45 | Metallic/Woody |
| `Armchair` | 안락의자 | 1x1 | 110 | Fabric/Leathery |
| `Couch` | 소파 | 2x1 | 200 | Fabric/Leathery |
| `MeditationThrone` | 명상 왕좌 | 1x1 | 125 | Metallic/Woody/Stony |
| `GrandMeditationThrone` | 대형 명상 왕좌 | 3x2 | 300 + Gold 75 | Metallic/Stony |

### 1.6 조명 (Furniture > Lamp)

| defName | Label | Size | 비용 | 전력 |
|---|---|---|---|---|
| `TorchLamp` | 횃불 | 1x1 | Wood 20 | 연료 |
| `TorchWallLamp` | 벽 횃불 | 1x1 | Wood 15 | 연료 |
| `StandingLamp` | 스탠드 램프 | 1x1 | Steel 20 | 전기 |
| `StandingLamp_Red/Blue/Green` | 색상 램프 | 1x1 | Steel 20 | 전기 |
| `WallLamp` | 벽 램프 | 1x1 | Steel 15 | 전기 |
| `SunLamp` | 태양 램프 | 1x1 | Steel 40 | 전기 (높은 소비) |
| `FloodLight` | 투광등 | 1x1 | Steel 50 | 전기 |
| `Brazier` | 화로 | 1x1 | 50 | Metallic/Stony, 연료 |
| `DarklightBrazier` | 어둠 화로 | 1x1 | 50 | Metallic/Stony |
| `Darktorch` | 어둠 횃불 | 1x1 | Wood 20 | - |
| `FungusDarktorch` | 균류 어둠 횃불 | 1x1 | Raw fungus 20 | - |

### 1.7 작업대 (Production)

#### 원시 시대

| defName | Label | Size | 비용 | 전력 |
|---|---|---|---|---|
| `CraftingSpot` | 수공 작업 자리 | 1x1 | 무비용 | 없음 |
| `ButcherSpot` | 도살 자리 | 1x1 | 무비용 | 없음 |
| `FueledStove` | 연료 화덕 | 3x1 | Steel 80 + Wood 30 | 연료 |
| `HandTailorBench` | 수동 재봉대 | 3x1 | Stuff 75 | 없음 |
| `FueledSmithy` | 연료 대장간 | 3x1 | Steel 100 + Wood 30 | 연료 |
| `ArtBench` | 예술 작업대 | 3x1 | Stuff 100 | 없음 |
| `Brewery` | 양조기 | 1x1 | Stuff 30 | 없음 |
| `FermentingBarrel` | 발효통 | 1x1 | Stuff 30 | 없음 |
| `SimpleResearchBench` | 기본 연구대 | 1x3 | Stuff 75 | 없음 |

#### 산업 시대

| defName | Label | Size | 전력 | 비용 |
|---|---|---|---|---|
| `ElectricStove` | 전기 화덕 | 3x1 | 350W | Steel 80 + Component 2 |
| `ElectricSmithy` | 전기 대장간 | 3x1 | 210W | Steel 100 + Component 3 |
| `ElectricSmelter` | 전기 용광로 | 3x1 | 700W | Steel 170 + Component 2 |
| `ElectricTailorBench` | 전기 재봉대 | 3x1 | 120W | Steel 100 + Component 2 |
| `TableMachining` | 가공대 | 3x1 | 350W | Steel 200 + Component 6 |
| `TableButcher` | 도살대 | 3x1 | - | Stuff 75 |
| `ResearchBench` (HiTech) | 하이테크 연구대 | 1x5 | 전기 | Steel 100 + Component 2 |
| `TableStonecutter` | 석재 절단대 | 3x1 | - | Stuff 75 |
| `TableSculpting` | 조각대 | 3x1 | - | Stuff 100 |
| `FabricationBench` | 제작 작업대 | 3x1 | 전기 | Steel 200 + Component 6 |
| `DrugLab` | 약물 연구소 | 3x1 | 전기 | Steel 50 + Component 2 |
| `ElectricCrematorium` | 전기 화장로 | 3x1 | 전기 | Steel 150 + Component 2 |
| `BiofuelRefinery` | 바이오 연료 정제소 | 3x1 | 전기 | Steel 100 + Component 3 |

#### 기타 생산

| defName | Label | Size | 특이사항 |
|---|---|---|---|
| `NutrientPasteDispenser` | 영양 페이스트 기계 | 3x4 | Hopper 필요 |
| `Hopper` | 호퍼 | 1x1 | 영양 기계에 연결 |
| `HydroponicsBasin` | 수경 재배기 | 4x1 | 전기, SunLamp 필요 |
| `DeepDrill` | 심층 드릴 | 2x2 | 전기 |

### 1.8 저장 (Furniture > Storage)

| defName | Label | Size | 비용 | Stuff |
|---|---|---|---|---|
| `Shelf` | 선반 | 2x1 | 20 | Metallic/Woody/Stony |
| `SmallShelf` | 소형 선반 | 1x1 | 10 | Metallic/Woody/Stony |
| `Bookcase` | 책장 | 2x1 | 20 | Metallic/Woody/Stony |
| `SmallBookcase` | 소형 책장 | 1x1 | 10 | Metallic/Woody/Stony |
| `Dresser` | 서랍장 | 2x1 | 50 | Metallic/Woody/Stony |
| `EquipmentRack` | 장비 거치대 | 2x1 | - | - |

### 1.9 온도 (Temperature)

| defName | Label | Size | 비용 | 전력 | 특이사항 |
|---|---|---|---|---|---|
| `Heater` | 히터 | 1x1 | Steel + Component | 18W (대기)~200W | 실내 온도 상승 |
| `Cooler` | 쿨러 | 1x1 (벽에 설치) | Steel + Component | 20W (대기)~200W | 벽 관통, 뒷면에 열 방출 |
| `Vent` | 환기구 | 1x1 (벽에 설치) | Steel | 없음 | 방 사이 온도/가스 이동 |
| `PassiveCooler` | 패시브 쿨러 | 1x1 | Wood | 없음 (연료) | 5일 지속, 최대 15°C까지 냉각 |
| `Campfire` | 캠프파이어 | 1x1 | Wood | 없음 (연료) | 최대 30°C까지 가열 |

- Cooler/Heater: 이론상 단일 셀에 ~1800K 가열/냉각 가능
- 50타일 방 기준 약 36K/칸 효과
- 기본 목표 온도: 20°C (68°F)

### 1.10 기쁨/레크리에이션 (Recreation)

| defName | Label | Size | 비용 | 유형 | 최대 사용자 |
|---|---|---|---|---|---|
| `HorseshoesPin` | 편자 핀 | 1x1 | - | Dexterity play | 12 |
| `HoopstoneRing` | 고리 던지기 | 1x1 | - | Dexterity play | 12 |
| `ChessTable` | 체스 테이블 | 2x2 | - | Cerebral play | 2 |
| `GameOfUrBoard` | 우르 게임 | 1x1 | - | Cerebral play | 2 |
| `BilliardsTable` | 당구대 | 3x2 | - | Dexterity play | 2 |
| `PokerTable` | 포커 테이블 | 2x2 | - | Cerebral play | 4 |
| `Telescope` | 망원경 | 1x1 | - | Telescope study | 1 |
| `TubeTelevision` | 브라운관 TV | 2x1 | - | Television | 15 |
| `FlatscreenTelevision` | 평면 TV | 3x1 | - | Television | 30 |
| `MegascreenTelevision` | 메가스크린 TV | 4x2 | - | Television | 42 |

**Royalty DLC 악기:**
- `Harp` (하프, 1x1), `Harpsichord` (하프시코드), `Piano` (피아노) — Music 유형

**참고:** BilliardsTable은 3x2이지만 주변 1칸 빈 공간 필요 (실제 5x4 영역)

### 1.11 방어 (Security)

| defName | Label | Size | HP | 비용 | 특이사항 |
|---|---|---|---|---|---|
| `Sandbags` | 모래주머니 | 1x1 | 300 | Stuff 5 | 55% 엄폐, Beauty -10, 불연 |
| `Barricade` | 바리케이드 | 1x1 | 300 | Stuff 5 | 55% 엄폐, Beauty -3, Stuff 가연성 |
| `TurretGun` (Mini-turret) | 미니 터렛 | 1x1 | - | - | 전력 필요, 자동 사격 |
| `Turret_Autocannon` | 자동포 터렛 | - | - | - | 중거리 |
| `Turret_UraniumSlug` | 우라늄 슬러그 터렛 | - | - | - | 장거리 |
| `Turret_Foam` | 소화 터렛 | - | - | - | 화재 진압 |
| `Turret_RocketswarmLauncher` | 로켓 발사기 | - | - | - | 범위 공격 |
| `Turret_MortarBomb` | 박격포 (폭발) | - | - | - | 유인, 탄약 필요, 배럴 교체 |
| `Turret_MortarIncendiary` | 박격포 (소이) | - | - | - | " |
| `Turret_MortarEMP` | 박격포 (EMP) | - | - | - | " |

**트랩:**

| defName | Label | 피해 | 특이사항 |
|---|---|---|---|
| `TrapSpike` | 스파이크 트랩 | 100 Sharp (재질 배율) | 일회용, 아군 0.4% 확률로 발동 |
| `TrapIED_HighExplosive` | IED 폭발 | 50 Bomb, 4셀 반경 | 일회용 |
| `TrapIED_Incendiary` | IED 소이 | 10 Flame, 4셀 반경 | 주변 점화 |
| `TrapIED_EMP` | IED EMP | 50 EMP, 11셀 반경 | 기계 기절 |
| `TrapIED_Firefoam` | IED 소화 | 무피해, 10셀 반경 | 화재 진압 |
| `TrapIED_Smoke` | IED 연막 | 무피해, 8.6셀 반경 | 시야 차단 |
| `TrapIED_AntigrainWarhead` | IED 반물질 | 550 Super Bomb, 15셀 반경 | 제작 불가 |

### 1.12 전력 (Power)

#### 발전기

| defName | Label | Size | 비용 | 출력 | 특이사항 |
|---|---|---|---|---|---|
| `WoodFiredGenerator` | 나무 발전기 | 2x2 | Steel 100 + Component 2 | 1,000W | 나무 22/일 소비 |
| `ChemfuelPoweredGenerator` | 화학연료 발전기 | 2x2 | Steel 100 + Component 3 | 1,000W | 화학연료 4.5/일 |
| `WindTurbine` | 풍력 발전기 | 7x2 (본체) | Steel 100 + Component 2 | ~2,300W | 가변, 7x18 배제 영역 |
| `SolarGenerator` | 태양열 발전기 | 4x4 | Steel 100 + Component 3 | ~1,700W | 낮에만, 지붕 불가 |
| `WatermillGenerator` | 수차 | 5x6 | Wood 280 + Steel 80 + Component 3 | 1,100W | 강변 필수 |
| `GeothermalGenerator` | 지열 발전기 | 6x6 | Steel 340 + Component 8 | 3,600W | 간헐천 위 필수 |

#### 저장/전도

| defName | Label | Size | 비용 | 특이사항 |
|---|---|---|---|---|
| `Battery` | 배터리 | 1x2 | - | 600Wd 저장, 50% 효율, 일 5Wd 손실 |
| `PowerConduit` | 전선 | 1x1 | Steel 1 | 바닥 아래 |
| `HiddenConduit` | 숨긴 전선 | 1x1 | Steel + Component | 눈에 안 보임 |
| `WaterproofConduit` | 방수 전선 | 1x1 | - | 물 위 가능 |
| `PowerSwitch` | 전력 스위치 | 1x1 | Steel | 회로 분리 |

### 1.13 위생/의료 (Misc/Medical)

| defName | Label | Size | 비용 | 특이사항 |
|---|---|---|---|---|
| `HospitalBed` | 병원 침대 | 1x2 | Metallic 40 + Steel 80 + Component 5 | 수술 성공률 보너스 |
| `VitalsMonitor` | 생체 모니터 | 1x1 | Steel + Component | 침대 인접 배치, 의료 보너스 |
| `BiosculpterPod` | 바이오조각기 | 2x3 | Steel 120 + Component 4 | Ideology DLC, 치유/회춘 |
| `SleepAccelerator` | 수면 가속기 | 1x1 | Steel + Component | Ideology DLC |
| `CryptosleepCasket` | 크립토슬립 캡슐 | 1x2 | Steel + Uranium + Component | 동면 |

**VitalsMonitor 배치 팁:** 침대에 인접해야 효과 발동. 1개가 최대 2~3개 침대에 보너스 가능.

---

## 2. 건물 크기와 배치 규칙

### 2.1 크기 요약

| 크기 | 대표 건물 |
|---|---|
| **1x1** | Wall, Door, Heater, Cooler, Vent, StandingLamp, Stool, DiningChair, Shelf(Small), Brazier, Campfire, PowerConduit, Battery, VitalsMonitor, Hopper, PlantPot |
| **1x2** | Bed, SlabBed, HospitalBed, Bedroll, Battery, CryptosleepCasket, Table1x2c |
| **2x1** | Shelf, Bookcase, Dresser, Couch, Drape, TubeTelevision, EquipmentRack |
| **2x2** | DoubleBed, RoyalBed, ChessTable, PokerTable, Table2x2c, WoodFiredGenerator, ChemfuelPoweredGenerator, DeepDrill |
| **2x3** | BiosculpterPod |
| **2x4** | Table2x4c |
| **3x1** | ElectricStove, ElectricSmithy, ElectricSmelter, TableButcher, FlatscreenTelevision |
| **3x2** | BilliardsTable, GrandMeditationThrone |
| **3x3** | Table3x3c |
| **3x4** | NutrientPasteDispenser |
| **4x1** | HydroponicsBasin |
| **4x2** | MegascreenTelevision |
| **4x4** | SolarGenerator |
| **5x6** | WatermillGenerator |
| **6x6** | GeothermalGenerator |
| **7x2** | WindTurbine (본체; 배제 영역 7x18) |

### 2.2 회전 (Rotation)

- **회전 가능 (rotatable=true)**: 침대, 작업대, 쿨러, 히터, TV, 소파 등 대부분의 비정사각형 건물
- **회전 불가 (rotatable=false)**: Wall, Door (대칭이므로), SolarGenerator (정사각형), 많은 1x1 아이템
- 회전은 `Rot4` 열거형: North(0), East(1), South(2), West(3)

### 2.3 Stuff(재질) 사용 여부

**Stuff 사용 O (stuffable):**
- 벽, 문, 침대(Bed/DoubleBed), 테이블, 의자, 선반, 바리케이드, 많은 작업대(TableButcher, ArtBench 등)
- `<costStuffCount>N</costStuffCount>` + `<stuffCategories>` 태그로 정의

**Stuff 사용 X (고정 재질):**
- 전기 장비 (ElectricStove, ElectricSmithy 등) — `<costList>`로 고정 재질 명시
- 터렛, 발전기, 배터리, 전선 등 전력 관련
- 병원 침대, VitalsMonitor 등 의료 장비

---

## 3. Stuff(재질) 시스템

### 3.1 Stuff 카테고리

총 6개 카테고리:

| 카테고리 | 재질 수 | 대표 재질 |
|---|---|---|
| **Woody** | 1 | WoodLog |
| **Stony** | 7+ | BlocksGranite, BlocksLimestone, BlocksMarble, BlocksSandstone, BlocksSlate, Jade, BlocksVacstone |
| **Metallic** | 7+ | Steel, Silver, Gold, Plasteel, Uranium, Bioferrite, Obsidian |
| **Fabric** | 11+ | Cloth, DevilstrandCloth, Hyperweave, Synthread, 각종 양모 |
| **Leathery** | 27+ | 각종 가죽/모피 (Plainleather, Thrumbofur 등) |
| **Bioferrite** | 1 | Bioferrite (Anomaly DLC) |

### 3.2 건축용 주요 재질 상세 비교

| defName | 카테고리 | Market Value | Beauty 배율 | HP 배율 | 가연성 배율 | 작업량 배율 |
|---|---|---|---|---|---|---|
| `WoodLog` | Woody | 1.2 | x1 | x0.65 | x1 | x0.7 |
| `BlocksGranite` | Stony | 0.9 | x1 | x1.7 | x0 | x1.3 |
| `BlocksLimestone` | Stony | 0.9 | x1 | x1.55 | x0 | x1.3 |
| `BlocksMarble` | Stony | 0.9 | x1.35 | x1.2 | x0 | x1.15 |
| `BlocksSandstone` | Stony | 0.9 | x1.1 | x1.4 | x0 | x1.1 |
| `BlocksSlate` | Stony | 0.9 | x1.1 | x1.3 | x0 | x1.3 |
| `Steel` | Metallic | 1.9 | x1 | x1 | x0.4 | x1 |
| `Silver` | Metallic | 1.0 | x2 | x0.7 | x0.4 | x1 |
| `Gold` | Metallic | 10 | x4 | x0.6 | x0.4 | x0.9 |
| `Plasteel` | Metallic | 9 | x1 | x2.8 | x0 | x2.2 |
| `Uranium` | Metallic | 6 | x0.5 | x2.5 | x0 | x1.9 |
| `Jade` | Stony | 5 | x2.5 | x0.5 | x0 | x1.4 |

### 3.3 Stuff 선택 패턴 (건축별)

| 건물 유형 | 허용 Stuff 카테고리 | 예시 |
|---|---|---|
| 벽 (Wall) | Metallic, Woody, Stony | 어떤 재질이든 가능 |
| 침대 (Bed) | Metallic, Woody, Stony | Wood/Steel/Stone |
| 안락의자 (Armchair) | Fabric, Leathery | 천/가죽만 |
| 소파 (Couch) | Fabric, Leathery | 천/가죽만 |
| 바리케이드 (Barricade) | Metallic, Woody, Stony | 어떤 재질이든 |
| 모래주머니 (Sandbags) | 전용 Stuff | Sandbags stuff |

### 3.4 Stuff가 건물 스탯에 미치는 영향

Stuff 재질의 각 factor가 건물 기본 스탯에 **곱해짐**:
- `Beauty = baseStat.Beauty * stuff.beautyMultiplier`
- `MaxHitPoints = baseStat.MaxHP * stuff.hpFactor`
- `Flammability = baseStat.Flammability * stuff.flammabilityFactor`
- `WorkToBuild = baseStat.WorkToBuild * stuff.workToBuildFactor`

**모딩 시 건축 추천:**
- 최고 HP: Plasteel (x2.8) → 벽/문에 사용
- 최고 Beauty: Gold (x4) → 장식용 가구
- 불연 + 견고: Granite (x1.7 HP, x0 가연)
- 빠른 건설: Wood (x0.7 작업량) 또는 Sandstone (x1.1)

---

## 4. 방(Room) 시스템

### 4.1 방이 되려면 필요한 조건

1. **완전 밀폐**: 벽, 문, 환기구, 쿨러, 자연 암석 등 impassable 오브젝트로 완전히 둘러싸여야 함
2. **모서리 채움 불필요**: 대각선 모서리는 비어 있어도 방으로 인정
3. **지붕 75% 이상**: 75% 이상 지붕이 덮여 있으면 "실내"로 판정
4. **최대 크기**: 36 map region (약 5,184 타일)까지

### 4.2 방 역할(Room Role)

| 방 역할 | 필수 조건 | 효과 |
|---|---|---|
| **Bedroom** | 단인 침대 (1명에게 배정) | Impressiveness → 기분 버프 |
| **Barracks** | 침대 2개 이상 | 기숙사 페널티 (-4 기분) |
| **Dining Room** | 테이블 배치 | 식사 시 Impressiveness → 기분 |
| **Hospital** | 병원 침대 배치 | Cleanliness → 감염률 감소, 수술 성공률 |
| **Kitchen** | 화덕/스토브 배치 | Cleanliness → 식중독 확률 |
| **Laboratory** | 연구대 배치 | Cleanliness → 연구 속도 |
| **Workshop** | 작업대 배치 | 일반 작업 공간 |
| **Recreation Room** | 오락 시설 배치 | Impressiveness → 기분 |
| **Prison Cell** | 수감자용 침대 배치 | 감옥 |
| **Throne Room** | 왕좌 배치 (Royalty) | 제국 직위 요구사항 |
| **Temple** | 제단 배치 (Ideology) | 의식 수행 공간 |
| **Nursery** | 유아 침대 (Biotech) | 아이 양육 공간 |
| **Classroom** | 교탁/칠판 (Biotech) | 아이 교육 공간 |

### 4.3 방 통계 (Room Stats)

4개 핵심 통계가 Impressiveness를 결정:

#### Wealth (부)
- 방 안 모든 아이템, 벽, 문, 전선의 시장가치 합산
- 공식: wealth / 1500으로 정규화

#### Beauty (아름다움)
- 모든 타일의 환경 미관 평균
- 40타일 미만 방에는 페널티
- 공식: beauty / 3으로 정규화
- **주요 Beauty 소스:** 조각품, 금 타일(+11), 카펫(+2), 스무딩된 벽(+1~+2), 식물 화분
- **Beauty 감소:** 더러운 바닥(-5), 혈흔(-30), 구토물(-40)

#### Space (공간)
- `1.4 * 타일 수 - 0.9 * 공간 차지 오브젝트 수`
- 침대, 테이블, 작업대 등이 공간을 차지
- 공식: space / 125로 정규화

#### Cleanliness (청결)
- 모든 타일의 청결도 평균
- 범위: -20 (시체 담즙) ~ +1 (보이드 메탈 바닥)
- 공식: (cleanliness + 1) / 2.5로 정규화
- **주요 청결 소스:** SterileTile(+0.6), SteelTile(+0.2), SilverTile(+0.2), GoldTile(+0.2)

### 4.4 Impressiveness 계산 공식

1. 4개 스탯을 정규화
2. (-1, 1) 범위 밖의 값은 대수 곡선 적용 (감소 수익)
3. **가중 평균**: 최소값이 51.25%, 나머지 3개가 각 16.25%
4. **공간 상한**: Impressiveness는 Space 점수의 500%로 소프트 캡

**핵심 원칙:** 가장 약한 스탯이 전체의 51.25%를 차지하므로 **균형 잡힌 개선이 필수**

### 4.5 Impressiveness 등급

| 수치 | 등급 | 기분 보너스 |
|---|---|---|
| < 20 | Awful | 없음 |
| 20-30 | Dull | 없음 |
| 30-40 | Mediocre | 없음 |
| 40-50 | Decent | +2 |
| 50-65 | Slightly impressive | +3 |
| 65-85 | Somewhat impressive | +4 |
| 85-120 | Very impressive | +5 |
| 120-170 | Extremely impressive | +6 |
| 170-240 | Unbelievably impressive | +7 |
| 240+ | Wondrously impressive | +8 |

---

## 5. 건물 배치 제약 조건

### 5.1 야외 전용 (outdoor-only)

| 건물 | 배치 조건 |
|---|---|
| `SolarGenerator` | **지붕 없어야 함** — 지붕 아래 설치 시 출력 0 |
| `WindTurbine` | **7x18 배제 영역에 지붕/장애물 없어야 함** — 5칸 이상 장애물 시 출력 0 |
| `WatermillGenerator` | **강변 전용** — 강이 있는 맵 가장자리에만 설치 |
| `GeothermalGenerator` | **간헐천(SteamGeyser) 위에만** 설치 가능 |

### 5.2 벽 관통/설치형

| 건물 | 배치 조건 |
|---|---|
| `Cooler` | **벽에 관통 설치** — 앞면(실내)은 냉각, 뒷면(실외)은 가열 |
| `Vent` | **벽에 관통 설치** — 두 방 사이 온도 전달 |
| `Autodoor` | **벽 연결부에 설치** |

### 5.3 실내에서만 효과가 있는 것

| 건물 | 이유 |
|---|---|
| `Heater` / `Cooler` | 밀폐된 방이어야 온도 제어 효과적 |
| `VitalsMonitor` | 병원 방 내부에서만 의미 |
| `SunLamp` | 실외에서도 작동하지만, 주로 실내 수경재배에 사용 |

### 5.4 인접 배치 규칙

| 건물 | 인접 규칙 |
|---|---|
| `EndTable` | 침대 머리맡에 인접해야 기분 보너스 |
| `Dresser` | 침대 인접 시 기분 보너스 |
| `VitalsMonitor` | 병원 침대에 인접해야 의료 보너스 |
| `Hopper` | NutrientPasteDispenser에 인접 필수 |
| `ChessTable` / `PokerTable` | 인접 의자 필수 (의자 없으면 사용 불가) |
| `BilliardsTable` | 주변 1칸 빈 공간 필수 (5x4 영역) |

### 5.5 지형 요구사항 (terrainAffordanceNeeded)

| 수준 | 설명 | 예시 |
|---|---|---|
| `Light` | 가벼운 바닥 — 대부분의 자연 지형 | 가구, 소형 장비 |
| `Medium` | 중간 — 모래나 습지 제외 | 대부분의 건물 |
| `Heavy` | 무거운 — 단단한 바닥 필요 | 대형 건물, 터렛 |
| `GravelOrWorse` | 자갈 이하 지형에만 | DeepDrill 등 특수 |
| `Bridgeable` | 다리 건설 가능 지형 | 물 위 다리 |

### 5.6 지붕 붕괴 규칙

- 지붕은 지지물(벽, 기둥, 자연 암석)에서 **최대 6칸**까지 유지
- 12x12 방(벽 포함 14x14)이 기둥 없는 최대 크기
- 그보다 크면 내부에 기둥 필수

### 5.7 풍력 발전기 배제 영역 상세

```
     [  배제 영역 7x8  ]
     [  배제 영역 7x8  ]
     [    터빈 7x2     ]
     [  배제 영역 7x8  ]
     [  배제 영역 7x8  ]
```
- 총 배제 영역: 7x18 (터빈 포함)
- 배제 영역 내 허용: 태양광 패널, 선반, 재배지(나무 제외), 비축 구역
- 배제 영역 내 금지: 벽, 나무, 지붕, 대부분의 건물
- 나무/지붕/건물 5개 이상 차단 시 출력 0%

---

## 6. 인게임 건축 관련 RimWorld API

### 6.1 GenConstruct — 건축 핵심 클래스

**경로:** `RimWorld.GenConstruct` (Assembly-CSharp.dll)
**디컴파일 소스:** [josh-m/RW-Decompile](https://github.com/josh-m/RW-Decompile/blob/master/RimWorld/GenConstruct.cs), [Chillu1/RimWorldDecompiled](https://github.com/Chillu1/RimWorldDecompiled)

#### PlaceBlueprintForBuild — 청사진 배치

```csharp
public static Blueprint_Build PlaceBlueprintForBuild(
    BuildableDef sourceDef,
    IntVec3 center,
    Map map,
    Rot4 rotation,
    Faction faction,
    ThingDef stuff)
{
    Blueprint_Build blueprint = (Blueprint_Build)ThingMaker.MakeThing(
        sourceDef.blueprintDef, null);
    blueprint.SetFactionDirect(faction);
    blueprint.stuffToUse = stuff;
    GenSpawn.Spawn(blueprint, center, map, rotation, WipeMode.Vanish, false);
    return blueprint;
}
```

**사용 예시 (건물 배치):**
```csharp
// Wood Wall 청사진 배치
var wallDef = ThingDefOf.Wall;
var woodDef = ThingDefOf.WoodLog;
GenConstruct.PlaceBlueprintForBuild(
    wallDef,          // 건물 정의
    new IntVec3(10, 0, 10),  // 위치
    Find.CurrentMap,  // 맵
    Rot4.North,       // 회전
    Faction.OfPlayer, // 팩션
    woodDef           // 재질
);
```

#### CanPlaceBlueprintAt — 배치 가능 여부 검증

```csharp
public static AcceptanceReport CanPlaceBlueprintAt(
    BuildableDef entDef,
    IntVec3 center,
    Rot4 rot,
    Map map,
    bool godMode = false,
    Thing thingToIgnore = null)
```

**수행하는 검증 목록:**
1. 셀이 맵 범위 내인지 (`InBounds`)
2. 맵 가장자리 건설 금지 영역인지
3. 안개(미발견) 영역인지 → `"CannotPlaceInUndiscovered"`
4. 지형 요구사항 충족 여부 (`terrainAffordanceNeeded`)
5. 기존 건물/프레임과의 충돌 검사
6. 기존 블루프린트와의 중복 검사
7. Interaction cell 차단 여부 검사
8. 통과 불가 식물 위 건설 가능 여부
9. **PlaceWorker** 커스텀 검증 실행

#### BlocksConstruction — 건설 방해 여부

```csharp
public static bool BlocksConstruction(Thing constructible, Thing t)
```
- 기존 건물이 새 건설을 방해하는지 검사

### 6.2 Designator_Build — 건축 지정자 UI

**경로:** `RimWorld.Designator_Build`
**디컴파일 소스:** [RimWorld-zh/RimWorld-Decompile](https://github.com/RimWorld-zh/RimWorld-Decompile/blob/master/Assembly-CSharp/RimWorld/Designator_Build.cs)

**핵심 메서드:**
```csharp
// 셀 지정 가능 여부
public override AcceptanceReport CanDesignateCell(IntVec3 loc)
// 단일 셀에 건축 지정
public override void DesignateSingleCell(IntVec3 loc)
// 고스트(미리보기) 그리기
public override void DrawMouseAttachments()
```

**동작 흐름:**
1. 플레이어가 Architect 탭에서 건물 선택 → `Designator_Build` 인스턴스 활성화
2. 마우스 이동 시 `DrawMouseAttachments()` → `GhostDrawer.DrawGhostThing()` 호출로 투명 미리보기
3. 클릭 시 `DesignateSingleCell()` → `GenConstruct.PlaceBlueprintForBuild()` 호출
4. 블루프린트 배치 → 건설공이 자재 가져와서 건설

### 6.3 GhostDrawer — 건물 고스트 미리보기

**경로:** `Verse.GhostDrawer`

```csharp
public static void DrawGhostThing(
    IntVec3 center,
    Rot4 rot,
    ThingDef thingDef,
    Graphic baseGraphic,
    Color ghostCol,
    AltitudeLayer drawAltitude)
```

- 건물 배치 전 투명한 미리보기 이미지 렌더링
- 배치 가능 시 초록색, 불가 시 빨간색
- `Designator_Build.DrawMouseAttachments()`에서 호출

### 6.4 PlaceWorker — 배치 규칙 커스텀

**경로:** `Verse.PlaceWorker`

**XML에서 사용:**
```xml
<ThingDef>
    <placeWorkers>
        <li>PlaceWorker_Cooler</li>
    </placeWorkers>
</ThingDef>
```

**주요 PlaceWorker 타입:**

| PlaceWorker | 대상 건물 | 기능 |
|---|---|---|
| `PlaceWorker_Cooler` | Cooler | 벽 관통 설치 검증, 두 방 표시 |
| `PlaceWorker_Heater` | Heater | 방 온도 영향 범위 표시 |
| `PlaceWorker_NeedsFuelingPort` | - | 연료 포트 필요 건물 |
| `PlaceWorker_NotUnderRoof` | SolarGenerator 등 | 지붕 아래 배치 금지 |
| `PlaceWorker_OnlyOnThing` | GeothermalGenerator | 특정 Thing 위에만 |
| `PlaceWorker_WatchArea` | TV, 의자 등 | 시청 영역 표시 |

**커스텀 PlaceWorker 만들기:**
```csharp
public class PlaceWorker_MyCustom : PlaceWorker
{
    public override AcceptanceReport AllowsPlacing(
        BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map,
        Thing thingToIgnore = null, Thing thing = null)
    {
        // 커스텀 배치 검증 로직
        if (/* 조건 불만족 */)
            return new AcceptanceReport("배치할 수 없습니다.");
        return AcceptanceReport.WasAccepted;
    }
}
```

### 6.5 ThingDef 건물 XML 구조 (모딩용 참조)

#### BuildingBase 추상 부모 클래스

```xml
<ThingDef Name="BuildingBase" Abstract="True">
    <category>Building</category>
    <soundImpactDefault>BulletImpactMetal</soundImpactDefault>
    <selectable>true</selectable>
    <drawerType>MapMeshAndRealTime</drawerType>
    <terrainAffordanceNeeded>Light</terrainAffordanceNeeded>
    <repairEffect>Repair</repairEffect>
    <leaveResourcesWhenKilled>true</leaveResourcesWhenKilled>
    <filthLeaving>BuildingRubble</filthLeaving>
</ThingDef>
```

#### 건물 정의 예시 (모든 주요 태그 포함)

```xml
<ThingDef ParentName="BuildingBase">
    <defName>MyCustomBuilding</defName>
    <label>커스텀 건물</label>
    <description>건물 설명</description>
    <thingClass>Building</thingClass>
    <category>Building</category>

    <!-- 크기 및 배치 -->
    <size>(2,3)</size>
    <rotatable>true</rotatable>
    <terrainAffordanceNeeded>Medium</terrainAffordanceNeeded>
    <designationCategory>Furniture</designationCategory>
    <placingDraggableDimensions>0</placingDraggableDimensions>

    <!-- 외형 -->
    <graphicData>
        <texPath>Things/Building/MyBuilding</texPath>
        <graphicClass>Graphic_Multi</graphicClass>
        <drawSize>(2,3)</drawSize>
    </graphicData>
    <altitudeLayer>Building</altitudeLayer>
    <fillPercent>1.0</fillPercent>
    <castEdgeShadows>true</castEdgeShadows>
    <staticSunShadowHeight>0.5</staticSunShadowHeight>

    <!-- 재질 (Stuff) -->
    <costStuffCount>50</costStuffCount>
    <stuffCategories>
        <li>Metallic</li>
        <li>Woody</li>
        <li>Stony</li>
    </stuffCategories>

    <!-- 추가 고정 비용 -->
    <costList>
        <Steel>10</Steel>
        <ComponentIndustrial>2</ComponentIndustrial>
    </costList>

    <!-- 통과성 -->
    <passability>Impassable</passability>
    <blockLight>true</blockLight>
    <blockWind>true</blockWind>
    <coversFloor>true</coversFloor>
    <pathCost>0</pathCost>

    <!-- 스탯 -->
    <statBases>
        <MaxHitPoints>300</MaxHitPoints>
        <WorkToBuild>1200</WorkToBuild>
        <Flammability>0.5</Flammability>
        <Beauty>5</Beauty>
    </statBases>

    <!-- 배치 규칙 -->
    <placeWorkers>
        <li>PlaceWorker_Heater</li>
    </placeWorkers>

    <!-- 건물 속성 -->
    <building>
        <isEdifice>true</isEdifice>
    </building>
</ThingDef>
```

#### 주요 XML 태그 해설

| 태그 | 설명 | 예시 값 |
|---|---|---|
| `<size>` | 건물 크기 (x, z) | `(2,3)`, `(1,1)` |
| `<rotatable>` | 회전 가능 여부 | `true` / `false` |
| `<passability>` | 통과 가능성 | `Impassable`, `PassThroughOnly`, `Standable` |
| `<fillPercent>` | 엄폐 제공률 | `0.0` ~ `1.0` |
| `<costStuffCount>` | Stuff 재질 소비량 | 정수 |
| `<stuffCategories>` | 허용 Stuff 카테고리 리스트 | Metallic, Woody, Stony 등 |
| `<costList>` | 고정 재질 비용 | `<Steel>10</Steel>` 등 |
| `<terrainAffordanceNeeded>` | 필요 지형 수준 | Light, Medium, Heavy |
| `<designationCategory>` | Architect 탭 카테고리 | Structure, Furniture, Production, Power, Security, Temperature, Misc |
| `<altitudeLayer>` | 렌더링 높이 레이어 | Building, FloorEmplacement, Item |
| `<placeWorkers>` | 배치 규칙 클래스 | PlaceWorker_Cooler 등 |
| `<placingDraggableDimensions>` | 드래그 배치 차원 | 0 (단일), 1 (선형 드래그) |
| `<blockLight>` | 빛 차단 여부 | `true` / `false` |
| `<blockWind>` | 바람 차단 여부 | `true` / `false` |
| `<coversFloor>` | 바닥 덮음 여부 | `true` (벽 등) |

---

## 참고 자료 링크

### 공식 / 커뮤니티 Wiki
- [RimWorld Wiki - Furniture](https://rimworldwiki.com/wiki/Furniture)
- [RimWorld Wiki - Buildings](https://rimworldwiki.com/wiki/Buildings)
- [RimWorld Wiki - Structure](https://rimworldwiki.com/wiki/Structure)
- [RimWorld Wiki - Rooms](https://rimworldwiki.com/wiki/Rooms)
- [RimWorld Wiki - Stuff](https://rimworldwiki.com/wiki/Stuff)
- [RimWorld Wiki - Materials](https://rimworldwiki.com/wiki/Materials)
- [RimWorld Wiki - Floors](https://rimworldwiki.com/wiki/Floors)
- [RimWorld Wiki - Power](https://rimworldwiki.com/wiki/Power)
- [RimWorld Wiki - Security](https://rimworldwiki.com/wiki/Security)
- [RimWorld Wiki - Temperature](https://rimworldwiki.com/wiki/Temperature)
- [RimWorld Wiki - Recreation](https://rimworldwiki.com/wiki/Recreation)

### 모딩 튜토리얼
- [Modding Tutorials/Furniture](https://rimworldwiki.com/wiki/Modding_Tutorials/Furniture)
- [Modding Tutorials/ThingDef](https://rimworldwiki.com/wiki/Modding_Tutorials/ThingDef)
- [Modding Tutorials/Flooring](https://rimworldwiki.com/wiki/Modding_Tutorials/Flooring)
- [RimWorld Modding Resources - Abstracts](https://spdskatr.github.io/RWModdingResources/abstracts.html)

### 디컴파일 소스
- [Chillu1/RimWorldDecompiled](https://github.com/Chillu1/RimWorldDecompiled) — 최신 디컴파일 (1.5+)
- [josh-m/RW-Decompile - GenConstruct.cs](https://github.com/josh-m/RW-Decompile/blob/master/RimWorld/GenConstruct.cs)
- [RimWorld-zh/RimWorld-Decompile - Designator_Build.cs](https://github.com/RimWorld-zh/RimWorld-Decompile/blob/master/Assembly-CSharp/RimWorld/Designator_Build.cs)

### 건물 defName 참조
- [User:Alistaire/Type:defName/ThingDef/Building](https://rimworldwiki.com/wiki/User:Alistaire/Type:defName/ThingDef/Building)
- [Ludeon 공식 번역 저장소](https://github.com/Ludeon/RimWorld-Finnish/tree/master/DefInjected/ThingDef) — Buildings_*.xml 파일에서 모든 defName 확인 가능
