---
name: image_conversion
description: Guidelines for image formats: WebP at quality 85 for everything shown in the browser UI, and PNG (or JPEG) for images embedded in downloadable documents such as Word files.
---

# Image Conversion Guidelines

## Guidelines
- **Browser UI: WebP.** Images shown in the web user interface are WebP at quality 85. Convert JPG and PNG assets to the WebP format for optimal loading performance.
- **Downloadable documents: PNG.** Images embedded in files a user downloads and opens in another application — Word (`.docx`) and similar office formats (`.xlsx`, `.pptx`, `.odt`) — are PNG, or JPEG for a photograph, because WebP pictures do not open in Word 2019, Word 2021 or LibreOffice. Such images are server-side resources that never reach the browser, so WebP's loading-performance purpose does not apply. A renderer that decodes WebP itself before embedding (QuestPDF for the benchmark PDFs) may keep using the WebP originals.
- **Compression Quality**: Always convert WebP images with a quality of **85**.

## When Not to Convert

Images that exist only to be embedded in downloadable documents stay PNG or JPEG. The standing
example is the pair of GnollBench logos in `Overseer/Resources/Word/`, which the Word renderer embeds
in every `.docx` it produces.

## Examples

### Python (Pillow)
```python
from PIL import Image

with Image.open("image.jpg") as im:
    im.save("image.webp", "webp", quality=85)
```

### CLI (cwebp)
```bash
cwebp -q 85 image.jpg -o image.webp
```
