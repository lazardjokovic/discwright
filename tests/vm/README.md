# Running the suites on a virtual machine

The window suite drives the real window: it moves the pointer, takes the
foreground and needs the desktop to itself for about six minutes. On the machine
you are working at, that is six minutes you cannot use the computer, and a
single stray click fails tests for reasons that have nothing to do with the
code.

A Hyper-V virtual machine has its own mouse, keyboard and display. The suite
drives that desktop instead, so it can run while you work, and nothing competes
with it for the foreground.

What this directory builds is a Windows machine that can run the suites
unattended. It is not required: the suites run perfectly well on the machine you
are sitting at, and some things can only run there.

## What cannot move to a VM

- **Burning.** Hyper-V cannot pass a DVD writer through to a guest.
- **The demo films.** They show real timings on real hardware, and a VM would
  quietly make the app look slower than it is.
- **Anything about one particular machine**, Smart App Control being the obvious
  case: a VM answers "what happens on a normal Windows", not "what happens on
  mine".
- **Tests that need real GOG downloads.** The suite builds a context per folder
  it finds, so a machine with fewer downloads discovers fewer tests. Check the
  counts rather than assuming a green run means the same thing in both places.

## Building one

```powershell
.\tests\vm\New-TestVM.ps1 -IsoPath F:\ISO\Win11-Enterprise-Eval.iso -VMPath F:\VMs
```

It asks for a password for the VM's local account rather than carrying one, and
writes an unattended answer file from `autounattend.xml.template` so Windows
installs without anyone clicking through Setup.

Windows 11 Enterprise Evaluation is free, licensed for testing, and a fresh
image every 90 days is a feature rather than a chore.

## The part that is easy to get wrong

**`ForegroundLockTimeout`.** A fresh Windows sets it to 200 seconds so that
background programs cannot pull another program's window to the front. The
window suite does exactly that, by design. Until it is zero, every test dies at
`Start-DiscWright` reporting that something else owns the foreground, with
nobody at the machine. `Harden-TestVM.ps1` sets it, along with turning off the
popups that otherwise steal focus mid-run.

**Session 0 has no desktop.** `Invoke-Command -VMName` lands there, which is
fine for the logic suite and useless for the window suite, where UI Automation
can see nothing. GUI work has to be pushed into the logged-on session, which is
what the scheduled task the build registers is for.

**Prove the foreground can be taken, not merely that a window can be seen.**
Those are different privileges and only the second one matters. A probe that
creates its own window and activates it proves nothing: the suite activates a
window belonging to another process, which Windows governs far more strictly.
