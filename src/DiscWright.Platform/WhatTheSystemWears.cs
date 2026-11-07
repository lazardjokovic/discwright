using Microsoft.Win32;

namespace DiscWright.Platform;

/// <summary>
/// Whether Windows says apps are dark, read from where Settings writes it.
/// </summary>
/// <remarks>
/// <para>
/// <c>AppsUseLightTheme</c> under the Personalize key: 1 is light, 0 is dark,
/// and absent is a Windows old enough never to have heard the question, which
/// is light. The name is Microsoft's and it is inverted, which is exactly the
/// kind of fact that gets flipped in a hurry, so the rule about what a value
/// means is a method of its own that a test can hand every shape the registry
/// can produce.
/// </para>
/// <para>
/// Read on demand rather than remembered, because the one caller asks again
/// precisely when the answer may have changed.
/// </para>
/// </remarks>
public static class WhatTheSystemWears
{
    public static bool Dark() =>
        DarkFrom(Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme",
            1));

    /// <summary>
    /// What a value of that registry entry means. Only a DWORD zero means
    /// dark: a missing key, a missing value or a value of some other shape
    /// all mean light, because light is what every Windows wore before the
    /// question existed.
    /// </summary>
    public static bool DarkFrom(object? appsUseLightTheme) =>
        appsUseLightTheme is int light && light == 0;
}
