@echo off
"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" AqT_Utl.sln -p:Configuration=Release -p:SignManifests=false
pause
