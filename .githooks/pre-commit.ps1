# Fail on any error
$ErrorActionPreference = "Stop"

# staged added/changed files (text-ish)
$files = git diff --cached --name-only --diff-filter=ACM | ? { Test-Path $_ }

# patterns to block (add more if you like)
$patterns = @(
  'sk_(test|live)_[0-9A-Za-z]{10,}',                # Stripe secret keys
  'whsec_[0-9A-Za-z]{10,}',                         # Stripe webhook secrets
  '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----', # any PEM private key
  'aws_secret_access_key\s*=\s*[A-Za-z0-9\/=+]{30,}',
  'ghp_[0-9A-Za-z]{30,}'                            # GitHub PAT
)

# optional allowlist lines in this file
$allow = @()
if (Test-Path .secret-allowlist) { $allow = Get-Content .secret-allowlist }

$violations = @()
foreach ($f in $files) {
  # skip hook files and allowlist itself
  if ($f -like '.githooks/*' -or $f -eq '.secret-allowlist') { continue }
  # skip big/binary files
  if ((Get-Item $f).Length -gt 5MB) { continue }
  try {
    $hits = Select-String -Path $f -Pattern $patterns -AllMatches -Encoding utf8 -ErrorAction SilentlyContinue
    foreach ($h in $hits) {
      $line = $h.Line.Trim()
      if ($allow -notcontains $line) { $violations += "$($h.Path):$($h.LineNumber): $line" }
    }
  } catch { }
}

if ($violations.Count -gt 0) {
  Write-Host "`n❌ Secret-like strings detected. Commit blocked:" -ForegroundColor Red
  $violations | Sort-Object -Unique | ForEach-Object { Write-Host $_ }
  Write-Host "`nTip: add an exact line to .secret-allowlist to permit known test fixtures." -ForegroundColor Yellow
  exit 1
}

exit 0
