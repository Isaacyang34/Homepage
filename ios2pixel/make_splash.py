import os
from PIL import Image, ImageDraw, ImageFont

def create_splash_image():
    src_img = "apple_android_fusion.jpg"
    dst_img = "splash.png"
    if not os.path.exists(src_img):
        print(f"Source image {src_img} not found.")
        return

    # Base canvas: 600 x 420 (Modern Dark Tech Card)
    canvas_w, canvas_h = 600, 420
    canvas = Image.new("RGBA", (canvas_w, canvas_h), (18, 19, 24, 255))
    
    # Load and resize logo
    logo = Image.open(src_img).convert("RGBA")
    logo = logo.resize((240, 240), Image.Resampling.LANCZOS)
    
    # Paste logo in top center
    canvas.paste(logo, (180, 20))

    draw = ImageDraw.Draw(canvas)
    
    # Modern card border
    draw.rectangle([(0, 0), (canvas_w - 1, canvas_h - 1)], outline=(45, 48, 60), width=2)
    
    # Use native Microsoft JhengHei fonts (guaranteed 0% tofu glitch on Windows)
    font_path_bold = r"C:\Windows\Fonts\msjhbd.ttc"
    font_path_reg = r"C:\Windows\Fonts\msjh.ttc"
    
    try:
        font_title = ImageFont.truetype(font_path_bold, 24)
        font_sub = ImageFont.truetype(font_path_bold, 13)
        font_hint = ImageFont.truetype(font_path_reg, 12)
    except Exception:
        font_title = font_sub = font_hint = ImageFont.load_default()

    # Title text
    draw.text((canvas_w // 2, 280), "iOS 2 Pixel Pro", fill=(255, 255, 255), font=font_title, anchor="mm")
    
    # Subtitle text
    draw.text((canvas_w // 2, 312), "iPhone  ➔  電腦緩衝庫  ➔  Google Pixel 1", fill=(0, 230, 118), font=font_sub, anchor="mm")
    
    # Progress Bar Track (Background groove)
    bar_x1, bar_y1, bar_x2, bar_y2 = 80, 345, 520, 353
    draw.rectangle([(bar_x1, bar_y1), (bar_x2, bar_y2)], fill=(28, 30, 38), outline=(50, 53, 68), width=1)
    
    # Progress Bar Fill (Modern neon emerald glowing bar)
    draw.rectangle([(bar_x1 + 1, bar_y1 + 1), (bar_x1 + 280, bar_y2 - 1)], fill=(0, 230, 118))

    # Dynamic loading hint
    draw.text((canvas_w // 2, 380), "正在啟動獨立執行環境，請稍候...", fill=(150, 155, 175), font=font_hint, anchor="mm")

    canvas.save(dst_img, "PNG")
    print(f"Successfully generated crystal-clear splash: {dst_img}")

if __name__ == "__main__":
    create_splash_image()
