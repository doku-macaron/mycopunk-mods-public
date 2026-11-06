"""Placeholder Thunderstore icons (256x256 PNG): a coloured tile with a short label per package.

python packaging/make_icons.py   -> mods/<folder>/thunderstore/icon.png (skips folders that already have one
                                    unless --force)
"""
import colorsys, json, os, sys
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LABELS = {
    "MacaronCore": "CORE", "UpgradeLagFix": "LAG", "QuickTimers": "FAST", "HealthNumbers": "HP", "DPSChecker": "DPS",
    "MissionLog": "LOG", "MinimapNorthUp": "N", "PingDistance": "m", "AbilityCooldown": "CD",
    "ThrowPreview": "ARC", "XPNumbers": "XP", "WeaponCycle": "123", "SystemMonitor": "FPS",
    "SpeedMonitor": "m/s", "HealthRegenTimer": "REGEN",
}
FONT = r"C:\Windows\Fonts\segoeuib.ttf"
SMALL = r"C:\Windows\Fonts\segoeui.ttf"


def font(path, size):
    try:
        return ImageFont.truetype(path, size)
    except OSError:
        return ImageFont.load_default()


def icon(label, hue, path):
    img = Image.new("RGB", (256, 256))
    d = ImageDraw.Draw(img)
    top = tuple(int(c * 255) for c in colorsys.hsv_to_rgb(hue, 0.55, 0.55))
    bottom = tuple(int(c * 255) for c in colorsys.hsv_to_rgb(hue, 0.65, 0.25))
    for y in range(256):
        t = y / 255
        d.line([(0, y), (255, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(top, bottom)))
    # Bevelled frame, like the game's cut-corner boxes.
    c = 28
    d.line([(c, 12), (243, 12), (243, 243 - c), (243 - c, 243), (12, 243), (12, c), (c, 12)], fill=(240, 240, 240), width=6)
    size = 120
    while size > 20:
        f = font(FONT, size)
        w = d.textlength(label, font=f)
        if w <= 196:
            break
        size -= 4
    box = d.textbbox((0, 0), label, font=f)
    x = (256 - (box[2] - box[0])) / 2 - box[0]
    y = (236 - (box[3] - box[1])) / 2 - box[1]
    d.text((x, y), label, font=f, fill=(255, 255, 255))
    s = font(SMALL, 20)
    tag = "doku_macaron"
    d.text(((256 - d.textlength(tag, font=s)) / 2, 202), tag, font=s, fill=(220, 220, 220))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


def main():
    force = "--force" in sys.argv
    spec = json.load(open(os.path.join(ROOT, "packaging", "packages.json"), encoding="utf-8"))
    n = len(spec["packages"])
    for i, p in enumerate(spec["packages"]):
        path = os.path.join(ROOT, "mods", p["folder"], "thunderstore", "icon.png")
        if os.path.exists(path) and not force:
            continue
        icon(LABELS.get(p["name"], p["name"][:3].upper()), i / n, path)
        print("icon:", path)


if __name__ == "__main__":
    main()
