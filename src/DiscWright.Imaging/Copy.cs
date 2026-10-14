using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace DiscWright.Imaging;

/// <summary>
/// The loop that writes the image out, block by block.
/// </summary>
/// <remarks>
/// <para>
/// <strong>There is no unsafe code here, and that is deliberate.</strong> v1's
/// first version took the address of a local with a pointer, which meant
/// compiling with /unsafe. Four unmanaged bytes do the same job, and the
/// difference matters: <strong>Smart App Control refuses an assembly that
/// combines unsafe pointers with a delegate invoked inside a copy loop</strong>,
/// because that is the shape of a shellcode loader. Measured on this machine,
/// the pointer version was refused every time and this one is accepted every
/// time.
/// </para>
/// <para>
/// So the rule for this file: no pointers, whatever it costs in neatness.
/// Breaking it stops DiscWright writing images at all on a clean Windows 11.
/// </para>
/// </remarks>
internal static class Copy
{
    internal static void StreamToFile(
        string path,
        object comStream,
        int blockSize,
        int totalBlocks,
        int reportEvery,
        Action<long, long> say,
        CancellationToken ct)
    {
        if (comStream is not IStream reading)
        {
            throw new InvalidOperationException("The image did not come with a stream to read.");
        }

        byte[] block = new byte[blockSize];

        // IStream.Read wants somewhere to put the number of bytes it read.
        // Four unmanaged bytes, rather than a pointer to a local.
        IntPtr howMany = Marshal.AllocHGlobal(sizeof(int));

        try
        {
            using System.IO.FileStream writing = System.IO.File.OpenWrite(path);

            for (long done = 0; done < totalBlocks; done++)
            {
                ct.ThrowIfCancellationRequested();

                reading.Read(block, blockSize, howMany);
                writing.Write(block, 0, Marshal.ReadInt32(howMany));

                if (reportEvery > 0 && (done + 1) % reportEvery == 0)
                {
                    say(done + 1, totalBlocks);
                }
            }

            writing.Flush();
        }
        finally
        {
            Marshal.FreeHGlobal(howMany);
        }

        // Said once at the end whatever the interval worked out to, so the last
        // thing anybody sees is the whole of it rather than 199 parts in 200.
        say(totalBlocks, totalBlocks);
    }
}
