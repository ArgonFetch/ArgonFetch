' Silent Starter for Python GUI Application

Option Explicit
Dim objShell, strPythonScriptPath, strPythonWExePath, strCommand, strScriptDir, strPythonScriptName, fso ' << ADDED fso HERE

' --- CONFIGURATION ---
strPythonScriptName = "Db-Script-GUI.py"
strPythonWExePath = "pythonw.exe" ' << TRY THIS FIRST. If it fails, use the full path.
' --- END CONFIGURATION ---


Set objShell = WScript.CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject") ' This line is now fine

' Get the directory where this VBScript is located
strScriptDir = fso.GetParentFolderName(WScript.ScriptFullName)

' Construct the full path to the Python script
strPythonScriptPath = fso.BuildPath(strScriptDir, strPythonScriptName)

' Check if the Python script exists
If Not fso.FileExists(strPythonScriptPath) Then
    MsgBox "Python script not found at: " & strPythonScriptPath & vbCrLf & "Please check the strPythonScriptName variable and the VBScript location.", vbCritical, "Error"
    WScript.Quit
End If

' Construct the command to run the Python script silently
' The quotes are important, especially if paths contain spaces.
strCommand = """" & strPythonWExePath & """ """ & strPythonScriptPath & """"

' Run the command:
' Parameter 1: Command to run
' Parameter 2: Window style (0 = Hidden)
' Parameter 3: Wait for command to complete (False = Don't wait)
objShell.Run strCommand, 0, False

Set objShell = Nothing
Set fso = Nothing
WScript.Quit