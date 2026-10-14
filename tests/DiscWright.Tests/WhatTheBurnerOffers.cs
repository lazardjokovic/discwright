using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace DiscWright.Tests;

/// <summary>
/// What the Windows burning components say about themselves.
/// </summary>
/// <remarks>
/// The identifiers of COM interfaces are the one thing in this program that
/// cannot be worked out from first principles and must not be remembered
/// wrongly: a mistyped one fails at run time, on the machine of whoever is
/// holding the disc, with a message about an interface not being supported.
/// So they are asked for here and checked against what the code uses.
/// </remarks>
public class WhatTheBurnerOffers(Xunit.Abstractions.ITestOutputHelper says)
{
    [Fact]
    public void Tells_us_which_interface_it_reports_progress_through()
    {
        foreach (string progId in (string[])["IMAPI2.MsftDiscFormat2Data", "IMAPI2.MsftDiscFormat2Erase"])
        {
            Type? type = Type.GetTypeFromProgID(progId);

            if (type is null)
            {
                says.WriteLine($"{progId}: not on this machine");
                continue;
            }

            object com = Activator.CreateInstance(type)!;

            try
            {
                if (com is not IConnectionPointContainer points)
                {
                    says.WriteLine($"{progId}: offers no connection points at all");
                    continue;
                }

                points.EnumConnectionPoints(out IEnumConnectionPoints enumerating);

                IConnectionPoint[] one = new IConnectionPoint[1];

                // Next wants somewhere to put the count, and the signature
                // takes a raw address rather than an out parameter.
                IntPtr fetched = Marshal.AllocHGlobal(sizeof(int));

                says.WriteLine($"{progId} reports through:");

                while (true)
                {
                    enumerating.Next(1, one, fetched);

                    if (Marshal.ReadInt32(fetched) == 0)
                    {
                        break;
                    }

                    one[0].GetConnectionInterface(out Guid iid);
                    says.WriteLine($"  {iid:B}");
                    Marshal.ReleaseComObject(one[0]);
                }

                Marshal.FreeHGlobal(fetched);
            }
            finally
            {
                Marshal.FinalReleaseComObject(com);
            }
        }
    }

    [Fact]
    public void Tells_us_the_number_each_report_arrives_under()
    {
        // A dispatch interface is called by number, not by name. Guessing the
        // number does not fail loudly: the drive takes the listener, writes the
        // disc, and never calls anything, which looks exactly like a drive that
        // does not report. So the number is read out of Windows' own type
        // library rather than remembered.
        LoadTypeLib(@"C:\Windows\System32\imapi2.dll", out ITypeLib library);

        foreach ((string what, Guid iid) in (ValueTuple<string, Guid>[])
        [
            ("the write", new Guid("2735413c-7f64-5b0f-8f00-5d77afbe261e")),
            ("the erase", new Guid("2735413a-7f64-5b0f-8f00-5d77afbe261e"))
        ])
        {
            Guid looking = iid;
            library.GetTypeInfoOfGuid(ref looking, out ITypeInfo about);

            about.GetTypeAttr(out IntPtr raw);
            TYPEATTR attributes = Marshal.PtrToStructure<TYPEATTR>(raw);
            about.ReleaseTypeAttr(raw);

            says.WriteLine($"{what} reports through {attributes.cFuncs} method(s):");

            for (int at = 0; at < attributes.cFuncs; at++)
            {
                about.GetFuncDesc(at, out IntPtr describing);
                FUNCDESC description = Marshal.PtrToStructure<FUNCDESC>(describing);
                int number = description.memid;
                about.ReleaseFuncDesc(describing);

                about.GetDocumentation(number, out string name, out _, out _, out _);

                says.WriteLine($"  {name} arrives as {number} (0x{number:X})");

                // The guard, not the print. Code that listens on the wrong
                // number writes the whole disc and never hears a word.
                Assert.Equal(0x200, number);
            }
        }
    }

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void LoadTypeLib(string file, out ITypeLib library);

    [Fact]
    public void Tells_us_what_a_report_about_a_write_actually_carries()
    {
        // Asked rather than guessed, again. A property read under the wrong
        // name throws where nobody is looking, and the bar sits at nothing
        // while the drive is talking.
        LoadTypeLib(@"C:\Windows\System32\imapi2.dll", out ITypeLib library);

        library.GetTypeInfoCount();

        foreach (string wanted in (string[])["IDiscFormat2DataEventArgs", "IWriteEngine2EventArgs"])
        {
            for (int at = 0; at < library.GetTypeInfoCount(); at++)
            {
                library.GetDocumentation(at, out string name, out _, out _, out _);

                if (name != wanted)
                {
                    continue;
                }

                library.GetTypeInfo(at, out ITypeInfo about);
                about.GetTypeAttr(out IntPtr raw);
                TYPEATTR attributes = Marshal.PtrToStructure<TYPEATTR>(raw);
                about.ReleaseTypeAttr(raw);

                says.WriteLine($"{wanted} carries:");

                for (int which = 0; which < attributes.cFuncs; which++)
                {
                    about.GetFuncDesc(which, out IntPtr describing);
                    FUNCDESC description = Marshal.PtrToStructure<FUNCDESC>(describing);
                    int number = description.memid;
                    about.ReleaseFuncDesc(describing);

                    about.GetDocumentation(number, out string member, out _, out _, out _);
                    says.WriteLine($"  {member}");
                }
            }
        }
    }
}
