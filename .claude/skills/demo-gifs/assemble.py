# Turn a folder of captured frames into a GIF the size and shape of the ones in
# docs/, which are 672 wide and run at 80 ms a frame.
#
# Three things happen here that a plain "save every frame" would not do:
#
#   1. The Add game segment is cut. The Browse For Folder tree shows the OneDrive
#      node, which carries a real name, so the GIF jumps from the empty form to
#      the game already in the list. The 0.4.0 GIF cuts in exactly the same
#      place, for what was presumably the same reason.
#   2. Dead time is compressed. The driver pauses after each step so a viewer can
#      read what changed, but a pause longer than about half a second is just a
#      still image costing frames.
#   3. The last frame is held, so the menu preview the recording ends on stays up
#      long enough to be looked at before the loop restarts.
import sys, os, glob, datetime
from PIL import Image, ImageChops

SRC   = sys.argv[1]
DEST  = sys.argv[2]
# The recorder writes the stretches it wants dropped, by the clock, and the
# capture writes the time of every frame. Given both, the segments to cut are
# known rather than guessed from how much the picture changed.
CUTS  = sys.argv[3] if len(sys.argv) > 3 else os.path.join(
    os.path.dirname(SRC), 'cuts-' + os.path.basename(SRC).replace('frames-', '') + '.txt')
STAMPS = SRC + '.frames.csv'

CROP  = (7, 0, 693, 985)      # the window itself, without the shadow around it
WIDTH = 672
MS    = 80
HOLD_MS = 2500
STILL_KEEP = 6                # frames of stillness kept before skipping ahead
BUILD_KEEP = 1                # of every N frames, while only the progress bar moves

files = sorted(glob.glob(os.path.join(SRC, '*.png')))
if not files:
    sys.exit('no frames in ' + SRC)

def small(path):
    return Image.open(path).convert('L').resize((175, 248))

def changed(a, b, thresh=24):
    d = ImageChops.difference(a, b)
    return sum(1 for p in d.get_flattened_data() if p > thresh)

def parse_time(s):
    return datetime.datetime.fromisoformat(s.strip())

# Which frame was on screen at a given moment.
stamps = {}
if os.path.exists(STAMPS):
    for line in open(STAMPS, encoding='utf-8-sig'):
        if ',' not in line:
            continue
        n, when = line.split(',', 1)
        stamps[int(n)] = parse_time(when)

dropped = set()
if stamps and os.path.exists(CUTS):
    for line in open(CUTS, encoding='utf-8-sig'):
        if ',' not in line:
            continue
        a, b = line.split(',', 1)
        a, b = parse_time(a), parse_time(b)
        seg = [n for n, t in stamps.items() if a <= t <= b]
        dropped.update(seg)
        if seg:
            print('cut frames %d-%d (a folder dialog)' % (min(seg), max(seg)))
elif not stamps:
    print('no frame timestamps; nothing can be cut by the clock')

keep = [i for i in range(len(files)) if i not in dropped]

# Compress stillness, and thin the build right down.
#
# The build is minutes of a form that never changes except for a progress bar and
# a clock in the bottom strip. Keeping it whole would swamp the GIF, and dropping
# it would lose the only proof the thing produces a disc, so the frames where
# nothing above the strip moves are sampled instead.
BOTTOM = 860 * 248 // 992          # the strip, in the small image's rows
def moved_above_strip(a, b):
    d = ImageChops.difference(a, b).crop((0, 0, 175, BOTTOM))
    return sum(1 for p in d.get_flattened_data() if p > 24) > 8

out, last, still, since = [], None, 0, 0
for i in keep:
    cur = small(files[i])
    if last is None:
        out.append(i); last = cur; continue
    if moved_above_strip(cur, last):
        out.append(i); last = cur; still = 0; since = 0
    elif changed(cur, last) > 8:
        since += 1
        if since % 20 == 0:            # the progress bar, a couple of times a second
            out.append(i); last = cur
    else:
        still += 1
        if still <= STILL_KEEP:
            out.append(i)
print('frames: %d captured, %d cut, %d kept' % (len(files), len(dropped), len(out)))

def prepared(path):
    im = Image.open(path).convert('RGB').crop(CROP)
    h = round(im.height * WIDTH / im.width)
    return im.resize((WIDTH, h), Image.LANCZOS)

rgb = [prepared(files[i]) for i in out]

# One palette for the whole film, split by what the two halves of it need.
#
# It has to be one palette: give each frame its own and Pillow stores every frame
# whole, 19 MB for this recording, instead of storing the rectangle that changed.
#
# But the halves are nothing alike. The form is two dozen flat greys and needs
# almost none of the palette; the menu artwork is a photograph of fog and needs
# all of it. Quantizing the two together spent the palette on greys and banded
# the fog into contour rings. So the busiest frame gets 208 entries and the
# plainest gets the remaining 48, which is more than the form has colours.
#
# Dithering is not the answer here: error diffusion spreads a changed pixel
# across everything below it, so a moving pointer redraws the whole frame and the
# file goes to 13 MB.
def variety(im):
    return len(set(im.resize((168, 241)).get_flattened_data()))

art  = max(rgb, key=variety)
form = min(rgb, key=variety)
pal = Image.new('P', (1, 1))
pal.putpalette(art.quantize(colors=208, method=Image.MEDIANCUT).getpalette()[:208 * 3] +
               form.quantize(colors=48, method=Image.MEDIANCUT).getpalette()[:48 * 3])

frames = [im.quantize(palette=pal, dither=Image.NONE) for im in rgb]

dur = [MS] * len(frames)
dur[-1] = HOLD_MS
dur[0] = 600
frames[0].save(DEST, save_all=True, append_images=frames[1:], loop=0,
               duration=dur, optimize=True, disposal=1)
print('%s  %dx%d  %d frames  %.1f MB  %.1fs' %
      (DEST, frames[0].width, frames[0].height, len(frames),
       os.path.getsize(DEST) / 1e6, sum(dur) / 1000.0))
