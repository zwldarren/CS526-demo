#!/usr/bin/env bash
#
# Compiles the FACET assemblies outside the Unity Editor, so a change can be
# checked without switching windows. Two things it proves:
#
#   FACET.Core  - compiles against netstandard2.1 / C# 9 with NO engine reference,
#                 which is the contract the Core asmdef enforces.
#   FACET.Game  - compiles against the real UnityEngine assemblies and the real
#                 package assemblies (Unity.InputSystem, ...) from Library/ScriptAssemblies.
#
# It is a fast syntax + API-surface check, not a substitute for opening the Editor:
# it cannot catch scene wiring, serialization or rendering mistakes.
#
# Usage:  bash Tools/check.sh
#
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="$PROJECT_ROOT/Temp/CompileCheck"

# ---------------------------------------------------------------- locate toolchain
if ! command -v dotnet >/dev/null 2>&1; then
    echo "error: 'dotnet' is not on PATH (install the .NET SDK, it ships the Roslyn compiler)." >&2
    exit 2
fi

DOTNET_HOME="$(dirname "$(command -v dotnet)")"
SDK_VERSION="$(ls "$DOTNET_HOME/sdk" | sort -V | tail -1)"
CSC="$DOTNET_HOME/sdk/$SDK_VERSION/Roslyn/bincore/csc.dll"

if [ ! -f "$CSC" ]; then
    echo "error: no Roslyn compiler at $CSC" >&2
    exit 2
fi

# ---------------------------------------------------------------- locate the Unity editor
# The editor writes its own install path here; it is the authoritative answer and
# survives moving the project between machines.
UNITY_DATA=""
if [ -f "$PROJECT_ROOT/Library/EditorInstance.json" ]; then
    UNITY_DATA="$(grep -o '"app_contents_path"[^,]*' "$PROJECT_ROOT/Library/EditorInstance.json" \
        | sed 's/.*: *"//; s/"$//')"
fi
if [ -z "$UNITY_DATA" ] && [ -n "${UNITY_DATA_OVERRIDE:-}" ]; then
    UNITY_DATA="$UNITY_DATA_OVERRIDE"
fi
if [ ! -d "$UNITY_DATA/Managed/UnityEngine" ]; then
    echo "error: cannot find the Unity editor install." >&2
    echo "       Open the project in the Editor once (it writes Library/EditorInstance.json)," >&2
    echo "       or run with UNITY_DATA_OVERRIDE='D:/path/to/Unity/Editor/Data'." >&2
    exit 2
fi

NETSTANDARD="$UNITY_DATA/NetStandard/ref/2.1.0/netstandard.dll"
if [ ! -f "$NETSTANDARD" ]; then
    echo "error: no netstandard2.1 reference assemblies at $NETSTANDARD" >&2
    exit 2
fi

# csc is a native Windows process: every path handed to it must be in Windows form.
win() { cygpath -m "$1"; }

mkdir -p "$OUT_DIR"

# ---------------------------------------------------------------- FACET.Core
core_rsp="$(win "$OUT_DIR/core.rsp")"
{
    echo "-target:library"
    echo "-langversion:9.0"
    echo "-nologo"
    echo "-nostdlib+"
    echo "-out:\"$(win "$OUT_DIR/FACET.Core.dll")\""
    echo "-reference:\"$(win "$NETSTANDARD")\""
    for f in "$PROJECT_ROOT/Assets/Scripts/Core"/*.cs; do echo "\"$(win "$f")\""; done
} > "$OUT_DIR/core.rsp"

echo "== FACET.Core (netstandard2.1, no engine) =="
if ! dotnet "$(win "$CSC")" "@$core_rsp"; then
    echo "FAILED: FACET.Core" >&2
    exit 1
fi

# ---------------------------------------------------------------- FACET.Game
game_rsp="$(win "$OUT_DIR/game.rsp")"
{
    echo "-target:library"
    echo "-langversion:9.0"
    echo "-nologo"
    echo "-nostdlib+"
    echo "-out:\"$(win "$OUT_DIR/FACET.Game.dll")\""
    echo "-reference:\"$(win "$NETSTANDARD")\""
    for d in "$UNITY_DATA/Managed/UnityEngine"/*.dll; do
        echo "-reference:\"$(win "$d")\""
    done
    echo "-reference:\"$(win "$OUT_DIR/FACET.Core.dll")\""
    # Package assemblies, but not our own stale output and not Editor-only ones.
    for d in "$PROJECT_ROOT/Library/ScriptAssemblies"/*.dll; do
        [ -e "$d" ] || continue
        name="$(basename "$d")"
        case "$name" in
            FACET.*|*.Editor.dll) continue ;;
        esac
        echo "-reference:\"$(win "$d")\""
    done
    for f in "$PROJECT_ROOT/Assets/Scripts/Unity"/*.cs; do echo "\"$(win "$f")\""; done
} > "$OUT_DIR/game.rsp"

echo "== FACET.Game (UnityEngine + package assemblies) =="
if ! dotnet "$(win "$CSC")" "@$game_rsp"; then
    echo "FAILED: FACET.Game" >&2
    exit 1
fi

echo
echo "OK - FACET.Core and FACET.Game compiled clean."
