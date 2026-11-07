using System.Windows;
using System.Windows.Interop;

using DiscWright.Platform;
using DiscWright.ViewModels;

using Microsoft.Win32;

namespace DiscWright.App;

/// <summary>
/// Dresses the program the way the system is dressed, or the way somebody
/// chose, and keeps it dressed when either changes its mind.
/// </summary>
/// <remarks>
/// <para>
/// Look.xaml is the light look and the whole design. LookInTheDark.xaml
/// answers for the same brushes and nothing else, merged on top when the
/// program should be dark and taken off when it should not. That is the
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
/// Which look to wear is <see cref="ChoosingTheLook"/>'s decision, where a
/// test can hold it. Following Windows was the only behaviour at first, on
/// the argument that a program asking "which theme" again can disagree with
/// every other window on the screen; the owner decided on 2026-11-07 that
/// the question may be answered for this program alone, because plenty of
/// people keep Windows light and want one program dark. Following Windows
/// is still the default, so nobody meets the question unasked.
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

    public static void Follow(Application app, ChoosingTheLook choice)
    {
        Wear(app, choice.WearsTheDark(WhatTheSystemWears.Dark()));

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
                app.Dispatcher.BeginInvoke(() => Wear(app, choice.WearsTheDark(WhatTheSystemWears.Dark())));
            }
        };

        // And the person changing their mind, through the quiet control on
        // the window, which lands here the same way the system's change does.
        choice.PropertyChanged += (_, _) =>
            app.Dispatcher.BeginInvoke(() => Wear(app, choice.WearsTheDark(WhatTheSystemWears.Dark())));
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
