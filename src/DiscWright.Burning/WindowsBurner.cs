using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

using DiscWright.Core;

namespace DiscWright.Burning;

/// <summary>
/// Writes an image to a disc, and erases one when asked to.
/// </summary>
/// <remarks>
/// <para>
/// Everything answerable was answered before this ran. What is left is the part
/// that spends a disc.
/// </para>
/// <para>
/// <strong>Nothing here erases on its own initiative.</strong> Erasing is a
/// separate call, made only when somebody has said so, because whatever is on
/// the disc goes.
/// </para>
/// </remarks>
public sealed class WindowsBurner : IBurnDiscs, IEraseDiscs, IWorkTheTray
{
    public async Task<Outcome<BurnedDisc>> Burn(
        string imagePath,
        Recorder onto,
        IProgress<BurnProgress>? progress,
        CancellationToken ct) =>
        await Task.Run(() => Write(imagePath, onto, progress, ct), ct);

    /// <summary>
    /// Erases a rewritable disc, taking everything on it.
    /// </summary>
    /// <remarks>
    /// A quick erase, which blanks the table of contents in well under a
    /// minute, rather than a full one, which rewrites the whole surface and
    /// takes twenty minutes on a CD-RW. The disc is usable either way.
    /// </remarks>
    public Task<Outcome<string>> BlankIt(Recorder disc, IProgress<string>? progress, CancellationToken ct) =>
        Task.Run(() => DoTheErase(disc, progress), ct);

    private static Outcome<BurnedDisc> Write(
        string imagePath,
        Recorder onto,
        IProgress<BurnProgress>? progress,
        CancellationToken ct)
    {
        if (!System.IO.File.Exists(imagePath))
        {
            return Refusals.ThereIsNothingToPutOnADisc();
        }

        object? recorder = null;
        object? format = null;
        IStream? reading = null;
        int reports = 0;
        Stopwatch clock = Stopwatch.StartNew();

        try
        {
            recorder = Open(onto.Id);

            format = Activator.CreateInstance(
                Type.GetTypeFromProgID("IMAPI2.MsftDiscFormat2Data", throwOnError: true)!)!;

            dynamic writer = format;
            writer.Recorder = recorder;
            writer.ClientName = WindowsRecorders.WhoIsAsking;

            // Closed, so it reads everywhere. A disc left open reads on the
            // machine that wrote it and confuses older drives, which are
            // exactly the audience for a game on a disc.
            writer.ForceMediaToBeClosed = true;

            if (WriteSpeeds.Choose(onto.Speeds, onto.MediaType) is int sectors)
            {
                try
                {
                    writer.SetWriteSpeed(sectors, false);
                }
                catch (COMException)
                {
                    // The drive would not take it, so its own choice stands.
                    // Not worth stopping a burn over.
                }
            }

            using Listening listening = Listening.To(format, new TheWriteSoFar(step =>
            {
                reports++;
                progress?.Report(step);
            }));

            reading = SHCreateStreamOnFileEx(imagePath, StgmRead, 0, false, null);

            progress?.Report(new BurnProgress("starting the drive", null, null));

            ct.ThrowIfCancellationRequested();
            writer.Write(reading);

            clock.Stop();

            // Ejected on purpose. The check afterwards has to read a freshly
            // mounted volume rather than whatever Windows remembered about the
            // blank that used to be in there.
            try
            {
                ((dynamic)recorder).EjectMedia();
            }
            catch (COMException)
            {
            }

            return new BurnedDisc(onto.Drive, (int)clock.Elapsed.TotalSeconds, reports, listening.Attached);
        }
        catch (COMException wrong)
        {
            return Refusals.TheDriveCouldNotWriteIt(onto.Drive, FirstLineOf(wrong.Message));
        }
        finally
        {
            Release(reading);
            Release(format);
            Release(recorder);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private static Outcome<string> DoTheErase(Recorder disc, IProgress<string>? progress)
    {
        if (!OpticalMedia.CanBeErased(disc.MediaType))
        {
            return Refusals.ThatDiscCannotBeWrittenTo(disc.Drive, disc.Holding);
        }

        object? recorder = null;
        object? eraser = null;

        try
        {
            recorder = Open(disc.Id);

            eraser = Activator.CreateInstance(
                Type.GetTypeFromProgID("IMAPI2.MsftDiscFormat2Erase", throwOnError: true)!)!;

            dynamic erasing = eraser;
            erasing.Recorder = recorder;
            erasing.ClientName = WindowsRecorders.WhoIsAsking;
            erasing.FullErase = false;

            using Listening listening = Listening.To(eraser, new TheEraseSoFar(progress));

            progress?.Report("erasing the disc");
            erasing.EraseMedia();

            return $"The {disc.Holding} in {disc.Drive} is blank again.";
        }
        catch (COMException wrong)
        {
            return Refusals.TheDriveCouldNotWriteIt(disc.Drive, FirstLineOf(wrong.Message));
        }
        finally
        {
            Release(eraser);
            Release(recorder);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>
    /// Shuts the tray, and says whether the drive would.
    /// </summary>
    /// <remarks>
    /// A slim drive, and most of the ones in a laptop, cannot shut its own
    /// tray: it has to be pushed. False is that answer and not a fault.
    /// </remarks>
    public bool ShutIt(Recorder drive)
    {
        object? recorder = null;

        try
        {
            recorder = Open(drive.Id);
            ((dynamic)recorder).CloseTray();

            return true;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            Release(recorder);
        }
    }

    private static object Open(string id)
    {
        object recorder = Activator.CreateInstance(
            Type.GetTypeFromProgID("IMAPI2.MsftDiscRecorder2", throwOnError: true)!)!;

        ((dynamic)recorder).InitializeDiscRecorder(id);

        return recorder;
    }

    private static string FirstLineOf(string message) =>
        message.Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries) is [string first, ..] ? first : message;

    private static void Release(object? com)
    {
        if (com is null)
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(com);
        }
        catch (ArgumentException)
        {
        }
    }

    private const uint StgmRead = 0;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern IStream SHCreateStreamOnFileEx(
        string file, uint mode, uint attributes, [MarshalAs(UnmanagedType.Bool)] bool create, IStream? template);
}

/// <summary>
/// Listening to a COM object for as long as this is held.
/// </summary>
/// <remarks>
/// Connection points have to be let go of by hand, and a sink left attached to
/// a drive is a drive that cannot be used again until the program closes.
/// </remarks>
internal sealed class Listening : IDisposable
{
    private IConnectionPoint? _point;
    private readonly int _cookie;

    /// <summary>Whether the drive took the listener at all.</summary>
    internal bool Attached => _attached;

    private readonly bool _attached;

    private Listening(IConnectionPoint? point, int cookie)
    {
        _point = point;
        _cookie = cookie;
        _attached = point is not null;
    }

    /// <summary>Not listening, because the drive would not have it.</summary>
    private static Listening Deaf => new(null, 0);

    internal static Listening To(object com, object sink)
    {
        if (com is not IConnectionPointContainer container)
        {
            return Deaf;
        }

        Guid which = sink is TheWriteSoFar
            ? typeof(IHearAboutTheWrite).GUID
            : typeof(IHearAboutTheErase).GUID;

        try
        {
            container.FindConnectionPoint(ref which, out IConnectionPoint? point);

            if (point is null)
            {
                return Deaf;
            }

            point.Advise(sink, out int cookie);

            return new Listening(point, cookie);
        }
        catch (COMException)
        {
            // A drive that will not report is not a drive that cannot write.
            // v1 learned to say which of the two silences it was looking at
            // rather than treating them the same.
            return Deaf;
        }
    }

    public void Dispose()
    {
        if (_point is null)
        {
            return;
        }

        try
        {
            _point.Unadvise(_cookie);
        }
        catch (COMException)
        {
        }

        Marshal.ReleaseComObject(_point);
        _point = null;
    }
}

/// <summary>What the drive says while it writes.</summary>
/// <remarks>
/// <para>
/// Every figure is asked for on its own. They do not all come from the same
/// place: what the drive is doing belongs to the data write, and the sector
/// counts belong to the engine underneath it, so a report can carry one and not
/// the other depending on the drive.
/// </para>
/// <para>
/// <strong>The call is counted before anything is read from it.</strong> The
/// first version counted at the end, after the figures, so a report that
/// arrived and could not be read counted as no report at all, and the burn said
/// the drive had gone quiet when it had been talking the whole time.
/// </para>
/// </remarks>
[ClassInterface(ClassInterfaceType.None)]
[ComVisible(true)]
public sealed class TheWriteSoFar(Action<BurnProgress> say) : IHearAboutTheWrite
{
    public void Update(object sender, object progress)
    {
        dynamic how = progress;

        // Converted rather than cast. These come back as thirty-two bit
        // numbers, and casting a boxed one to a wider type through dynamic
        // throws rather than widening, so every sector figure came back as
        // nothing and the bar had no percentage on it while the drive was
        // reporting them perfectly well.
        string doing = Of(() => WhatTheDriveIsDoing.InWords(Convert.ToInt32(how.CurrentAction))) ?? "writing the disc";
        long? written = Of<long?>(() => Convert.ToInt64(how.LastWrittenLba));
        long? start = Of<long?>(() => Convert.ToInt64(how.StartLba));
        long? sectors = Of<long?>(() => Convert.ToInt64(how.SectorCount));

        // RemainingTime, which is its name. There is no EstimatedRemainingTime
        // on it, and asking for one throws where nobody is looking.
        int? left = Of<int?>(() => Convert.ToInt32(how.RemainingTime));

        double? far = written is long at && start is long from && sectors is long all && all > 0
            ? Math.Clamp((at - from) / (double)all, 0, 1)
            : null;

        say(new BurnProgress(doing, far, left));
    }

    /// <summary>
    /// One figure, or nothing, because a drive that will not give a figure is
    /// not a reason to stop writing a disc.
    /// </summary>
    private static T? Of<T>(Func<T?> ask)
    {
        try
        {
            return ask();
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return default;
        }
        catch (COMException)
        {
            return default;
        }
        catch (InvalidCastException)
        {
            return default;
        }
    }
}

/// <summary>What the drive says while it erases.</summary>
[ClassInterface(ClassInterfaceType.None)]
[ComVisible(true)]
public sealed class TheEraseSoFar(IProgress<string>? say) : IHearAboutTheErase
{
    public void Update(object sender, int elapsedSeconds, int estimatedTotalSeconds) =>
        say?.Report(estimatedTotalSeconds > 0
            ? $"erasing the disc, {elapsedSeconds} of about {estimatedTotalSeconds} seconds"
            : $"erasing the disc, {elapsedSeconds} seconds so far");
}
