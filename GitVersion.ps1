param(
    [switch]$AssemblyVersionOnly,
    [switch]$FileVersionOnly,
    [switch]$NuGetVersionOnly,
    [switch]$InformationVersionOnly,
    [switch]$Verbose,
	[switch]$AsJson
)

function Get-GitCommitCount {
    try {
        return [int](git -C $RepoPath rev-list --count HEAD 2>$null)
    } catch { return 0 }
}

function Write-Log ($msg) {
    if ($Verbose){
		Write-Host $msg
	}
}

function Get-BaseReleaseBranchVersion($currentBranch, $commitId, $devVersion){
	if ("$($env:SYSTEM_PULLREQUEST_TARGETBRANCH)" -match 'release/(\d+\.\d+\.\d+)'){
		return $matches[1]
	}
    $branches = Get-ReleaseBranches | Where-Object { $_ -match 'release/\d+\.\d+\.\d+' }
	$devBranch = git -C $RepoPath branch -r --list "origin/develop" 2>$null
	if ($devBranch){
		$branches += $devBranch
	}
	$branchClearName = $currentBranch -replace "^origin/", ""
    $mergeBaseCommit = git log $currentBranch --pretty=format:%H -n 100
	$releaseCommit = @{}
    foreach ($branch in $branches) {
        $branchName = $branch.ToString().Trim()
        $baseCommit = git merge-base "$branchName" "$currentBranch"
		$releaseCommit[$baseCommit] = $branchName 
		Write-Log "$branchName : $baseCommit"
    }
	foreach ($commit in $mergeBaseCommit){
		if ($releaseCommit.ContainsKey($commit)) {
			Write-Log "Match commit $commit"
			if ($releaseCommit[$commit] -match "release/(\d+\.\d+\.\d+)") {
				return $matches[1]
			} else {
				return $devVersion
			}
		}
	}
    return $null
}

function Get-ReleaseBranches($remoteOnly){
	$releaseBranches = @()
    $remoteBranches = git -C $RepoPath branch -r --list "origin/release/*" 2>$null
    if (-not($remoteOnly)){
        $localBranches = git -C $RepoPath branch --list "release/*" 2>$null
    }
	if ($remoteBranches) { $releaseBranches += ($remoteBranches | ForEach-Object {$_.Trim()}) }
    if ($localBranches) { $releaseBranches += ($localBranches | ForEach-Object {$_.Trim()}) }
	return ($releaseBranches | Select-Object -Unique)
}

function Get-LatestReleaseVersion {
    $releaseBranches = Get-ReleaseBranches $true
    
    $latestVersion = "1.0.0"
    foreach ($branch in $releaseBranches) {
        if ($branch -match "release/(\d+\.\d+\.\d+)") {
            $version = $matches[1]
            $v1 = [version]$version
            $v2 = [version]$latestVersion
            if ($v1 -gt $v2) { $latestVersion = $version }
        }
    }
    return $latestVersion
}

function Get-VersionFromBranch {
    param($BranchData)
    
	$commitCount = Get-GitCommitCount
	$latestRelease = Get-LatestReleaseVersion
	$v = [version]$latestRelease
	$majorMinorPatch = "$($v.Major).$($v.Minor + 1).$($v.Build)"
	$branch = $BranchData.Branch
    if ($branch -match "release/(\d+\.\d+\.\d+)") {
        $version = $matches[1]
		$tag = "-release"
        $majorMinorPatch = "$version"
    } elseif ($branch -eq "develop" -or $branch -eq "origin/develop") {
		$tag = "-alpha"
    } else {
		# Для других веток
		$baseReleaseVersion = Get-BaseReleaseBranchVersion $branch $BranchData.CommitId $majorMinorPatch
		if ($baseReleaseVersion){
			$majorMinorPatch = $baseReleaseVersion
		}
		$branchName = $branch -replace "^origin/", "" -replace "[^a-zA-Z0-9]", "-"
		if ($branchName.Length -gt 15) { $branchName = $branchName.Substring(0, 15) }
		$tag = "-misc-$branchName"
	}
	$branchName = $branch -replace "^origin/", "" -replace "[^a-zA-Z0-9.]", "-"
    return [PSCustomObject]@{
		AssemblyVersion = "$majorMinorPatch$tag.$commitCount"
		NugetVersion = "$majorMinorPatch$tag$($commitCount.ToString('0000'))"
		FileVersion = "$majorMinorPatch.$commitCount"
		InformationVersion = "$majorMinorPatch$tag.$commitCount+Branch.$branchName.Sha.$($BranchData.CommitId)"
		Timestamp = ([DateTime]::Now.ToString("o"))
		CommitId = $BranchData.CommitId
		Revision = $commitCount
		BranchName = $BranchData.Branch
	}
}

function Get-GitBranchData {
	if ($env:SYSTEM_PULLREQUEST_SOURCEBRANCH -or $env:BUILD_SOURCEBRANCH){
		$commitSha = $env:BUILD_SOURCEVERSION
		if ($env:SYSTEM_PULLREQUEST_SOURCEBRANCH){
			$branch = $env:SYSTEM_PULLREQUEST_SOURCEBRANCH
		} else {
			$branch = $env:BUILD_SOURCEBRANCH
		}
		$branch = "origin/" + ($branch -replace 'refs/heads/', '')
	} else {
		$branch = (git -C $RepoPath rev-parse --abbrev-ref HEAD 2>$null).Trim()
		$commitSha = (git rev-parse HEAD 2>$null).Trim()
	}
    return [PSCustomObject]@{
		Branch = $branch
		CommitId = $commitSha
	}
}

function Get-VersionFromCache ($file, $branchData) {
	if (Test-Path $file){
		$version = Get-Content $file | ConvertFrom-Json
        $version.Timestamp = [DateTime]::Parse($version.Timestamp)
		if ($version.CommitId -eq $branchData.CommitId -and $version.BranchName -eq $branchData.Branch -and $version.Timestamp.AddMinutes(5) -gt [DateTime]::Now){
			return $version
		}
	}
	return $null
}

function Set-VersionToCache ($file, $version) {
    $text = $version | ConvertTo-Json
    [System.Io.File]::WriteAllText($file,$text)
}

$tmpFile = Join-Path $PSScriptRoot "GitVersionCache.json"

$RepoPath = "."
$branchData = Get-GitBranchData
$version = Get-VersionFromCache $tmpFile $branchData
if (-not($version)){
	$version = Get-VersionFromBranch -Branch $branchData
	Set-VersionToCache $tmpFile $version
}
if ($AssemblyVersionOnly) { Write-Output "$($version.AssemblyVersion)"}
if ($FileVersionOnly) { Write-Output "$($version.FileVersion)"}
if ($NuGetVersionOnly) { Write-Output "$($version.NugetVersion)"}
if ($InformationVersionOnly) { Write-Output "$($version.InformationVersion)"}
if (-not($NuGetVersionOnly) -and -not($FileVersionOnly) -and -not($AssemblyVersionOnly) -and -not($InformationVersionOnly) ) { 
	if ($AsJson){
		Write-Output $version | ConvertTo-Json -Depth 10 -Compress
	} else {
		Write-Output "$($version.AssemblyVersion) | $($version.NugetVersion) | $($version.FileVersion) | $($version.InformationVersion)"
	}
}