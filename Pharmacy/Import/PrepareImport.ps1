$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $PSScriptRoot 'import/import data.xlsx'))
function Read-Entry([string] $name) {
    $reader = [IO.StreamReader]::new($archive.GetEntry($name).Open())
    try { return [xml] $reader.ReadToEnd() }
    finally { $reader.Dispose() }
}
try {
    $shared = Read-Entry 'xl/sharedStrings.xml'
    $strings = @($shared.sst.si | ForEach-Object { $_.InnerText })
    $result = @{}
    foreach ($sheetNumber in 1..3) {
        $sheet = Read-Entry "xl/worksheets/sheet$sheetNumber.xml"
        $rows = @($sheet.worksheet.sheetData.row | ForEach-Object {
            $values = @{}
            foreach ($cell in $_.c) {
                $value = [string] $cell.v
                if ($cell.t -eq 's') { $value = $strings[[int] $value] }
                $values[($cell.r -replace '\d', '')] = $value
            }
            $values
        })
        $result[('users', 'products', 'orders')[$sheetNumber - 1]] = $rows
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 (Join-Path $PSScriptRoot 'seed.json')
}
finally { $archive.Dispose() }
