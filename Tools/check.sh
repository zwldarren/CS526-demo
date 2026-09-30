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
# Runs on Windows (Git Bash) and macOS: Tools/locate-unity.sh resolves the editor and hands back the
# path form the native compiler takes on that machine.
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

# `command -v dotnet` is a symlink on macOS (usually into /usr/local/share/dotnet), so the SDK lives
# beside the resolved binary, not beside the link.
DOTNET_BIN="$(command -v dotnet)"
DOTNET_BIN="$(readlink -f "$DOTNET_BIN" 2>/dev/null || printf '%s' "$DOTNET_BIN")"
DOTNET_HOME="$(dirname "$DOTNET_BIN")"
# ---------------------------------------------------------------- locate the Unity editor
# Also leaves newest(), the version sort the SDK lookup below uses.
. "$PROJECT_ROOT/Tools/locate-unity.sh"

SDK_VERSION="$(ls "$DOTNET_HOME/sdk" | newest)"
CSC="$DOTNET_HOME/sdk/$SDK_VERSION/Roslyn/bincore/csc.dll"

if [ ! -f "$CSC" ]; then
    echo "error: no Roslyn compiler at $CSC" >&2
    exit 2
fi

NETSTANDARD="$UNITY_DATA/NetStandard/ref/2.1.0/netstandard.dll"
if [ ! -f "$NETSTANDARD" ]; then
    echo "error: no netstandard2.1 reference assemblies at $NETSTANDARD" >&2
    exit 2
fi

mkdir -p "$OUT_DIR"

# ---------------------------------------------------------------- FACET.Core
core_rsp="$(to_native "$OUT_DIR/core.rsp")"
{
    echo "-target:library"
    echo "-langversion:9.0"
    echo "-nologo"
    echo "-nostdlib+"
    echo "-out:\"$(to_native "$OUT_DIR/FACET.Core.dll")\""
    echo "-reference:\"$(to_native "$NETSTANDARD")\""
    for f in "$PROJECT_ROOT/Assets/Scripts/Core"/*.cs; do echo "\"$(to_native "$f")\""; done
} > "$OUT_DIR/core.rsp"

echo "== FACET.Core (netstandard2.1, no engine) =="
if ! dotnet "$(to_native "$CSC")" "@$core_rsp"; then
    echo "FAILED: FACET.Core" >&2
    exit 1
fi

# ---------------------------------------------------------------- FACET.Game
game_rsp="$(to_native "$OUT_DIR/game.rsp")"
{
    echo "-target:library"
    echo "-langversion:9.0"
    echo "-nologo"
    echo "-nostdlib+"
    echo "-out:\"$(to_native "$OUT_DIR/FACET.Game.dll")\""
    echo "-reference:\"$(to_native "$NETSTANDARD")\""
    for d in "$UNITY_DATA/Managed/UnityEngine"/*.dll; do
        echo "-reference:\"$(to_native "$d")\""
    done
    echo "-reference:\"$(to_native "$OUT_DIR/FACET.Core.dll")\""
    # Package assemblies, but not our own stale output and not Editor-only ones.
    for d in "$PROJECT_ROOT/Library/ScriptAssemblies"/*.dll; do
        [ -e "$d" ] || continue
        name="$(basename "$d")"
        case "$name" in
            FACET.*|*.Editor.dll) continue ;;
        esac
        echo "-reference:\"$(to_native "$d")\""
    done
    for f in "$PROJECT_ROOT/Assets/Scripts/Unity"/*.cs; do echo "\"$(to_native "$f")\""; done
} > "$OUT_DIR/game.rsp"

echo "== FACET.Game (UnityEngine + package assemblies) =="
if ! dotnet "$(to_native "$CSC")" "@$game_rsp"; then
    echo "FAILED: FACET.Game" >&2
    exit 1
fi

echo
echo "OK - FACET.Core and FACET.Game compiled clean."
