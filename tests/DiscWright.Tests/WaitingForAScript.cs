using System.Diagnostics;

namespace DiscWright.Tests;

/// <summary>
/// Waiting for a script host a test started, for so long and no longer.
/// </summary>
/// <remarks>
/// <para>
/// Both harnesses that run the menu's script under cscript read everything
/// it printed and only then waited, with a limit. Reading to the end of a
/// process that never ends never returns, so the limit was never reached:
/// a script in an endless loop held a test run for fifty minutes on
/// 2026-11-20, until the script host was stopped by hand. It was a fault
/// planted on purpose, a loop walking a page's parents with its guard taken
/// off, and the test that should have gone red in a second sat there.
/// </para>
/// <para>
/// What it prints is read while it runs, so a script that prints a great
/// deal cannot fill the pipe and stop itself, and the wait is the wait.
/// </para>
/// </remarks>
internal static class WaitingForAScript
{
    /// <summary>
    /// Everything the process printed, once it has ended. Stops it and fails
    /// the test when it is still running after the time allowed.
    /// </summary>
    public static string WhatItSaid(Process running, int milliseconds)
    {
        Task<string> said = running.StandardOutput.ReadToEndAsync();
        Task<string> complained = running.StandardError.ReadToEndAsync();

        if (!running.WaitForExit(milliseconds))
        {
            try
            {
                running.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It ended between the wait and the stopping.
            }

            Assert.Fail(
                $"The script host was still running after {milliseconds / 1000} seconds and was stopped. "
                + "A script that never returns is a menu that hangs.");
        }

        // The wait with a limit returns before the streams are drained.
        running.WaitForExit();

        return said.GetAwaiter().GetResult() + complained.GetAwaiter().GetResult();
    }
}
