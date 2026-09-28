#!/usr/bin/env python3
"""Render docs/assets/demo.txt, a terminal transcript, as the animated docs/assets/demo.svg.

Lines starting with "$ " are typed out as commands; every other line appears in turn. The
animation loops, and viewers who prefer reduced motion see the final frame without animation.
Only the standard library is used:

    python3 docs/assets/render-demo.py
"""

from __future__ import annotations

import re
from pathlib import Path
from xml.sax.saxutils import escape

HERE = Path(__file__).resolve().parent
SOURCE = HERE / "demo.txt"
TARGET = HERE / "demo.svg"

FONT_SIZE = 13
CHAR_WIDTH = 7.83  # advance width of a 13px monospace glyph
LINE_HEIGHT = 19
PADDING = 18
TITLE_BAR = 30

TYPE_SPEED = 0.045  # seconds per typed character
COMMAND_PAUSE = 0.6  # pause after a command is typed
LINE_DELAY = 0.12  # delay between output lines
HOLD = 5.0  # time the final frame stays on screen

PATH_LINE = re.compile(r"\(\d+,\d+\)-\(\d+,\d+\)$")


def style_for(line: str) -> str:
    if PATH_LINE.search(line):
        return "path"
    stripped = line.strip()
    if re.match(r"EFD\d{3}:", stripped):
        return "rule"
    if stripped.startswith(("Severity:", "Message:")):
        return "detail"
    if stripped.startswith("⋮"):
        return "elided"
    if stripped.startswith("Found "):
        return "summary"
    return "output"


def main() -> None:
    lines = SOURCE.read_text(encoding="utf-8").rstrip("\n").split("\n")
    width = int(PADDING * 2 + CHAR_WIDTH * max(len(line) for line in lines))
    height = TITLE_BAR + PADDING * 2 + LINE_HEIGHT * len(lines)

    # First pass: when each line (or each typed character) appears.
    events: list[tuple[int, int | None, float]] = []  # (line, char or None for whole line, time)
    clock = 0.5
    for index, line in enumerate(lines):
        if line.startswith("$ "):
            events.append((index, 0, clock))  # the prompt appears at once
            for char in range(2, len(line)):
                clock += TYPE_SPEED
                events.append((index, char, clock))
            clock += COMMAND_PAUSE
        else:
            clock += LINE_DELAY if line else LINE_DELAY / 2
            events.append((index, None, clock))
    total = clock + HOLD

    def keyframes(name: str, start: float) -> str:
        on = start / total * 100
        return (
            f"@keyframes {name}{{0%,{on:.3f}%{{opacity:0}}"
            f"{min(on + 0.01, 99.9):.3f}%,99%{{opacity:1}}100%{{opacity:0}}}}"
        )

    css: list[str] = []
    body: list[str] = []
    appear = {(line, char): time for line, char, time in events}
    for index, line in enumerate(lines):
        y = TITLE_BAR + PADDING + LINE_HEIGHT * (index + 1) - 5
        if line.startswith("$ "):
            spans = []
            css.append(keyframes(f"k{index}p", appear[(index, 0)]))
            spans.append(f'<tspan class="prompt a" style="animation-name:k{index}p">$ </tspan>')
            for char in range(2, len(line)):
                name = f"k{index}c{char}"
                css.append(keyframes(name, appear[(index, char)]))
                spans.append(f'<tspan class="a" style="animation-name:{name}">{escape(line[char])}</tspan>')
            body.append(f'<text x="{PADDING}" y="{y}" class="command">{"".join(spans)}</text>')
        elif line:
            name = f"k{index}"
            css.append(keyframes(name, appear[(index, None)]))
            body.append(
                f'<text x="{PADDING}" y="{y}" class="{style_for(line)} a" '
                f'style="animation-name:{name}" xml:space="preserve">{escape(line)}</text>'
            )

    svg = f"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-labelledby="title">
<title id="title">efdoctor analyze on Microsoft's eShop: two synchronous EF Core calls inside async methods, 15 findings in total</title>
<style>
text{{font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,"Liberation Mono",monospace;font-size:{FONT_SIZE}px;fill:#c9d1d9;white-space:pre}}
.a{{opacity:0;animation-duration:{total:.2f}s;animation-iteration-count:infinite;animation-timing-function:step-end}}
.prompt{{fill:#7ee787}}.command{{fill:#e6edf3}}.path{{fill:#79c0ff}}.rule{{fill:#ffa657;font-weight:bold}}
.detail{{fill:#c9d1d9}}.elided{{fill:#8b949e;font-style:italic}}.summary{{fill:#e6edf3;font-weight:bold}}.output{{fill:#8b949e}}
@media (prefers-reduced-motion:reduce){{.a{{animation:none;opacity:1}}}}
{chr(10).join(css)}
</style>
<rect width="{width}" height="{height}" rx="8" fill="#0d1117"/>
<rect width="{width}" height="{TITLE_BAR}" rx="8" fill="#161b22"/>
<rect y="{TITLE_BAR - 8}" width="{width}" height="8" fill="#161b22"/>
<circle cx="18" cy="15" r="6" fill="#ff5f57"/><circle cx="38" cy="15" r="6" fill="#febc2e"/><circle cx="58" cy="15" r="6" fill="#28c840"/>
<text x="{width / 2}" y="19" text-anchor="middle" style="fill:#8b949e;font-size:12px">efdoctor — eShop</text>
{chr(10).join(body)}
</svg>
"""
    TARGET.write_text(svg, encoding="utf-8")
    print(f"wrote {TARGET.relative_to(HERE.parent.parent)} ({width}x{height}, {total:.1f}s loop)")


if __name__ == "__main__":
    main()
