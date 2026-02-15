#!/usr/bin/env python3
from PIL import Image, ImageDraw
import os

out_dir = r"src\Resources"
os.makedirs(out_dir, exist_ok=True)

# Spinner frames - blue arc rotating
for i in range(8):
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    start_angle = i * 45
    end_angle = start_angle + 90
    # Draw arc on bounding box [1,1,14,14] for a centered circle
    draw.arc([1, 1, 14, 14], start_angle, end_angle, fill=(0, 102, 204, 255), width=2)
    img.save(os.path.join(out_dir, f"spinner_{i}.png"))
    print(f"Generated spinner_{i}.png")

# Bell icon - red/orange
img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)
bell_color = (220, 80, 40, 255)
# Bell dome
draw.pieslice([3, 1, 12, 10], 180, 360, fill=bell_color)
# Bell body (rectangle below dome)
draw.rectangle([3, 6, 12, 11], fill=bell_color)
# Bell rim (wider at bottom)
draw.rectangle([2, 11, 13, 13], fill=bell_color)
# Clapper (small circle at bottom center)
draw.ellipse([6, 13, 9, 15], fill=bell_color)
img.save(os.path.join(out_dir, "bell.png"))
print("Generated bell.png")
print("All icons generated successfully!")
