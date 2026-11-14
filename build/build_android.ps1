$version = (& $PSScriptRoot/../GitVersion.ps1 -AsJson) | ConvertFrom-Json
Push-Location $PSScriptRoot/../src
Write-host "dotnet publish -c Release -f net10.0-android -p:ApplicationDisplayVersion=$($version.FileVersion) -p:ApplicationVersion=$($version.Revision) -p:IsArm=true"
dotnet publish -c Release -f net10.0-android -p:ApplicationDisplayVersion=$($version.FileVersion) -p:ApplicationVersion=$($version.Revision) -p:IsArm=true
Pop-Location
Push-Location $PSScriptRoot/../src/bin/Release/net10.0-android/publish
if (Test-Path "./drop"){
    Remove-Item ./drop -Recurse -Force
}
New-Item -ItemType Directory "drop"
Copy-Item ./com.companyname.SunsetMP-Signed.apk -Destination ./drop/com.companyname.sunsetmp.$($version.FileVersion).apk
Pop-Location