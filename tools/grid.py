"""캡처 여러 장을 한 장으로 이어 붙인다 (이펙트 흐름 확인용).
python tools/grid.py <out.png> <x0,y0,x1,y1> <열 수> <그림...>"""
import sys
from PIL import Image

out, box, cols = sys.argv[1], tuple(int(v) for v in sys.argv[2].split(",")), int(sys.argv[3])
ims = [Image.open(p).crop(box) for p in sys.argv[4:]]
w, h = ims[0].size
rows = (len(ims) + cols - 1) // cols
sheet = Image.new("RGB", (w * cols, h * rows))
for i, im in enumerate(ims):
    sheet.paste(im, ((i % cols) * w, (i // cols) * h))
sheet.save(out)
