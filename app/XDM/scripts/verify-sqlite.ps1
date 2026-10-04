param([string]$RuntimeDirectory,[ValidateSet('x86','x64')][string]$Architecture,[string]$WorkDirectory,[string]$Version)
$ErrorActionPreference='Stop'
if([Environment]::Is64BitProcess -ne ($Architecture -eq 'x64')){throw 'SQLite test host architecture mismatch'}
foreach($exe in @('xdm-app.exe','xdm-guide.exe')) {
    $name=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $RuntimeDirectory $exe))
    $expected=if($Architecture -eq 'x64'){'Amd64'}else{'X86'}
    if($name.ProcessorArchitecture.ToString() -ne $expected){throw "$exe architecture mismatch: $($name.ProcessorArchitecture)"}
    if($name.Version.ToString(3) -ne $Version){throw "$exe version mismatch: $($name.Version)"}
}
New-Item -ItemType Directory -Path $WorkDirectory -Force | Out-Null
$dll=Join-Path $RuntimeDirectory 'System.Data.SQLite.dll'
[Reflection.Assembly]::LoadFrom($dll) | Out-Null
$db=Join-Path $WorkDirectory 'smoke.db'
$connection=New-Object System.Data.SQLite.SQLiteConnection("Data Source=$db;Version=3;")
try {
    $connection.Open()
    $command=$connection.CreateCommand()
    $command.CommandText='CREATE TABLE IF NOT EXISTS probe(id INTEGER PRIMARY KEY, value TEXT); DELETE FROM probe; INSERT INTO probe(value) VALUES (''incomplete''),(''finished'');'
    $command.ExecuteNonQuery() | Out-Null
    $command.CommandText='SELECT COUNT(*) FROM probe'
    if($command.ExecuteScalar() -ne 2){throw 'SQLite read/write failed'}
} finally {$connection.Dispose()}
Write-Output "SQLite $Architecture native library read/write passed."
