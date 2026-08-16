' DiscWright - silent launcher.
'
' Starts the WinForms script with its console window hidden, so no black box
' flashes before the app appears. wscript.exe and powershell.exe are both
' Microsoft-signed, so this whole chain runs under Smart App Control.

Option Explicit
Dim sh, fso, base, ps, cmd

Set sh  = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

base = fso.GetParentFolderName(WScript.ScriptFullName)
ps   = fso.BuildPath(base, "DiscWright.ps1")

If Not fso.FileExists(ps) Then
    MsgBox "Cannot find the app script:" & vbCrLf & vbCrLf & ps, 16, "DiscWright"
    WScript.Quit 1
End If

cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File """ & ps & """"

sh.CurrentDirectory = base
' 0 = hidden window, False = do not wait for it to exit
sh.Run cmd, 0, False
