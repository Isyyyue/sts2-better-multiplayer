from __future__ import annotations

import argparse
import colorsys
from collections import deque
from pathlib import Path

from PIL import Image, ImageFilter, ImageOps


OUTPUT_SIZE = (512, 384)
PANEL_SIZE = (512, 384)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Import the user-approved trade artwork.")
    parser.add_argument("--gold", type=Path, required=True)
    parser.add_argument("--campfire", type=Path, required=True)
    parser.add_argument("--out-dir", type=Path, required=True)
    parser.add_argument("--preview", type=Path, required=True)
    return parser.parse_args()


def fit_panel(image: Image.Image, size: tuple[int, int] = PANEL_SIZE) -> Image.Image:
    return ImageOps.fit(image, size, Image.Resampling.LANCZOS, centering=(0.5, 0.5))


def hue_candidate(pixel: tuple[int, int, int, int], palette: str) -> bool:
    r, g, b, _ = pixel
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    if v < 0.13 or s < 0.16:
        return False
    if palette == "green":
        return 0.18 <= h <= 0.48 and g >= r * 0.82 and g >= b * 0.72
    return h >= 0.90 or h <= 0.075


def connected_background_mask(image: Image.Image, palette: str) -> Image.Image:
    rgba = image.convert("RGBA")
    width, height = rgba.size
    pixels = rgba.load()
    seen = bytearray(width * height)
    selected = bytearray(width * height)

    seeds = [
        (width // 10, height // 10),
        (width // 2, height // 12),
        (width * 9 // 10, height // 10),
        (width // 16, height // 2),
        (width * 15 // 16, height // 2),
        (width // 10, height * 9 // 10),
        (width // 2, height * 11 // 12),
        (width * 9 // 10, height * 9 // 10),
    ]
    for x in range(0, width, 4):
        seeds.extend(((x, 2), (x, height - 3)))
    for y in range(0, height, 4):
        seeds.extend(((2, y), (width - 3, y)))

    queue: deque[tuple[int, int]] = deque()
    for x, y in seeds:
        index = y * width + x
        if not seen[index] and hue_candidate(pixels[x, y], palette):
            seen[index] = 1
            queue.append((x, y))

    while queue:
        x, y = queue.popleft()
        selected[y * width + x] = 255
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if nx < 0 or nx >= width or ny < 0 or ny >= height:
                continue
            index = ny * width + nx
            if seen[index]:
                continue
            seen[index] = 1
            if hue_candidate(pixels[nx, ny], palette):
                queue.append((nx, ny))

    mask = Image.frombytes("L", (width, height), bytes(selected))
    return mask.filter(ImageFilter.GaussianBlur(1.15))


def recolor_background(image: Image.Image, palette: str) -> Image.Image:
    rgba = image.convert("RGBA")
    transformed = Image.new("RGBA", rgba.size)
    source = rgba.load()
    target = transformed.load()

    for y in range(rgba.height):
        for x in range(rgba.width):
            r, g, b, a = source[x, y]
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if palette == "green":
                h = (h + 0.235) % 1.0
                s = min(1.0, s * 0.90)
                v = min(1.0, v * 0.83)
            else:
                h = (h - 0.205) % 1.0
                s = min(1.0, s * 0.88)
                v = min(1.0, v * 0.86)
            nr, ng, nb = colorsys.hsv_to_rgb(h, s, v)
            target[x, y] = (round(nr * 255), round(ng * 255), round(nb * 255), a)

    mask = connected_background_mask(rgba, palette)
    return Image.composite(transformed, rgba, mask)


def import_icons(gold_path: Path, campfire_path: Path, out_dir: Path) -> list[Path]:
    out_dir.mkdir(parents=True, exist_ok=True)
    gold_source = Image.open(gold_path).convert("RGBA")
    campfire_source = Image.open(campfire_path).convert("RGBA")

    gold = fit_panel(gold_source.crop((145, 72, 1292, 1015)), OUTPUT_SIZE)
    trade_panel = campfire_source.crop((78, 181, 739, 799))
    smith_panel = campfire_source.crop((777, 181, 1442, 799))

    trade = fit_panel(recolor_background(trade_panel, "green"))
    smith = fit_panel(recolor_background(smith_panel, "red"))

    outputs = [
        out_dir / "gold-trade.png",
        out_dir / "rest-trade.png",
        out_dir / "assist-smith.png",
    ]
    for image, path in zip((gold, trade, smith), outputs, strict=True):
        image.save(path, optimize=True)
    return outputs


def save_preview(paths: list[Path], preview_path: Path) -> None:
    preview_path.parent.mkdir(parents=True, exist_ok=True)
    images = [Image.open(path).convert("RGBA") for path in paths]
    margin = 24
    preview = Image.new(
        "RGBA",
        (OUTPUT_SIZE[0] * len(images) + margin * (len(images) + 1), OUTPUT_SIZE[1] + margin * 2),
        (10, 14, 18, 255),
    )
    for index, image in enumerate(images):
        preview.alpha_composite(image, (margin + index * (OUTPUT_SIZE[0] + margin), margin))
    preview.save(preview_path, optimize=True)


def main() -> None:
    args = parse_args()
    outputs = import_icons(args.gold, args.campfire, args.out_dir)
    save_preview(outputs, args.preview)


if __name__ == "__main__":
    main()
