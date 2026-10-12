# M9 지역 몬스터·사냥터 (2026-10-11)

전직(Lv 10) 뒤 새 스킬을 쓸 상대가 없었다 (몬스터 2종). 지역마다 고유 몬스터와 레벨대를 두어 Lv 1→30 사냥 동선을 만든다.

## 몬스터

| id | 이름 | 지역 | Lv | 행동 | 그림 |
|---|---|---|---|---|---|
| sakura_slime | 벚꽃 슬라임 | 바람의 초원 | 2 | 뛰어들기 | 기존 48px |
| field_wolf | 들개 | 바람의 초원 | 4 | 무리, 빠른 물기 | 새 (네발, dog 틀) 56px |
| forest_goblin | 숲 고블린 | 벚꽃 골짜기 | 7 | 내려찍기(예고 원) | 기존 56px |
| goblin_archer | 고블린 궁수 | 벚꽃 골짜기 | 8 | 거리를 두고 화살 | 새 56px |
| scarecrow | 허수아비 | 꽃 들판 | 7 | 제자리 회전 베기(둘레 원) | 새 64px |
| kappa | 갓파 | 등대 곶 | 11 | 등껍질로 앞을 막음, 물대포 | 새 56px |
| goblin_chief | 고블린 대장 (우두머리) | 벚꽃 골짜기 위 | 12 | 3연타·도약 내려찍기·돌진 | 새 96px |
| fox_spirit | 여우불 | 단풍 협곡 | 14 | 여우불 구슬(천천히 휜다), 순간이동 | 새 64px |
| crow_tengu | 까마귀 텐구 | 단풍 협곡·고목의 숲 | 16 | 빠른 연속 베기, 도약 | 새 64px |
| moss_wolf | 이끼 늑대 | 고목의 숲 | 17 | field_wolf 색 바꿈 | 색 바꿈 |
| treant | 나무 정령 | 고목의 숲 | 19 | 느림, 큰 내려찍기 | 새 96px |
| mist_ghost | 안개 망령 | 안개 호수 | 20 | 떠다니며 터지는 구슬 | 새 64px |
| mist_kappa | 안개 갓파 | 안개 호수 | 21 | kappa 색 바꿈 | 색 바꿈 |
| frost_slime | 얼음 슬라임 | 서리 고원 | 23 | sakura_slime 색 바꿈 | 색 바꿈 |
| frost_wolf | 서리 늑대 | 서리 고원 | 24 | field_wolf 색 바꿈 | 색 바꿈 |
| yeti | 설인 | 서리 고원 | 27 | 내려찍기 + 눈덩이 던지기 | 새 96px |

## 그림 아끼기
- 동작은 5방향(남·남동·동·북동·북)만 뽑고 서·남서·북서는 좌우 뒤집기 (`assets.json` `"mirror": true`).
- 몬스터는 걷기·공격·쓰러짐만. 서 있기·움찔은 회전 원화로 대신한다 (+ 깜빡임).
- 같은 몸에 색만 바꾼 변종은 `data/monsters` 의 `"base"` + `"recolor"` (불러올 때 색상 범위를 옮긴다, `Render/Recolor`).

## PixelLab (v3, high top-down, selective outline, high detail)
| 캐릭터 | id |
|---|---|
| goblin_archer | 764fdd55-4cb9-42ae-91d5-2ccd9561e688 |
| scarecrow | 793cd0ca-1ada-43db-b8fd-51a149e1293d |
| kappa | 50da4b65-fff4-46b6-acdb-17f893d8304e |
| fox_spirit | f86dbf93-ee67-4d74-a21a-5bdcc5e6dd5e |
| crow_tengu | eb586f18-4556-426e-a507-93b19ac70aaa |
| goblin_chief | bdb8aae4-3188-4771-abdd-b1f8502b8354 |
| field_wolf (standard, 네발 dog) | 9c393f34-d704-454c-aa6a-10d6297f5a70 |
| treant | 0f6103e4-9b1e-4cd1-9d72-ab08826c270b |
| yeti | 42925f88-8f8a-48a7-91a9-375031050b59 |
| mist_ghost | 2592fb97-a2d1-46db-b103-6fcef8c750ec |

## 진행
모든 동작 5방향 완료 (2026-10-12). 걷기는 틀 walking-6-frames (들개는 running-6-frames, 망령은 v3 떠다니기).
- 버린 것: gc_slam (마법 소용돌이·몸이 푸르게) → gc_slam2
- 남긴 흠: 갓파 북쪽 걷기는 몸이 가늘어진다(틀 뼈대), 고블린 대장 걷기에서 몽둥이가 망치 모양, 설인 쓰러짐은 주저앉기만, 텐구 도약 찍기 순간 흰 섬광

| 캐릭터 | attack | attack2 | move | death |
|---|---|---|---|---|
| goblin_archer | gb_shoot b69f40ef | | gb_walk 7c1da2d6 | gb_death 761f65bd |
| scarecrow | sc_attack b0200203 | | sc_walk 3812bfed | sc_death 29ae17d7 |
| kappa | kp_attack 32d92db8 | kp_spit efebf248 | kp_walk 2c897bde | kp_death e1bef3a3 |
| fox_spirit | fx_cast 22add181 | | fx_walk f8203a3f | fx_death 8742463b |
| crow_tengu | tg_slash 909565ef | tg_leap 95c16292 | tg_walk e4c794d9 | tg_death 88540ca2 |
| goblin_chief | gc_swing 45f63c50 | gc_slam2 2ab44dbe | gc_walk be387fb0 | gc_death c3432e4a |
| field_wolf | wf_bite a633e7c6 | | wf_run 30cb4c5f | wf_death d61f7887 |
| treant | tr_slam 923dc51c | | tr_walk 72f6862c | tr_death 8249d094 |
| yeti | yt_slam 07551899 | yt_throw a65ca1f7 | yt_walk b73bd97e | yt_death fab54a94 |
| mist_ghost | mg_cast 2ab2f5ad | | mg_float 4d0223e9 | mg_death d6d68129 |
