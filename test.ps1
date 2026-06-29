# Navigate to working solution folder first
cd "C:\Veradigm\EdiFabric New Zip\edifabric.examples (10)\X12.NET\NET 6"

# Get already added projects in solution
$slnProjects = dotnet sln list

# Get all csproj files
$allProjects = Get-ChildItem -Recurse -Filter "*.csproj" | Select-Object -ExpandProperty FullName

# Add only projects not already in solution
foreach ($proj in $allProjects) {
    $projName = Split-Path $proj -Leaf
    if ($slnProjects -notmatch $projName) {
        Write-Host "Adding: $projName"
        dotnet sln add $proj
    } else {
        Write-Host "Skipping (already exists): $projName"
    }
}
