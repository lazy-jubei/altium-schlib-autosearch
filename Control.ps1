param([ValidateSet('Refresh','Disable','Status')][string]$Action='Status',[string]$AltiumExe='C:\Program Files\Altium\AD25\X2.EXE')
$ErrorActionPreference='Stop'
if(-not(Get-Process X2 -ErrorAction SilentlyContinue)){throw 'Open Altium before using this control.'}
if(-not(Test-Path -LiteralPath $AltiumExe)){throw 'Altium executable not found. Pass -AltiumExe for your installation.'}
# This targets the existing interactive Altium session; Status opens a small dialog.
Start-Process -FilePath $AltiumExe -ArgumentList ('-RSchLibSearchPaths:'+$Action) -WindowStyle Normal
"Sent $Action to Altium."
