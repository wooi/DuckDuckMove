"""Generate an original, transparent motion fixture and the app's simple D icon."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import math

root = Path(__file__).resolve().parents[1]
samples = root / "samples"
samples.mkdir(exist_ok=True)
frames = []
for step in range(24):
    frame = Image.new("P", (192, 192), 0)
    frame.putpalette([0, 0, 0, 41, 101, 207, 118, 164, 242, 183, 209, 252] + [0] * (768 - 12))
    draw = ImageDraw.Draw(frame)
    for index in range(3):
        angle = (step / 24 + index / 3) * math.tau
        x, y = 96 + 52 * math.cos(angle), 96 + 52 * math.sin(angle)
        draw.ellipse((x - 16, y - 16, x + 16, y + 16), fill=index + 1)
    frames.append(frame)
frames[0].save(samples / "orbit.gif", save_all=True, append_images=frames[1:], duration=80, loop=0, transparency=0, disposal=2, optimize=False)
icon = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
d = ImageDraw.Draw(icon)
d.rounded_rectangle((8, 8, 248, 248), radius=52, fill="#2965CF")
font = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 155)
d.text((128, 120), "D", font=font, anchor="mm", fill="white")
d.ellipse((177, 187, 206, 216), fill="#FFD77C")
icon.save(root / "src/DuckDuckMove.App/app.ico", sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print(samples / "orbit.gif")
