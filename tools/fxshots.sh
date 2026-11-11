#!/usr/bin/env bash
# 직업별 이펙트 확인용 캡처 묶음: tools/fxshots.sh <직업> <누른 뒤 물리 걸음> [press] [몬스터] [dx,dz]
# 몬스터를 주인공 북쪽에 세우고, 다 나타난 뒤(--delay) 북쪽을 보며 누른다.
cd "$(dirname "$0")/.."
job=$1; steps=$2; press=${3:-move_up,attack}; mon=${4:-moss_boar:1:12}; at=${5:-0,-1.9}
tools/godot.sh -- --capture --shot=640,560,10 --job=$job --level=15 --no=spawn \
  --spawn=$mon --spawnat=$at --delay=${DELAY:-90} --press=$press --steps=$steps \
  --out=user://shots_fx_${job} 2>&1 | grep -iE "error|exception|warn" | head -5
echo "$APPDATA/Godot/app_userdata/PixelMmo/shots_fx_${job}/"
