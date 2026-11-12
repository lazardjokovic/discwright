"""Renders the Store package's logos from discwright.svg, at every size Windows
and the Store ask for.

    python packaging\\msix\\Render-TheLogos.py

The PNGs are committed beside this script, so building a package never needs
a browser. Run this again only when the icon changes, and commit what it
writes. Headless Edge draws the SVG, transparent where the SVG is, at exactly
the pixel size asked for, so no size is a scaled copy of another.

Names follow the qualifiers Windows reads through resources.pri: a scale for
the logos drawn at a size in effective pixels, and a target size for the
small icon Windows draws at an exact pixel size, in the taskbar and the title
bar. altform-unplated is the same icon for the places Windows draws it with
no plate of its own behind it; this icon brings its own plate.
"""
import os
import subprocess
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
SVG = os.path.join(HERE, "discwright.svg")
ASSETS = os.path.join(HERE, "Assets")
EDGE = os.path.join(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"),
                    "Microsoft", "Edge", "Application", "msedge.exe")

SCALES = {100: 1.0, 125: 1.25, 150: 1.5, 200: 2.0, 400: 4.0}

# Name: the size at 100%.
SCALED = {
    "StoreLogo": 50,
    "Square44x44Logo": 44,
    "Square150x150Logo": 150,
}

TARGET_SIZES = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256]


def render(side, out):
    page = tempfile.NamedTemporaryFile("w", suffix=".html", delete=False, encoding="utf-8")
    page.write(
        "<!doctype html><html><head><style>html,body{margin:0;padding:0;background:transparent;overflow:hidden}"
        f"img{{display:block;width:{side}px;height:{side}px}}</style></head>"
        f'<body><img src="file:///{SVG.replace(os.sep, "/")}"></body></html>'
    )
    page.close()
    # Three tries: headless Edge now and then never finishes a screenshot,
    # and a fresh profile on the next try does.
    for attempt in range(3):
        profile = tempfile.mkdtemp(prefix="edge-render-")
        try:
            subprocess.run(
                [EDGE, "--headless=new", "--disable-gpu", "--hide-scrollbars", f"--user-data-dir={profile}",
                 "--force-device-scale-factor=1", "--default-background-color=00000000",
                 f"--window-size={side},{side}", f"--screenshot={out}", "file:///" + page.name.replace(os.sep, "/")],
                check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=60)
            break
        except subprocess.TimeoutExpired:
            if attempt == 2:
                raise
    os.unlink(page.name)


def main():
    os.makedirs(ASSETS, exist_ok=True)
    for old in os.listdir(ASSETS):
        if old.endswith(".png"):
            os.remove(os.path.join(ASSETS, old))

    written = []
    for name, base in SCALED.items():
        for scale, factor in SCALES.items():
            out = os.path.join(ASSETS, f"{name}.scale-{scale}.png")
            # Halves round up, as Microsoft's own table of sizes does: 50 at
            # 125% is 63, where Python's round would make it 62.
            render(int(base * factor + 0.5), out)
            written.append(out)

    for side in TARGET_SIZES:
        for form in ("", "_altform-unplated", "_altform-lightunplated"):
            out = os.path.join(ASSETS, f"Square44x44Logo.targetsize-{side}{form}.png")
            render(side, out)
            written.append(out)

    print(f"{len(written)} logos in {ASSETS}")


if __name__ == "__main__":
    main()
