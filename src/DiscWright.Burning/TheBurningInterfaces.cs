using System.Runtime.InteropServices;

namespace DiscWright.Burning;

/// <summary>
/// The number both of Windows' burning event interfaces deliver their one
/// method under. Read from the type library, not remembered.
/// </summary>
internal static class TheNumbers
{
    internal const int ReportsArriveUnder = 0x200;
}

/// <summary>
/// The two interfaces Windows reports progress through while it writes or
/// erases a disc.
/// </summary>
/// <remarks>
/// <para>
/// <strong>These identifiers were asked for, not remembered.</strong> A
/// mistyped one fails at run time, on the machine of whoever is holding the
/// disc, with a message about an unsupported interface. <c>WhatTheBurnerOffers</c>
/// asks the components on this machine which interface they report through and
/// checks the answer against these, so a wrong one fails on the bench instead.
/// </para>
/// <para>
/// They are dispatch interfaces, which is how an event source written for
/// scripting hands things over, and why the method takes plain objects rather
/// than anything shaped.
/// </para>
/// <para>
/// <strong>They are declared, not imported.</strong> ComImport says "a COM
/// object implements this and we call it", and here it is the other way about:
/// we implement it and a COM object calls us. Marked as imported, the wrapper
/// around our sink does not offer the interface under its own identifier, the
/// drive takes the listener anyway, and nothing is ever called. That is a third
/// way to get a silent bar, and it also happened here.
/// </para>
/// <para>
/// <strong>A dispatch interface is called by number, and the number matters as
/// much as the identifier.</strong> Guessing it does not fail loudly: the drive
/// takes the listener, writes the whole disc, and never calls anything, which
/// looks exactly like a drive that does not report progress. That happened
/// here, on a real burn, with 0x100. Both of these are 0x200, read out of
/// Windows' own type library, and <c>WhatTheBurnerOffers</c> checks these
/// numbers against it.
/// </para>
/// </remarks>
[ComVisible(true)]
[Guid("2735413c-7f64-5b0f-8f00-5d77afbe261e")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
public interface IHearAboutTheWrite
{
    [DispId(TheNumbers.ReportsArriveUnder)]
    void Update(
        [MarshalAs(UnmanagedType.IDispatch)] object sender,
        [MarshalAs(UnmanagedType.IDispatch)] object progress);
}

[ComVisible(true)]
[Guid("2735413a-7f64-5b0f-8f00-5d77afbe261e")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
public interface IHearAboutTheErase
{
    [DispId(TheNumbers.ReportsArriveUnder)]
    void Update(
        [MarshalAs(UnmanagedType.IDispatch)] object sender,
        int elapsedSeconds,
        int estimatedTotalSeconds);
}

/// <summary>
/// The eight things a data write reports it is doing, in words.
/// </summary>
/// <remarks>
/// The values of IMAPI's write action. An unknown code is reported as itself: a
/// drive that invents a ninth phase must not make the window claim it is
/// writing.
/// </remarks>
internal static class WhatTheDriveIsDoing
{
    internal static string InWords(int action) => action switch
    {
        0 => "checking the disc",
        1 => "formatting the disc",
        2 => "getting the drive ready",
        3 => "setting the laser power",
        4 => "writing the disc",
        5 => "finishing the disc",
        6 => "flushing the drive's buffer",
        7 => "checking what was written",
        _ => $"doing something it calls {action}"
    };
}
