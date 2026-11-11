#!/usr/bin/env bash
# 이 PC 의 Godot(.NET) 콘솔 실행 파일로 프로젝트를 돌린다. 게임 인자는 반드시 -- 뒤에.
#   tools/godot.sh --headless --import
#   tools/godot.sh -- --capture --shot=112,150,9
# 다른 PC 에서는 GODOT 환경 변수로 실행 파일 자리를 준다.
G="${GODOT:-$HOME/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe}"
cd "$(dirname "$0")/.." && mkdir -p .claude && touch .claude/heartbeat && exec timeout "${GODOT_TIMEOUT:-300}" "$G" --path . "$@"
