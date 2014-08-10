param(
    [string]$target = "Build",
    [string]$verbosity = "minimal",
    [int]$maxCpuCount = 0
)

$msbuilds = @(get-command msbuild -ea SilentlyContinue)
if ($msbuilds.Count -eq 0) {
    throw "MSBuild could not be found in the path. Please ensure MSBuild v12 (from Visual Studio 2013) is in the path."
}

$msbuild = $msbuilds[0].Definition

if ($maxCpuCount -lt 1) {
    $maxCpuCountText = $Env:MSBuildProcessorCount
} else {
    $maxCpuCountText = ":$maxCpuCount"
}

$allArgs = @("visualstudio.xunit.proj", "/m$maxCpuCountText", "/nologo", "/verbosity:$verbosity", "/t:$target", $args)
& $msbuild $allArgs
