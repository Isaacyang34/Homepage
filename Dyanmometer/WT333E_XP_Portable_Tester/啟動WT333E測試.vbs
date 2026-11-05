Set WshShell = CreateObject("WScript.Shell")
strCurDir = CreateObject("Scripting.FileSystemObject").GetParentFolderName(WScript.ScriptFullName)
WshShell.CurrentDirectory = strCurDir
WshShell.Run Chr(34) & strCurDir & "\WT333E_Tester_GUI.exe" & Chr(34), 1, False
