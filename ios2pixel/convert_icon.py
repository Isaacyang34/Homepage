import os
from PIL import Image

def make_ico():
    src = "apple_android_fusion.jpg"
    dst = "app_icon.ico"
    if not os.path.exists(src):
        print(f"Error: {src} not found")
        return
    img = Image.open(src)
    # Save as multi-resolution icon
    icon_sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)]
    img.save(dst, format="ICO", sizes=icon_sizes)
    print(f"Successfully generated {dst}")

if __name__ == "__main__":
    make_ico()
