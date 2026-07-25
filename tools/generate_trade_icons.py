from pathlib import Path
from random import Random

from PIL import Image, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Trading"
SIZE = (512, 384)


def rounded_card(fill: str, edge: str, accent: str, angle: float) -> Image.Image:
    card = Image.new("RGBA", (180, 246), (0, 0, 0, 0))
    draw = ImageDraw.Draw(card)
    draw.rounded_rectangle((8, 8, 172, 238), radius=18, fill=fill, outline="#171b22", width=9)
    draw.rounded_rectangle((20, 20, 160, 226), radius=12, outline=edge, width=5)
    draw.rounded_rectangle((31, 31, 149, 215), radius=9, outline=accent, width=3)

    # Original card-back motif: a vertical spire and four orbiting shards.
    draw.polygon([(90, 56), (111, 118), (99, 167), (90, 192), (81, 167), (69, 118)], fill=accent)
    draw.ellipse((77, 104, 103, 130), fill="#f2ead3", outline=edge, width=3)
    for x, y in [(50, 83), (130, 83), (50, 177), (130, 177)]:
        draw.polygon([(x, y - 11), (x + 9, y), (x, y + 11), (x - 9, y)], fill=edge)

    rng = Random(17)
    for _ in range(34):
        x = rng.randint(27, 153)
        y = rng.randint(27, 219)
        alpha = rng.randint(18, 42)
        draw.line((x, y, x + rng.randint(5, 17), y + rng.randint(-2, 2)), fill=(255, 255, 255, alpha), width=1)

    return card.rotate(angle, resample=Image.Resampling.BICUBIC, expand=True)


def curved_exchange_arrows(image: Image.Image, top: str, bottom: str) -> None:
    draw = ImageDraw.Draw(image)
    draw.arc((120, 24, 392, 188), start=205, end=335, fill="#11151bcc", width=22)
    draw.arc((120, 24, 392, 188), start=205, end=335, fill=top, width=13)
    draw.polygon([(386, 64), (424, 75), (394, 101)], fill="#11151b")
    draw.polygon([(390, 70), (414, 77), (395, 94)], fill=top)

    draw.arc((120, 196, 392, 360), start=25, end=155, fill="#11151bcc", width=22)
    draw.arc((120, 196, 392, 360), start=25, end=155, fill=bottom, width=13)
    draw.polygon([(126, 283), (88, 272), (118, 246)], fill="#11151b")
    draw.polygon([(122, 277), (98, 270), (117, 253)], fill=bottom)


def shadowed(layer: Image.Image, offset: tuple[int, int]) -> Image.Image:
    alpha = layer.getchannel("A")
    shadow = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    shadow.putalpha(alpha.filter(ImageFilter.GaussianBlur(8)))
    black = Image.new("RGBA", layer.size, (8, 10, 14, 170))
    black.putalpha(shadow.getchannel("A"))
    result = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    result.alpha_composite(black, offset)
    result.alpha_composite(layer)
    return result


def make_card_trade() -> None:
    image = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    backdrop = ImageDraw.Draw(image)
    backdrop.rounded_rectangle(
        (8, 8, 504, 376),
        radius=28,
        fill="#432532",
        outline="#925166",
        width=6,
    )
    backdrop.arc((54, -58, 458, 346), start=200, end=340, fill="#c57a67", width=4)
    left = rounded_card("#6f2d3f", "#d5a64a", "#f2d88a", -9)
    right = rounded_card("#176276", "#76c6c9", "#d6f0e8", 9)
    layer = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    layer.alpha_composite(left, (86, 70))
    layer.alpha_composite(right, (250, 70))
    image.alpha_composite(shadowed(layer, (0, 8)))
    curved_exchange_arrows(image, "#efb84f", "#67c7c2")
    image.save(OUT / "rest-trade.png")


def draw_pouch(draw: ImageDraw.ImageDraw, center_x: int, color: str, edge: str) -> None:
    draw.polygon(
        [(center_x - 44, 108), (center_x - 65, 154), (center_x - 58, 246),
         (center_x - 31, 278), (center_x + 31, 278), (center_x + 58, 246),
         (center_x + 65, 154), (center_x + 44, 108)],
        fill=color,
        outline="#171b22",
    )
    draw.line((center_x - 44, 108, center_x - 65, 154, center_x - 58, 246,
               center_x - 31, 278, center_x + 31, 278, center_x + 58, 246,
               center_x + 65, 154, center_x + 44, 108), fill="#171b22", width=9, joint="curve")
    draw.rounded_rectangle((center_x - 56, 102, center_x + 56, 137), radius=12, fill=edge, outline="#171b22", width=7)
    draw.polygon([(center_x - 33, 102), (center_x - 47, 68), (center_x + 47, 68), (center_x + 33, 102)], fill=color, outline="#171b22")
    draw.ellipse((center_x - 28, 164, center_x + 28, 220), fill="#e9b83f", outline="#f8df83", width=5)
    draw.ellipse((center_x - 15, 176, center_x + 15, 207), outline="#8b5c21", width=4)


def make_gold_trade() -> None:
    image = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    layer = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    draw_pouch(draw, 170, "#713449", "#cf8d52")
    draw_pouch(draw, 342, "#1b6672", "#63b7b5")
    for x, y in [(112, 284), (139, 305), (376, 302), (405, 280)]:
        draw.ellipse((x - 17, y - 9, x + 17, y + 9), fill="#e7ad32", outline="#fff0a0", width=3)
    image.alpha_composite(shadowed(layer, (0, 8)))
    curved_exchange_arrows(image, "#efb84f", "#67c7c2")
    image.save(OUT / "gold-trade.png")


def draw_player(draw: ImageDraw.ImageDraw, center_x: int, color: str, edge: str) -> None:
    draw.ellipse(
        (center_x - 33, 132, center_x + 33, 198),
        fill=color,
        outline="#171b22",
        width=8,
    )
    draw.rounded_rectangle(
        (center_x - 58, 194, center_x + 58, 278),
        radius=34,
        fill=color,
        outline="#171b22",
        width=9,
    )
    draw.arc(
        (center_x - 42, 210, center_x + 42, 286),
        start=195,
        end=345,
        fill=edge,
        width=5,
    )


def make_room_lobby() -> None:
    image = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    backdrop = ImageDraw.Draw(image)
    backdrop.rounded_rectangle(
        (8, 8, 504, 376),
        radius=28,
        fill="#2a303a",
        outline="#737d89",
        width=6,
    )
    backdrop.ellipse((112, -66, 400, 222), outline="#59636f", width=4)
    layer = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)

    # Public-network globe behind two distinct player tokens.
    draw.ellipse((171, 78, 341, 248), fill="#10242b", outline="#171b22", width=12)
    draw.ellipse((181, 88, 331, 238), outline="#67c7c2", width=7)
    draw.arc((213, 88, 299, 238), start=90, end=270, fill="#67c7c2", width=5)
    draw.arc((213, 88, 299, 238), start=270, end=90, fill="#67c7c2", width=5)
    draw.arc((181, 118, 331, 208), start=0, end=180, fill="#67c7c2", width=5)
    draw.arc((181, 118, 331, 208), start=180, end=360, fill="#67c7c2", width=5)

    draw_player(draw, 158, "#713449", "#cf8d52")
    draw_player(draw, 354, "#1b6672", "#63b7b5")

    # Small lock communicates the optional password without any text.
    draw.rounded_rectangle((222, 226, 290, 292), radius=12, fill="#e9b83f", outline="#171b22", width=8)
    draw.arc((235, 188, 277, 246), start=180, end=360, fill="#171b22", width=17)
    draw.arc((235, 188, 277, 246), start=180, end=360, fill="#f8df83", width=8)
    draw.ellipse((248, 248, 264, 264), fill="#8b5c21")
    draw.rounded_rectangle((252, 258, 260, 276), radius=3, fill="#8b5c21")

    image.alpha_composite(shadowed(layer, (0, 8)))
    curved_exchange_arrows(image, "#efb84f", "#67c7c2")
    image.save(OUT / "room-lobby.png")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    make_card_trade()
    make_gold_trade()
    make_room_lobby()
