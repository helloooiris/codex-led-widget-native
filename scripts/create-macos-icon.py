from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont


ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "CodexLedWidget.Mac" / "Assets"
PNG = OUT / "AppIcon-1024.png"


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    candidates = [
        "/System/Library/Fonts/SFNS.ttf",
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf" if bold else "/System/Library/Fonts/Supplemental/Arial.ttf",
    ]
    for candidate in candidates:
        try:
            return ImageFont.truetype(candidate, size=size)
        except OSError:
            pass
    return ImageFont.load_default()


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    size = 1024
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))

    shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    shadow_draw = ImageDraw.Draw(shadow)
    shadow_draw.ellipse((54, 70, 970, 994), fill=(8, 24, 42, 62))
    shadow = shadow.filter(ImageFilter.GaussianBlur(24))
    image.alpha_composite(shadow)

    orb = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    orb_draw = ImageDraw.Draw(orb)
    orb_box = (48, 42, 976, 970)
    orb_mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(orb_mask).ellipse(orb_box, fill=255)

    shell = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    shell_draw = ImageDraw.Draw(shell)
    shell_draw.ellipse(orb_box, fill=(246, 252, 255, 186), outline=(255, 255, 255, 210), width=9)
    shell.putalpha(Image.composite(shell.getchannel("A"), Image.new("L", (size, size), 0), orb_mask))
    orb.alpha_composite(shell)

    clip_left = Image.new("L", (size, size), 0)
    clip_left_draw = ImageDraw.Draw(clip_left)
    clip_left_draw.pieslice(orb_box, start=90, end=270, fill=255)
    clip_right = Image.new("L", (size, size), 0)
    clip_right_draw = ImageDraw.Draw(clip_right)
    clip_right_draw.pieslice(orb_box, start=-90, end=90, fill=255)

    left_fill = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    left_draw = ImageDraw.Draw(left_fill)
    left_draw.rectangle((48, 360, 512, 970), fill=(12, 223, 166, 214))
    left_draw.rectangle((48, 360, 512, 610), fill=(120, 255, 221, 126))
    left_fill.putalpha(Image.composite(left_fill.getchannel("A"), Image.new("L", (size, size), 0), clip_left))

    right_fill = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    right_draw = ImageDraw.Draw(right_fill)
    right_draw.rectangle((512, 42, 976, 970), fill=(55, 136, 255, 218))
    right_draw.rectangle((512, 42, 976, 320), fill=(135, 218, 255, 118))
    right_fill.putalpha(Image.composite(right_fill.getchannel("A"), Image.new("L", (size, size), 0), clip_right))

    orb.alpha_composite(left_fill)
    orb.alpha_composite(right_fill)

    gloss = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    gloss_draw = ImageDraw.Draw(gloss)
    gloss_draw.ellipse((124, 72, 900, 452), fill=(255, 255, 255, 76))
    gloss_draw.ellipse((112, 104, 912, 954), outline=(255, 255, 255, 110), width=8)
    gloss_draw.arc((118, 86, 906, 880), start=208, end=316, fill=(255, 255, 255, 168), width=13)
    gloss.putalpha(Image.composite(gloss.getchannel("A"), Image.new("L", (size, size), 0), orb_mask))
    orb.alpha_composite(gloss)

    orb_draw.ellipse(orb_box, outline=(255, 255, 255, 220), width=7)
    orb_draw.ellipse((64, 58, 960, 954), outline=(30, 72, 110, 52), width=3)
    orb_draw.line((512, 72, 512, 940), fill=(255, 255, 255, 118), width=5)
    orb_draw.line((517, 80, 517, 932), fill=(18, 32, 51, 55), width=3)

    label_font = font(92, bold=True)
    percent_font = font(150, bold=True)
    for text, xy in [("5h", (282, 345)), ("1w", (742, 345))]:
        bbox = orb_draw.textbbox((0, 0), text, font=label_font)
        orb_draw.text((xy[0] - (bbox[2] - bbox[0]) / 2, xy[1]), text, font=label_font, fill=(10, 37, 63, 210))
    for text, xy in [("96", (282, 472)), ("92", (742, 472))]:
        bbox = orb_draw.textbbox((0, 0), text, font=percent_font)
        orb_draw.text((xy[0] - (bbox[2] - bbox[0]) / 2, xy[1]), text, font=percent_font, fill=(7, 23, 42, 228))

    image.alpha_composite(orb)

    image.save(PNG)
    print(PNG)


if __name__ == "__main__":
    main()
