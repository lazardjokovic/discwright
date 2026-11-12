using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

using DiscWright.Menu;

namespace DiscWright.Tests;

/// <summary>
/// Takes the data block C# generated and runs it through the real script host.
/// </summary>
/// <remarks>
/// <para>
/// This is the join between the two halves of the menu's testing. Every other
/// test here asserts that the generated text <em>looks</em> right. These assert
/// that JScript <em>agrees</em>: that the literal parses, and that a name with
/// an ampersand, a trademark sign, a quote or a backslash in it comes back out
/// character for character, on the other side of three sets of escaping rules.
/// </para>
/// <para>
/// A name that does not survive this is a name that reaches somebody as
/// mojibake on a disc that is already burned.
/// </para>
/// </remarks>
public sealed class TheGeneratedDataBlock : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "dwdata_" + Guid.NewGuid().ToString("N")[..8]);

    public TheGeneratedDataBlock() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData("Hollow Knight")]
    [InlineData("Rock & Roll")]
    [InlineData("STAR WARS™: Empire at War")]
    [InlineData("A name with \"quotes\" in it")]
    [InlineData("A name with a \\ backslash")]
    [InlineData("100% complete (v2)")]
    [InlineData("Nasty </script><h1>take over")]
    [InlineData("Данные игры")]
    [InlineData("ゲームデータ")]
    public void Gives_JScript_back_exactly_the_name_it_was_given(string name)
    {
        Assert.Equal(name, WhatJScriptReads(name));
    }

    [Theory]
    [InlineData("STAR WARS\u2122: Empire at War")]
    [InlineData("Pok\u00E9mon Caf\u00E9")]
    [InlineData("\u0110or\u0111e i \u010Ca\u010Dak")]
    [InlineData("\u0414\u0430\u043D\u043D\u044B\u0435 \u0438\u0433\u0440\u044B")]
    [InlineData("\u30B2\u30FC\u30E0")]
    [InlineData("A face \U0001F600 outside the first plane")]
    public void Gives_JScript_back_the_name_when_the_file_is_read_in_the_ANSI_codepage(string name)
    {
        // How mshta reads the menu: a file of UTF-8 with no byte order mark
        // and no charset, taken a byte at a time in the machine's codepage.
        // The owner's laptop showed "Starting" followed by three characters
        // of mojibake that way, on a disc burned the same day, and the test
        // above could not see it, because it hands cscript a byte order mark.
        Assert.Equal(name, Run(DataBlockFor(name), "DISC.game", theWayTheMenuIsRead: true));
    }

    [Fact]
    public void Gives_JScript_a_set_it_can_count()
    {
        // How many discs, and which this one is. Which discs have been
        // copied is not baked in any more: the menu reads that off the folder
        // the set is being put back into, because one menu drives the whole
        // set and the disc under it gets swapped.
        string answer = Run(DataBlockFor("Hollow Knight"), "DISC.discsInSet + '/' + DISC.discNumber + '/' + typeof DISC.discsCopied");

        Assert.Equal("1/1/undefined", answer);
    }

    [Fact]
    public void Gives_JScript_the_buttons_as_a_list()
    {
        string answer = Run(DataBlockFor("Hollow Knight"), "DISC.buttons.join('|')");

        Assert.Equal("Install|Exit", answer);
    }

    // ------------------------------------------------------------------ helpers

    private string WhatJScriptReads(string name) => Run(DataBlockFor(name), "DISC.game");

    /// <summary>
    /// Pulls the generated block back out of the built menu, so what is tested
    /// is what the disc would carry rather than something built again here.
    /// </summary>
    private static string DataBlockFor(string name)
    {
        string page = MenuWriter.MenuPage(
            new MenuFacts(name, "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], true));

        Match found = Regex.Match(page, @"var DISC = (\{.*?\n\});", RegexOptions.Singleline);
        Assert.True(found.Success, "No data block found in the generated menu.");

        return found.Groups[1].Value;
    }

    private string Run(string dataBlock, string expression, bool theWayTheMenuIsRead = false)
    {
        string job = Path.Combine(_folder, "ask.wsf");
        string answer = Path.Combine(_folder, "answer.txt");

        // The answer goes to a FILE, as UTF-16, rather than to the console.
        //
        // WScript.Echo writes in the console's codepage, so a Cyrillic or
        // Japanese name comes back as question marks, which would look exactly
        // like an escaping bug in the code under test. cscript's //U switch is
        // supposed to fix that and does not: measured, it exits 1 and prints
        // nothing at all when the output is a pipe rather than a file.
        //
        // FileSystemObject writing UTF-16 has no codepage anywhere in it.
        string answerInJScript = answer.Replace("\\", "\\\\", StringComparison.Ordinal);

        // UTF-8 with a byte order mark, and the job says so, or cscript reads
        // the job itself in the machine's ANSI codepage and the name is lost on
        // the way in rather than on the way out.
        //
        // Unless the question is exactly that loss: then the bytes are the
        // menu's own, UTF-8 with no mark, and the job says Windows-1252,
        // which is how mshta reads a menu on a Western machine.
        File.WriteAllText(
            job,
            (theWayTheMenuIsRead
                ? "<?xml version=\"1.0\" encoding=\"windows-1252\"?>\n"
                : "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n")
            + "<job>\n"
            + "  <script language=\"JScript\">\n"
            + "    var DISC = " + dataBlock + ";\n"
            + "    var fso = new ActiveXObject(\"Scripting.FileSystemObject\");\n"
            + "    var out = fso.CreateTextFile(\"" + answerInJScript + "\", true, true);\n"
            + "    out.Write(" + expression + ");\n"
            + "    out.Close();\n"
            + "  </script>\n"
            + "</job>\n",
            new UTF8Encoding(!theWayTheMenuIsRead));

        ProcessStartInfo how = new()
        {
            FileName = "cscript.exe",
            WorkingDirectory = _folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        how.ArgumentList.Add("//nologo");
        how.ArgumentList.Add(job);

        using Process? running = Process.Start(how);
        Assert.NotNull(running);

        string output = running.StandardOutput.ReadToEnd();
        string errors = running.StandardError.ReadToEnd();
        running.WaitForExit(30_000);

        Assert.True(running.ExitCode == 0, $"cscript failed:\n{output}\n{errors}");
        Assert.True(File.Exists(answer), $"cscript wrote no answer:\n{output}\n{errors}");

        return File.ReadAllText(answer, Encoding.Unicode).Trim();
    }
}
