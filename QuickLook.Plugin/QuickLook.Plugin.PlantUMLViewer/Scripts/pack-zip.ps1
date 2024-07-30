Remove-Item ..\QuickLook.Plugin.PlantUMLViewer.qlplugin -ErrorAction SilentlyContinue

$files = Get-ChildItem -Path ..\bin\Release\ -Exclude *.pdb,*.xml
Compress-Archive $files ..\QuickLook.Plugin.PlantUMLViewer.zip
Move-Item ..\QuickLook.Plugin.PlantUMLViewer.zip ..\QuickLook.Plugin.PlantUMLViewer.qlplugin