# -*- coding: utf-8 -*-
"""把界面要用的字体裁成小程序能扛的体积。

- assets\\digitfont.ttf  Fredoka（OFL，圆润无衬线）只留 ASCII 数字/字母 —— 专门显示速率、延迟等数字
- assets\\cjkfont.ttf    站酷快乐体，裁到界面里真正出现的中文字 —— 标题那种手写可爱感

数字为什么不用中文标题字体：站酷快乐体/黄油体的阿拉伯数字是风格化图形（0 画成空心方块），
速率会变成「▯ B/s」，所以数字必须用一套真正的拉丁数字字体。

用法：  .venv\\Scripts\\python.exe tools\\make-font.py
"""
import io
import os
import re
import sys

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")

LATIN = (
    "0123456789"
    "abcdefghijklmnopqrstuvwxyz"
    "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
    " .,:;!?()[]{}<>/\\|@#$%^&*-_+=~\"'`•·…—–°‰±×÷→←↑↓▲▼★☆♥✓✕⚡～"
    "，。、：；！？（）【】《》「」“”‘’％＋－＝"
)

# 快乐体里这些常用字即使源码里暂时没有也一并留着，方便以后加文案
SAFE_CJK = "的一是不了在人有我他这为之大来以个中上们到说国和地也子时道出而要于就下得可你年生自会那后能对着事其里所去行过家十用发天如然作方成者多日都三小军二无同么经法当起与好上学进种将还分此心前面又定见只主没公从"


def collect_text():
    """把 src 下所有 .cs 里出现的字符（ASCII / 中文 / 全角标点）收集起来。"""
    chars = set(LATIN) | set(SAFE_CJK)
    pat = re.compile(r"[\u3000-\u303F\u4E00-\u9FFF\uFF00-\uFFEF\u2000-\u206F]")
    for dirpath, _, files in os.walk(SRC):
        for name in files:
            if not name.endswith(".cs"):
                continue
            with io.open(os.path.join(dirpath, name), encoding="utf-8-sig") as fh:
                for ch in fh.read():
                    if ch.isascii() or pat.match(ch):
                        chars.add(ch)
    return "".join(sorted(chars))


def subset_font(font_in, font_out, text):
    if not os.path.exists(font_in):
        sys.exit("missing source font: " + font_in)
    args = [
        font_in,
        "--output-file=" + font_out,
        "--text=" + text,
        "--layout-features=",
        "--no-hinting",
        "--desubroutinize",
        "--drop-tables+=GSUB,GPOS,GDEF,DSIG",
        "--name-IDs=*",
        "--name-legacy",
        "--recalc-bounds",
        "--recalc-timestamp",
        "--no-prune-unicode-ranges",
    ]
    subset.main(args)

    f = TTFont(font_out)
    cmap = f.getBestCmap()
    cjk = sum(1 for c in text if "\u4e00" <= c <= "\u9fff")
    print("out=%-26s %7d bytes  glyphs=%4d  cjk=%d"
          % (os.path.relpath(font_out, ROOT), os.path.getsize(font_out), f["maxp"].numGlyphs, cjk))
    return cmap


def build_digit(text):
    """Fredoka 是可变字体：先固定 wght=600（圆润半粗，小字号最清楚），再子集化。"""
    src = os.path.join(ROOT, "tools", "fontsrc", "fredoka.ttf")
    if not os.path.exists(src):
        sys.exit("missing source font: " + src)

    tmp = os.path.join(ROOT, "tools", "fontsrc", "fredoka-600.ttf")
    font = TTFont(src)
    axis_tags = [a.axisTag for a in font["fvar"].axes]
    pins = {}
    if "wght" in axis_tags:
        pins["wght"] = 600
    if "wdth" in axis_tags:
        pins["wdth"] = 100
    instancer.instantiateVariableFont(font, pins, inplace=True)
    font.save(tmp)
    font.close()

    latin = "".join(c for c in text if c.isascii()) or "0123456789"
    return subset_font(tmp, os.path.join(ROOT, "assets", "digitfont.ttf"), latin)


def main():
    text = collect_text()
    print("chars=%d" % len(text))

    build_digit(text)
    cmap = subset_font(os.path.join(ROOT, "tools", "fontsrc", "ZCOOLKuaiLe-Regular.ttf"),
                       os.path.join(ROOT, "assets", "cjkfont.ttf"), text)

    missing = [c for c in text if ord(c) not in cmap and not c.isspace()]
    if missing:
        print("missing in cjk font: " + " ".join("U+%04X" % ord(c) for c in missing))


if __name__ == "__main__":
    main()
