using System.Runtime.InteropServices;

using DiscWright.Core;

namespace DiscWright.Burning;

/// <summary>
/// Every disc writer on the machine, and what is in it.
/// </summary>
/// <remarks>
/// <para>
/// Asking costs nothing and spends no disc, which is the whole reason this runs
/// before anything else. A drive that will not answer is not a fault: an empty
/// tray is the usual reason, and the answer carries why rather than throwing.
/// </para>
/// <para>
/// IMAPI wants a name for whoever is asking, and refuses some operations without
/// one. It appears in Windows' own event log, so it says what it is.
/// </para>
/// </remarks>
public sealed class WindowsRecorders(IKeepALog? log = null) : IFindRecorders
{
    internal const string WhoIsAsking = "DiscWright";

    private readonly IKeepALog _log = log ?? new NothingIsLogged();

    public IReadOnlyList<Recorder> All()
    {
        object? master = null;

        try
        {
            master = Activator.CreateInstance(
                Type.GetTypeFromProgID("IMAPI2.MsftDiscMaster2", throwOnError: true)!)!;

            dynamic drives = master;

            if (!drives.IsSupportedEnvironment)
            {
                _log.Write("Windows says this machine cannot burn discs at all.");
                return [];
            }

            List<Recorder> found = [];

            for (int at = 0; at < drives.Count; at++)
            {
                found.Add(Ask((string)drives.Item(at)));
            }

            return found;
        }
        catch (COMException wrong)
        {
            WhyNone(wrong);

            // No burning service on this machine at all, which is an answer and
            // not a failure: the image is still made and another machine can
            // burn it.
            return [];
        }
        catch (InvalidComObjectException wrong)
        {
            WhyNone(wrong);

            // NOT a COMException, which is the whole reason this line exists.
            // "Attempt has been made to use a COM object that does not have a
            // backing class factory" walked past the catch above, out of here,
            // out of the view model, and into the window's catch-all, where it
            // was shown as raw exception text in a box with a red cross.
            //
            // It reached Lazar by pressing Erase a disc: the drives are asked
            // about before anything is erased, and this is where that asking
            // happens. Nothing had gone wrong with his disc.
            return [];
        }
        catch (TypeLoadException wrong)
        {
            WhyNone(wrong);
            return [];
        }
        finally
        {
            Release(master);
        }
    }

    /// <summary>
    /// Writes down why Windows listed no drives, which nothing else ever sees.
    /// </summary>
    /// <remarks>
    /// Every way out of the catch above answers with an empty list, and an
    /// empty list reads as a machine with no writer. On 2026-11-10 that was
    /// said twice on a machine with two, and the reason was thrown away both
    /// times. The code is what tells a busy service from a missing one.
    /// </remarks>
    private void WhyNone(Exception wrong) =>
        _log.Write($"Windows would not list the drives: {FirstLineOf(wrong.Message)} (0x{wrong.HResult:X8}).");

    /// <summary>
    /// One drive, asked about itself and about whatever is in it.
    /// </summary>
    private static Recorder Ask(string id)
    {
        object? recorder = null;
        object? data = null;

        try
        {
            recorder = Activator.CreateInstance(
                Type.GetTypeFromProgID("IMAPI2.MsftDiscRecorder2", throwOnError: true)!)!;

            dynamic drive = recorder;
            drive.InitializeDiscRecorder(id);

            string letter = FirstPath(drive.VolumePathNames);
            string name = Tidied($"{drive.VendorId} {drive.ProductId}");

            data = Activator.CreateInstance(
                Type.GetTypeFromProgID("IMAPI2.MsftDiscFormat2Data", throwOnError: true)!)!;

            dynamic format = data;

            if (!format.IsRecorderSupported(recorder))
            {
                return new Recorder(id, letter, name, 0, false, 0, [], "this drive cannot write data discs");
            }

            format.Recorder = recorder;
            format.ClientName = WhoIsAsking;

            try
            {
                int media = format.CurrentPhysicalMediaType;
                bool blank = format.MediaHeuristicallyBlank;

                // Only a blank has free sectors to speak of, and asking an
                // empty or unreadable tray throws rather than answering.
                long free = blank ? (long)format.FreeSectorsOnMedia : 0;
                int[] speeds = [.. ((System.Collections.IEnumerable)format.SupportedWriteSpeeds).Cast<object>().Select(Convert.ToInt32)];

                return new Recorder(id, letter, name, media, blank, free, speeds);
            }
            catch (COMException nothingIn)
            {
                // An empty tray, or a disc Windows has not finished reading.
                // Not a fault, and the drive is still worth listing: somebody
                // is about to put a disc in it.
                //
                // WHICH OF THE TWO IT IS MATTERS, and this is the only place
                // that can tell. An empty tray has answered and a disc being
                // read has not, and everything above waits on the second. Said
                // by the code rather than by the sentence, which is localised.
                return new Recorder(
                    id, letter, name, 0, false, 0, [],
                    FirstLineOf(nothingIn.Message),
                    nothingIn.HResult == TheNumbers.NothingInTheDrive);
            }
        }
        catch (COMException wrong)
        {
            return new Recorder(id, string.Empty, "a drive", 0, false, 0, [], FirstLineOf(wrong.Message));
        }
        catch (InvalidComObjectException wrong)
        {
            // The same hole as in All, one drive down. A drive that will not
            // answer is an answer and never a throw, whichever kind of COM
            // failure it is: everything above this expects a Recorder back.
            return new Recorder(id, string.Empty, "a drive", 0, false, 0, [], FirstLineOf(wrong.Message));
        }
        finally
        {
            Release(data);
            Release(recorder);
        }
    }

    /// <summary>
    /// The drive letter, out of however many paths Windows gives for it.
    /// </summary>
    private static string FirstPath(object? paths)
    {
        if (paths is System.Collections.IEnumerable many)
        {
            foreach (object? each in many)
            {
                if (each?.ToString() is string path && path.Length > 0)
                {
                    return path.TrimEnd('\\');
                }
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// The make and model with the padding the drive reports taken out.
    /// </summary>
    /// <remarks>
    /// They come back padded to a fixed width, so "ASUS     DRW-24D5MT      "
    /// is what a drive actually says its name is.
    /// </remarks>
    private static string Tidied(string name) =>
        string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

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
}
