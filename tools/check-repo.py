#!/usr/bin/env python3
"""仓库卫生检查 / Repository hygiene checks.

本地和 CI 里都能跑：

    python tools/check-repo.py

检查项：

1. 版本号四处一致 —— csproj / manifest / 源码常量 / workshop 更新说明
2. 所有 JSON 文件可解析
3. 没有把游戏程序集或其他第三方 DLL 提交进仓库
4. CHANGELOG.md 有当前版本的条目
5. workshop.json 的 minBranch / maxBranch 方向正确（低 → 高）

退出码 0 表示全部通过；有任何一项失败则返回 1。
"""

from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# 分支从低到高：public（正式版）→ public-beta（先行版）。
# 写反会得到空区间，上传后玩家在两边都装不上。
BRANCH_ORDER = ["public", "public-beta"]

# 这些文件必须由构建者从官方来源自行安装，不能进仓库。
FORBIDDEN_SUFFIXES = (".dll", ".pck", ".exe")
FORBIDDEN_ALLOWLIST: set[str] = set()

failures: list[str] = []
notes: list[str] = []


def fail(message: str) -> None:
    failures.append(message)


def note(message: str) -> None:
    notes.append(message)


def read_text(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def load_json(relative: str):
    return json.loads(read_text(relative))


def check_versions() -> None:
    """四处版本来源必须一致。"""
    manifest = load_json("BetterMultiplayer.json")
    manifest_version = manifest.get("version", "")

    project = read_text("BetterMultiplayer.csproj")
    match = re.search(r"<Version>([^<]+)</Version>", project)
    project_version = match.group(1) if match else ""

    source = read_text("BetterMultiplayerMod.cs")
    match = re.search(r'public const string Version = "([^"]+)";', source)
    source_version = match.group(1) if match else ""

    workshop = load_json("workshop/workshop.json")
    change_note = workshop.get("changeNote", "")
    note_version = ""
    match = re.search(r"\[h1\]\s*([0-9]+\.[0-9]+\.[0-9]+)", change_note)
    if match:
        note_version = match.group(1)

    pairs = {
        "BetterMultiplayer.json": manifest_version,
        "BetterMultiplayer.csproj": project_version,
        "BetterMultiplayerMod.cs": source_version,
        "workshop/workshop.json changeNote": note_version,
    }

    missing = [name for name, value in pairs.items() if not value]
    if missing:
        fail(f"读不到版本号：{', '.join(missing)}")
        return

    distinct = set(pairs.values())
    if len(distinct) > 1:
        detail = "、".join(f"{name}={value}" for name, value in pairs.items())
        fail(f"版本号不一致：{detail}")
        return

    note(f"版本号一致：{manifest_version}")


def check_json() -> None:
    for relative in ("BetterMultiplayer.json", "workshop/workshop.json"):
        try:
            load_json(relative)
            note(f"JSON 有效：{relative}")
        except Exception as exc:  # noqa: BLE001 - 检查脚本，报出原因即可
            fail(f"{relative} 解析失败：{exc}")


def tracked_files() -> list[str]:
    result = subprocess.run(
        ["git", "ls-files"],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=True,
    )
    return [line for line in result.stdout.splitlines() if line]


def check_no_binaries() -> None:
    offenders = [
        path
        for path in tracked_files()
        if path.lower().endswith(FORBIDDEN_SUFFIXES) and path not in FORBIDDEN_ALLOWLIST
    ]
    if offenders:
        listing = "、".join(offenders)
        fail(f"仓库里出现了不该提交的二进制文件：{listing}")
        return

    note("没有提交 DLL / PCK / EXE")


def check_changelog() -> None:
    manifest_version = load_json("BetterMultiplayer.json").get("version", "")
    try:
        changelog = read_text("CHANGELOG.md")
    except FileNotFoundError:
        fail("缺少 CHANGELOG.md")
        return

    if f"[{manifest_version}]" not in changelog:
        fail(f"CHANGELOG.md 里没有 [{manifest_version}] 的条目")
        return

    note(f"CHANGELOG.md 有 [{manifest_version}] 条目")


def check_branch_range() -> None:
    workshop = load_json("workshop/workshop.json")
    minimum = workshop.get("minBranch", "")
    maximum = workshop.get("maxBranch", "")

    if not minimum or not maximum:
        fail("workshop.json 必须同时定义 minBranch 与 maxBranch")
        return

    try:
        low = BRANCH_ORDER.index(minimum)
        high = BRANCH_ORDER.index(maximum)
    except ValueError:
        fail(f"workshop.json 的分支名不认识：minBranch={minimum}, maxBranch={maximum}")
        return

    if low > high:
        fail(
            "workshop.json 的分支区间是空的："
            f"minBranch={minimum} 高于 maxBranch={maximum}（应写成 {maximum} through {minimum}）"
        )
        return

    note(f"分支区间有效：{minimum} → {maximum}")


def main() -> int:
    check_versions()
    check_json()
    check_no_binaries()
    check_changelog()
    check_branch_range()

    for line in notes:
        print(f"  ok   {line}")

    if failures:
        print()
        for line in failures:
            print(f"  FAIL {line}")
        print(f"\n{len(failures)} 项检查未通过。")
        return 1

    print(f"\n全部 {len(notes)} 项检查通过。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
