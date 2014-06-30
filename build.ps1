param(
    [string]$target = "Build",
    [string]$verbosity = "minimal",
    [int]$maxCpuCount = 0
)

$msbuilds = @(get-command msbuild -ea SilentlyContinue)
if ($msbuilds.Count -eq 0) {
    $msbuild = join-path $env:windir "Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
} else {
    $msbuild = $msbuilds[0].Definition
}

if ($maxCpuCount -lt 1) {
    $maxCpuCountText = $Env:MSBuildProcessorCount
} else {
    $maxCpuCountText = ":$maxCpuCount"
}

$allArgs = @("visualstudio.xunit.proj", "/m$maxCpuCountText", "/nologo", "/verbosity:$verbosity", "/t:$target", $args)
& $msbuild $allArgs
