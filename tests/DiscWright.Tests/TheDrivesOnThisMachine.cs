using DiscWright.Burning;
using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// Asking the real drives on whatever machine this runs on.
/// </summary>
/// <remarks>
/// <para>
/// These pass on a machine with two burners and on a machine with none, because
/// both are ordinary. What they check is that the answer makes sense, not that
/// any particular drive is there: the test VM has no optical drive at all and
/// the machine this was written on has two.
/// </para>
/// <para>
/// Nothing here writes, erases or ejects anything. Asking costs nothing.
/// </para>
/// </remarks>
public class TheDrivesOnThisMachine(Xunit.Abstractions.ITestOutputHelper says)
{
    [Fact]
    public void Says_what_it_found_here()
    {
        IReadOnlyList<Recorder> drives = new WindowsRecorders().All();

        // Written down rather than asserted, because none is as ordinary as
        // two. It is here so that a run which quietly found nothing cannot be
        // mistaken for a run that checked something: every test below loops
        // over this list, and a loop over nothing passes.
        says.WriteLine($"{drives.Count} drive(s) on this machine:");

        foreach (Recorder drive in drives)
        {
            says.WriteLine(
                $"  {drive.Drive} {drive.Name}: {drive.Holding}, blank={drive.Blank}, "
                    + $"ready={drive.Ready}, erasing would help={drive.ErasingWouldHelp}, "
                    + $"free={drive.FreeSectors} sectors, speeds=["
                    + string.Join(", ", drive.Speeds.Select(each => WriteSpeeds.InWords(each, drive.MediaType)))
                    + "]" + (drive.WhyNot is null ? string.Empty : $", said: {drive.WhyNot}"));
        }
    }

    [Fact]
    public void Can_be_asked_without_anything_going_wrong()
    {
        // The one thing that must never happen: a machine with no writer, or a
        // drive with an empty tray, taking the program down on the way to
        // telling somebody their disc is ready.
        IReadOnlyList<Recorder> drives = new WindowsRecorders().All();

        Assert.NotNull(drives);
    }

    [Fact]
    public void Says_something_sensible_about_every_drive_it_finds()
    {
        foreach (Recorder drive in new WindowsRecorders().All())
        {
            Assert.NotEmpty(drive.Id);
            Assert.NotEmpty(drive.Name);
            Assert.NotEmpty(drive.Holding);

            // Either it answered, or it said why not. Never both and never
            // neither.
            if (drive.WhyNot is null)
            {
                Assert.True(drive.MediaType >= 0);
            }
            else
            {
                Assert.NotEmpty(drive.WhyNot);
                Assert.False(drive.Ready);
            }
        }
    }

    [Fact]
    public void Only_calls_a_drive_ready_when_a_disc_could_really_go_on_it()
    {
        foreach (Recorder drive in new WindowsRecorders().All().Where(each => each.Ready))
        {
            Assert.True(drive.Blank);
            Assert.True(OpticalMedia.CanBeWritten(drive.MediaType));
            Assert.True(drive.FreeSectors > 0);
        }
    }

    [Fact]
    public void Reports_speeds_a_disc_is_actually_labelled_with()
    {
        foreach (Recorder drive in new WindowsRecorders().All())
        {
            foreach (int sectors in drive.Speeds)
            {
                double multiple = WriteSpeeds.AsAMultiple(sectors, drive.MediaType);

                // Nothing is made at a hundredth or at five hundred times. A
                // figure outside this means the base for that kind of disc is
                // wrong, which halves or doubles every speed in the program.
                Assert.InRange(multiple, 0.5, 100);
            }
        }
    }

    [Fact]
    public void Chooses_a_speed_the_drive_actually_offered()
    {
        foreach (Recorder drive in new WindowsRecorders().All().Where(each => each.Speeds.Count > 0))
        {
            int? chosen = WriteSpeeds.Choose(drive.Speeds, drive.MediaType);

            Assert.NotNull(chosen);
            Assert.Contains(chosen.Value, drive.Speeds);
        }
    }
}
