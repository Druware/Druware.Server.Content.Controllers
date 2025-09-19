# Get the package name from the parent folder
$PKGNAME = Split-Path -Leaf (Get-Location)

# Lookup the API key (if needed)
$APIKEY = (reg query "HKEY_CURRENT_USER\SOFTWARE\com.trustwin.nuget" /v apikey 2>$null | ForEach-Object {
    $_ -match "apikey\s*REG_SZ\s*(.+)" | Out-Null; $matches[1]
}) -replace "`r", ''

# Get the version from the .nuspec file
$VERSION = Get-Content "$PKGNAME.nuspec" |
    Select-String -Pattern '<version>(.*)</version>' |
    ForEach-Object { $_.Matches.Groups[1].Value }

# Parse the version into major, minor, and revision numbers
$MAJOR, $MINOR, $REVISION = $VERSION -split '\.'

# Increment the version based on the provided argument
if ($args[0] -eq '--major') {
    $MAJOR = [int]$MAJOR + 1
    $MINOR = 0
    $REVISION = 0
} elseif ($args[0] -eq '--minor') {
    $MINOR = [int]$MINOR + 1
    $REVISION = 0
} else {
    $REVISION = [int]$REVISION + 1
}

# Update the version
$VERSION = "$MAJOR.$MINOR.$REVISION"

# Update the .nuspec file
(Get-Content "$PKGNAME.nuspec") -replace '(<version>)(.*?)(</version>)', "`$1$VERSION`$3" |
    Set-Content "$PKGNAME.nuspec"

# Build and push the package
dotnet build . --configuration RELEASE

dotnet pack -o "pub" --configuration Release

dotnet nuget push "pub/$PKGNAME.$VERSION.nupkg" --source "https://nuget.satori-assoc.com/v3/index.json" --api-key $APIKEY