#!/usr/bin/env bash
# 이 노트북의 Godot(.NET) 콘솔 실행 파일로 프로젝트를 돌린다. 게임 인자는 반드시 -- 뒤에.
#   tools/godot.sh --headless --import
#   tools/godot.sh -- --capture --shot=112,150,9
G="/c/Users/gram/Downloads/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe"
cd "$(dirname "$0")/.." && exec timeout "${GODOT_TIMEOUT:-300}" "$G" --path . "$@"
