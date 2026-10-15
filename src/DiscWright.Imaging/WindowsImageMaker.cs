using System.Runtime.InteropServices;

using DiscWright.Core;

namespace DiscWright.Imaging;

/// <summary>
/// Turns a staged folder into one file that is the disc, using the same
/// Windows component that burns one.
/// </summary>
/// <remarks>
/// <para>
/// Everything that can be decided with arithmetic was decided before this ran,
/// in <see cref="ImagePlan"/>, where it can be tested. What is left here is the
/// part that needs a Windows: asking IMAPI for an image and copying its stream
/// to a file.
/// </para>
/// </remarks>
public sealed class WindowsImageMaker(IFileSystem files) : IMakeImages
{
    /// <summary>UDF 2.50, which is what a modern disc is.</summary>
    private const int Udf250 = 0x250;

    /// <summary>UDF alone, and UDF with the two older ways as well.</summary>
    private const int JustUdf = 4;
    private const int UdfAndTheOldWays = 1 | 2 | 4;

    public Task<Outcome<MadeImage>> Make(
        string stagedFolder,
        string imagePath,
        string label,
        bool wantsLegacyFileSystems,
        int discNumber,
        int ofDiscs,
        IProgress<ImageProgress>? progress,
        CancellationToken ct) =>
        Task.Run(
            () => MakeIt(stagedFolder, imagePath, label, wantsLegacyFileSystems, discNumber, ofDiscs, progress, ct),
            ct);

    private Outcome<MadeImage> MakeIt(
        string stagedFolder,
        string imagePath,
        string label,
        bool wantsLegacyFileSystems,
        int discNumber,
        int ofDiscs,
        IProgress<ImageProgress>? progress,
        CancellationToken ct)
    {
        FolderListing staged = files.List(stagedFolder);

        if (staged.Files.Count == 0)
        {
            return Refusals.ThereIsNothingToPutOnADisc();
        }

        ImagePlan plan = ImagePlan.For(
            label,
            staged.Bytes,
            staged.Files.Max(file => file.Bytes),
            wantsLegacyFileSystems,
            discNumber,
            ofDiscs);

        if (ClearTheWay(imagePath) is Refusal inTheWay)
        {
            return inTheWay;
        }

        // Every one of these has to be let go of by hand. AddTree opens a handle
        // on each staged file and the result image holds them until it is
        // released, so leaving it to the collector means DiscWright is still
        // locking its own disc folder when the build returns. In v1 that showed
        // up as a rebuild failing with "something is still using it", blaming
        // Explorer or a mounted image for a lock DiscWright was holding itself.
        object? image = null;
        object? root = null;
        object? result = null;
        object? stream = null;

        try
        {
            image = Activator.CreateInstance(
                Type.GetTypeFromProgID("IMAPI2FS.MsftFileSystemImage", throwOnError: true)!)!;

            dynamic fsi = image;

            fsi.FreeMediaBlocks = (int)plan.Blocks;
            fsi.FileSystemsToCreate = plan.LegacyFileSystems ? UdfAndTheOldWays : JustUdf;

            // Older Windows will not take 2.50 and says so; the image is still
            // perfectly good at whatever it will take.
            try
            {
                fsi.UDFRevision = Udf250;
            }
            catch (COMException)
            {
            }

            fsi.VolumeName = plan.VolumeName;

            // Bound once. Reading Root makes a fresh wrapper every time, and a
            // wrapper nobody kept is a wrapper nobody can release.
            root = fsi.Root;
            ((dynamic)root).AddTree(System.IO.Path.GetFullPath(stagedFolder), false);

            ct.ThrowIfCancellationRequested();

            result = fsi.CreateResultImage();

            dynamic made = result;
            stream = made.ImageStream;

            int blockSize = made.BlockSize;
            int totalBlocks = made.TotalBlocks;

            Copy.StreamToFile(
                imagePath,
                stream!,
                blockSize,
                totalBlocks,
                plan.ReportEvery,
                (done, all) => progress?.Report(new ImageProgress(done, all)),
                ct);

            return new MadeImage(imagePath, new System.IO.FileInfo(imagePath).Length, plan.Worry);
        }
        catch (COMException wrong)
        {
            return Refusals.WindowsCouldNotMakeTheImage(wrong.Message);
        }
        finally
        {
            foreach (object? each in new[] { stream, result, root, image })
            {
                if (each is not null)
                {
                    try
                    {
                        Marshal.FinalReleaseComObject(each);
                    }
                    catch (ArgumentException)
                    {
                    }
                }
            }

            // Letting go drops our references; the handles close when the
            // finalisers run. Waiting for them here is what makes an immediate
            // rebuild work instead of failing on a lock that clears a few
            // seconds later.
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>
    /// Takes the old image out of the way, or explains why it will not go.
    /// </summary>
    private static Refusal? ClearTheWay(string imagePath)
    {
        if (!System.IO.File.Exists(imagePath))
        {
            return null;
        }

        try
        {
            System.IO.File.Delete(imagePath);
            return null;
        }
        catch (Exception wrong) when (wrong is System.IO.IOException or UnauthorizedAccessException)
        {
            return Refusals.TheOldImageWillNotMoveAside(imagePath, wrong.Message);
        }
    }
}
