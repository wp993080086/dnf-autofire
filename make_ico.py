# -*- coding: utf-8 -*-
"""把生成的 LOGO PNG 转为多尺寸 app.ico：圆角外转透明"""
from PIL import Image, ImageDraw

SRC = r"f:\my-github\dnf-autofire\generated-images\扁平化现代应用图标设计_圆角方形_squircle_徽标_纯_2026-10-27T10-22-31.png"
DST = r"f:\my-github\dnf-autofire\app.ico"

img = Image.open(SRC).convert("RGBA")
side = min(img.size)
img = img.crop(((img.width - side) // 2, (img.height - side) // 2,
                (img.width + side) // 2, (img.height + side) // 2))

# 圆角矩形蒙版：圆角外全透明（ squircle 半径约 22% ）
mask = Image.new("L", img.size, 0)
d = ImageDraw.Draw(mask)
d.rounded_rectangle([0, 0, img.width - 1, img.height - 1], radius=int(side * 0.22), fill=255)
img.putalpha(mask)

img.save(DST, format="ICO", sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (24, 24), (16, 16)])
print("saved", DST)

# 预览各尺寸（放大 16px 供检查）
prev = Image.new("RGBA", (300, 110), (245, 245, 245, 255))
x = 8
for s in (16, 24, 32, 48):
    ic = img.resize((s, s), Image.LANCZOS)
    prev.paste(ic, (x, 55 - s // 2), ic)
    x += s + 18
prev.save(r"f:\my-github\dnf-autofire\generated-images\ico_preview.png")
print("preview saved")
