using System.Windows;
using System.Windows.Interop;

using DiscWright.Platform;

using Microsoft.Win32;

namespace DiscWright.App;

/// <summary>
/// Dresses the program the way the system is dressed, and keeps it dressed
/// when the system changes its mind.
/// </summary>
/// <remarks>
/// <para>
/// Look.xaml is the light look and the whole design. LookInTheDark.xaml
/// answers for the same brushes and nothing else, merged on top when Windows
/// says apps are dark and taken off when it stops saying so. That is the
/// entire mechanism, and it only works because every reference to a palette
/// brush is a DynamicResource: a StaticResource is resolved once, when the
/// markup loads, and a dictionary merged afterwards changes nothing it
/// already answered. A markup test holds that rule in place.
/// </para>
/// <para>
/// The title bar is Windows' own and no brush of ours reaches it, so it is
/// asked to go dark separately, through dwmapi: for every window as it
/// loads, dialogs included, which is what the class handler is for, and for
/// every window already open when the answer changes.
/// </para>
/// <para>
/// There is deliberately no setting of our own. The one honest answer to
/// "which theme" is the one somebody already gave Windows, and a program
/// that asks again is a program that can disagree with every other window
/// on the screen.
/// </para>
/// </remarks>
internal static class WearingTheTheme
{
    /// <summary>
    /// One instance, added and removed, so wearing the dark twice cannot
    /// stack two copies and taking it off always finds the one that went on.
    /// </summary>
    private static readonly ResourceDictionary InTheDark =
        new() { Source = new Uri("LookInTheDark.xaml", UriKind.Relative) };

    private static bool _dark;

    public static void FollowTheSystem(Application app)
    {
        Wear(app, WhatTheSystemWears.Dark());

        // Every window's title bar, painted as the window loads, so no
        // window has to know themes exist. Loaded rather than anything
        // earlier because the handle has to exist to be painted.
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window)
                {
                    PaintingTheTitleBar.OfWindow(new WindowInteropHelper(window).Handle, _dark);
                }
            }));

        // How Windows announces a theme change, among much else; asking
        // again on every General change is cheap and always right. Back on
        // the dispatcher because the announcement can arrive on any thread
        // and the dictionaries belong to the window's.
        SystemEvents.UserPreferenceChanged += (_, changed) =>
        {
            if (changed.Category == UserPreferenceCategory.General)
            {
                app.Dispatcher.BeginInvoke(() => Wear(app, WhatTheSystemWears.Dark()));
            }
        };
    }

    private static void Wear(Application app, bool dark)
    {
        _dark = dark;

        if (dark && !app.Resources.MergedDictionaries.Contains(InTheDark))
        {
            app.Resources.MergedDictionaries.Add(InTheDark);
        }

        if (!dark)
        {
            app.Resources.MergedDictionaries.Remove(InTheDark);
        }

        foreach (Window window in app.Windows)
        {
            PaintingTheTitleBar.OfWindow(new WindowInteropHelper(window).Handle, dark);
        }
    }
}
