' Csendes indítás a botnak: nincs látható ablak, a kimenet a bot.log
' fájlba kerül hibakereséshez. A Windows Indítópultja (Startup mappa)
' futtatja ezt bejelentkezéskor — lásd README.md "Helyi futtatás" szakaszát.
Set objShell = CreateObject("WScript.Shell")
scriptDir = CreateObject("Scripting.FileSystemObject").GetParentFolderName(WScript.ScriptFullName)
objShell.CurrentDirectory = scriptDir
objShell.Run "cmd /c npm start >> bot.log 2>&1", 0, False
