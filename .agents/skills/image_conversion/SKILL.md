---
name: image_conversion
description: Guidelines for converting images to WebP format, ensuring standard 85 quality compression.
---

# Image Conversion Guidelines

## Guidelines
- **Format**: Convert JPG and PNG assets to the WebP format for optimal loading performance.
- **Compression Quality**: Always convert WebP images with a quality of **85**.

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
