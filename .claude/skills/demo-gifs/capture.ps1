# Frame grabber. Runs alongside the demo driver and shoots the DiscWright window
# at a fixed rate until a sentinel file appears.
#
# CopyFromScreen rather than PrintWindow on purpose: the demo opens Browse
# dialogs over the window, and PrintWindow would capture the form underneath them
# as though nothing had happened.
#
# CopyFromScreen does not include the pointer, and a form whose fields fill
# themselves with nothing on screen causing it is a poor demo - the 0.4.0 GIFs
# show the pointer moving to each control. So the real cursor is drawn in
# afterwards, shape and hotspot as Windows currently has them, which keeps the
# I-beam over the text boxes.
param(
    [Parameter(Mandatory)][string]$OutDir,
    [Parameter(Mandatory)][string]$StopFile,
    [int]$IntervalMs = 100
)
$ErrorActionPreference = 'Stop'

# Every assembly first. An earlier version used System.Windows.Forms three lines
# before loading it, so this process died on startup and the whole take recorded
# nothing.
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if (-not ('DwCursor' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public class DwCursor {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
  [StructLayout(LayoutKind.Sequential)] public struct CURSORINFO {
    public int cbSize; public int flags; public IntPtr hCursor; public POINT ptScreenPos; }
  [StructLayout(LayoutKind.Sequential)] public struct ICONINFO {
    public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }
  [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO pci);
  [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO pii);
  [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon,
                                                          int w, int h, int step, IntPtr brush, int flags);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

  // Draw the pointer into a bitmap whose top left corner is at (ox, oy) on screen.
  public static void Draw(Graphics g, int ox, int oy) {
    CURSORINFO ci = new CURSORINFO();
    ci.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
    if (!GetCursorInfo(ref ci)) return;
    if (ci.flags != 1 || ci.hCursor == IntPtr.Zero) return;   // 1 = CURSOR_SHOWING
    ICONINFO ii;
    if (!GetIconInfo(ci.hCursor, out ii)) return;
    try {
      IntPtr hdc = g.GetHdc();
      try {
        DrawIconEx(hdc, ci.ptScreenPos.x - ox - ii.xHotspot, ci.ptScreenPos.y - oy - ii.yHotspot,
                   ci.hCursor, 0, 0, 0, IntPtr.Zero, 0x0003);   // DI_NORMAL
      } finally { g.ReleaseHdc(hdc); }
    } finally {
      // The two bitmaps GetIconInfo hands back are ours to free, and this runs
      // ten times a second for a minute and a half.
      if (ii.hbmMask  != IntPtr.Zero) DeleteObject(ii.hbmMask);
      if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
    }
  }
}
"@
}

$log = "$OutDir.log"
function Say($m) { Add-Content -LiteralPath $log -Value ("{0}  {1}" -f (Get-Date -Format 'HH:mm:ss.fff'), $m) }

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Set-Content -LiteralPath $log -Value 'capture starting'

try {
    $proc = $null
    for ($i = 0; $i -lt 300; $i++) {
        $proc = Get-Process powershell -ErrorAction SilentlyContinue |
                Where-Object { $_.MainWindowTitle -like 'DiscWright*' } | Select-Object -First 1
        if ($proc) { break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $proc) { Say 'no DiscWright window appeared'; exit 1 }
    Say ('found window: ' + $proc.MainWindowTitle)

    # Pin the rectangle once. The form does not move during the demo, and
    # re-reading it every frame makes the GIF jitter a pixel when Windows rounds
    # differently between calls.
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $r  = $el.Current.BoundingRectangle
    $x = [int]$r.X; $y = [int]$r.Y; $w = [int]$r.Width; $h = [int]$r.Height
    Say ("region {0}x{1} at {2},{3}" -f $w, $h, $x, $y)
    if ($w -le 0 -or $h -le 0) { Say 'bad rectangle'; exit 1 }

    # When each frame was taken, so the driver can name a stretch of the
    # recording by the clock and have the right frames cut out of it - the folder
    # dialogs, whose tree shows the user's name twice.
    $stamps = New-Object System.Collections.Generic.List[string]

    $n = 0
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $StopFile)) {
        $stamps.Add(('{0},{1}' -f $n, (Get-Date).ToString('o')))
        $bmp = New-Object System.Drawing.Bitmap($w, $h)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
            [DwCursor]::Draw($g, $x, $y)
        } catch { }
        $g.Dispose()
        $bmp.Save((Join-Path $OutDir ('f{0:D5}.png' -f $n)), [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $n++
        $wait = ($n * $IntervalMs) - $sw.ElapsedMilliseconds
        if ($wait -gt 0) { Start-Sleep -Milliseconds $wait }
    }
    Set-Content -LiteralPath "$OutDir.frames.csv" -Value $stamps -Encoding UTF8
    Say ("done: {0} frames in {1:N1}s" -f $n, $sw.Elapsed.TotalSeconds)
}
catch {
    Say ('FAILED: ' + $_.Exception.Message)
    exit 1
}
