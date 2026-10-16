param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference = 'Stop'
$expected = [IO.Path]::GetFullPath($Executable)
$scheduler = New-Object -ComObject Schedule.Service
$scheduler.Connect()
$folder = $scheduler.GetFolder('\')
$names = @()
foreach ($task in $folder.GetTasks(1)) {
    if ($task.Name.StartsWith('RelayDesk.Startup.',[StringComparison]::Ordinal)) {
        foreach ($action in $task.Definition.Actions) {
            if ($action.Type -eq 0 -and [string]::Equals($action.Path,$expected,[StringComparison]::OrdinalIgnoreCase)) { $names += $task.Name; break }
        }
    }
}
foreach ($name in $names) { $folder.DeleteTask($name,0) }
