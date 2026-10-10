# PixelLab 동작 큐 (8칸)
DIRS8 = south, south-east, east, north-east, north, north-west, west, south-west

## 프롬프트
- RUN(bow) "running forward at a brisk pace, arms swinging naturally, the longbow held low in one hand"  (v3 8f, keep_first=false — archer_f 만 true(skip_first))
- RUN(staff) "running forward at a brisk pace, robe flapping, holding the staff upright in one hand"
- RUN(dagger) "running forward low and fast, a dagger held in each hand"
- DASH "quick evasive dash: pushes off one foot and lunges low and fast in the facing direction, cloak trailing behind" (4f, keep_first=false)
- GUARD(bow) "snapping into a defensive guard: raises the longbow crosswise in front of the body to block, knees bent" (4f, keep_first=false)
- GUARD(staff) "snapping into a defensive guard: holds the staff crosswise in front of the body with both hands to block, knees bent"
- GUARD(dagger) "snapping into a defensive guard: crosses both daggers in front of the chest to block, crouched low"
- HURT "getting hit: flinches and recoils backward from a blow, then recovers balance" (4f, keep_first=false)
- IDLE template breathing-idle (8 dirs)
- AR1 (8f keep) "quickly raises the wooden longbow, nocks an arrow and pulls the bowstring back to her cheek, releases the arrow straight forward, the string snaps and the bow lowers; plain wooden bow, no magic effects, no glow"   (male: his)
- AR3 (10f keep) "power shot: plants her feet, draws the longbow to full strength and holds for a moment, releases a powerful shot, the recoil pushes her half a step back; plain wooden bow, no magic effects, no glow"
- MG1 (8f keep) "casting a spell: draws the staff back then thrusts it forward with the crystal end pointing ahead, robe swaying, then returns to a ready stance; no magic effects, no glow, no particles"
- MG3 (10f keep) "raises the staff high overhead with both hands, then swings it down and forward to unleash a powerful spell, robe billowing; no magic effects, no glow, no particles"
- RG1 (8f keep) "quick forward slash: steps in and swipes the right-hand dagger horizontally in front of him, then snaps back to a crouched ready stance; plain steel daggers, no magic effects, no glow"
- RG2 (8f keep) "quick backhand slash: sweeps the left-hand dagger from right to left in front of the body, then snaps back to a crouched ready stance; plain steel daggers, no magic effects, no glow"
- RG3 (8f keep) "lunging thrust: leaps forward and stabs with both daggers together, then recovers to a crouched stance; plain steel daggers, no magic effects, no glow"

## 진행 (그룹 id)
archer_f ✓ fetched
archer_m ✓ fetched
mage_m ✓ fetched
mage_f ✓ fetched
rogue_m 10c158bb: 전부 ✓ (idle b5bcecf7)
rogue_f fa94cde3 (남쪽 시험 → 나머지 7방향): attack1 c1d51b35 · attack2 325341c5 · attack3 82e27a09(마법 궤적 → 버림) attack3b becaf7f9 · run 2f912f19 · dash 42559296 · guard 1c9da63e · hurt 69799467 · idle a5adb00e ✓
npc idle 그룹: guide a88a962a · warrior c6d19149 · archer c1e7aaf6 · mage 47b1f9fe · rogue 06d89d79
npc_guide 39995271, npc_warrior 6c409a6e, npc_archer b39df277, npc_mage cfcaa900, npc_rogue fbede50e: idle -

## 다시 뽑은 방향 (assets.json "redo")
프롬프트 끝에 "plain grey steel daggers, no glow, no magic, no energy, no sparks, no light effects"
- rogue_f attack3 SE·NW·W → rg_attack3c cdfabf05 · attack2 SE → rg_attack2b e0e1bfbe
- rogue_m attack2 E(청록 궤적) → rg_attack2b. attack3 S 는 다시 뽑아도 고리가 더 생겨 원본 + "deglow", attack1 S·NW 도 deglow. NW 공격 끝에 얼굴이 보이게 도는 건 다시 뽑아도 같아서 둠
- 작게 남긴 것: rogue_f attack1 E 마지막 칸 불티, rogue_m attack1 S 마지막 칸 줄, rogue_m attack2 W 노란 반짝임
