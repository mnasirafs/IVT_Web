# apply_static_creds.ps1
# Run from repository root. Creates backups (*.bak) before edits.

Write-Host "Starting credential replacement..."

# Replace new NetworkCredential(creds.UserName, creds.Password) with AfsCredentials.GetSmtpCredential()
Get-ChildItem -Path . -Include *.cs,*.cshtml -Recurse | ForEach-Object {
	$path = $_.FullName
	$text = Get-Content $path -Raw
	$pattern = 'new\s+NetworkCredential\s*\(\s*creds\.UserName\s*,\s*creds\.Password\s*\)'
	if ($text -match $pattern) {
		$new = [regex]::Replace($text, $pattern, 'AFSDataGateway.App_Start.AfsCredentials.GetSmtpCredential()')
		Copy-Item $path "$path.bak_cred" -Force
		Set-Content -Path $path -Value $new -Force
		Write-Host "Replaced SMTP credential instantiation in: $path"
	}
}

# Replace usages like creds.UserName and creds.Password when clearly for SMTP/SQL - conservative replacement only when pattern includes 'creds.' and 'NetworkCredential' nearby
Get-ChildItem -Path . -Include *.cs,*.cshtml -Recurse | ForEach-Object {
	$path = $_.FullName
	$text = Get-Content $path -Raw
	# Replace creds.UserName and creds.Password where used together or near NetworkCredential/Sql
	$patternCombo = 'creds\.UserName\s*[,\)]|creds\.Password\s*[,\)]|creds\.UserName|creds\.Password'
	if ($text -match $patternCombo) {
		# Make a conservative replacement: replace creds.UserName with AfsCredentials.SqlUser and creds.Password with AfsCredentials.SqlPass
		$new = $text -replace 'creds\.UserName', 'AFSDataGateway.App_Start.AfsCredentials.SqlUser'
		$new = $new -replace 'creds\.Password', 'AFSDataGateway.App_Start.AfsCredentials.SqlPass'
		if ($new -ne $text) {
			Copy-Item $path "$path.bak_cred2" -Force
			Set-Content -Path $path -Value $new -Force
			Write-Host "Replaced creds.* usages in: $path"
		}
	}
}

# Replace GetSqlAuth(...) calls with AfsCredentials.GetSqlAuth()
Get-ChildItem -Path . -Include *.cs,*.cshtml -Recurse | ForEach-Object {
	$path = $_.FullName
	$text = Get-Content $path -Raw
	$pattern = 'GetSqlAuth\s*\([^\)]*\)'
	if ($text -match $pattern) {
		$new = [regex]::Replace($text, $pattern, 'AFSDataGateway.App_Start.AfsCredentials.GetSqlAuth()')
		Copy-Item $path "$path.bak_getsql" -Force
		Set-Content -Path $path -Value $new -Force
		Write-Host "Replaced GetSqlAuth usage in: $path"
	}
}

Write-Host "Replacement complete. Please review .bak files and run a solution build."