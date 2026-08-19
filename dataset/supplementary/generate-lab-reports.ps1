# Generates the three supplementary lab-report PNGs for the Lab Trends demo.
# Images are gitignored. Run from the repository root:
#   powershell -File dataset/supplementary/generate-lab-reports.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir = $PSScriptRoot
$fontFamily = 'Segoe UI'
$titleFont = New-Object System.Drawing.Font($fontFamily, 22, [System.Drawing.FontStyle]::Bold)
$subFont = New-Object System.Drawing.Font($fontFamily, 11, [System.Drawing.FontStyle]::Regular)
$warnFont = New-Object System.Drawing.Font($fontFamily, 10, [System.Drawing.FontStyle]::Bold)
$bodyFont = New-Object System.Drawing.Font($fontFamily, 13, [System.Drawing.FontStyle]::Regular)
$headerFont = New-Object System.Drawing.Font($fontFamily, 12, [System.Drawing.FontStyle]::Bold)
$black = [System.Drawing.Brushes]::Black
$slate = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(71, 85, 105))
$amber = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(146, 64, 14))
$line = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(203, 213, 225), 1)

$reports = @(
    @{
        File = 'demo_labs_2022-03-15.png'
        Date = '15 March 2022'
        Age  = '58'
        Rows = @(
            @{ Name = 'HbA1c'; Result = '6.4'; Unit = '%'; Range = '4.0 - 5.6' }
            @{ Name = 'Creatinine'; Result = '0.9'; Unit = 'mg/dL'; Range = '0.6 - 1.2' }
            @{ Name = 'ALT (SGPT)'; Result = '32'; Unit = 'U/L'; Range = '7 - 56' }
        )
    }
    @{
        File = 'demo_labs_2023-04-10.png'
        Date = '10 April 2023'
        Age  = '59'
        Rows = @(
            @{ Name = 'HbA1c'; Result = '7.1'; Unit = '%'; Range = '4.0 - 5.6' }
            @{ Name = 'Creatinine'; Result = '1.1'; Unit = 'mg/dL'; Range = '0.6 - 1.2' }
            @{ Name = 'ALT (SGPT)'; Result = '48'; Unit = 'U/L'; Range = '7 - 56' }
        )
    }
    @{
        File = 'demo_labs_2024-06-02.png'
        Date = '2 June 2024'
        Age  = '60'
        Rows = @(
            @{ Name = 'HbA1c'; Result = '8.2'; Unit = '%'; Range = '4.0 - 5.6' }
            @{ Name = 'Creatinine'; Result = '1.4'; Unit = 'mg/dL'; Range = '0.6 - 1.2' }
            @{ Name = 'ALT (SGPT)'; Result = '55'; Unit = 'U/L'; Range = '7 - 56' }
        )
    }
)

foreach ($report in $reports) {
    $bmp = New-Object System.Drawing.Bitmap 900, 640
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::White)
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    $g.DrawString('JAFFNA DIAGNOSTIC LABORATORY', $titleFont, $black, 40, 36)
    $g.DrawString('Biochemistry report', $subFont, $slate, 40, 76)
    $g.DrawString('SUPPLEMENTARY DEMO - not part of the official judge dataset', $warnFont, $amber, 40, 104)

    $g.DrawLine($line, 40, 132, 860, 132)

    $g.DrawString("Patient: Demo labs - supplementary    Age: $($report.Age) Y    Sex: M", $bodyFont, $black, 40, 152)
    $g.DrawString("Collected: $($report.Date)    Reported: $($report.Date)", $bodyFont, $black, 40, 180)
    $g.DrawString('Referring doctor: Dr. N. Rajan', $bodyFont, $black, 40, 208)

    $g.DrawLine($line, 40, 244, 860, 244)
    $g.DrawString('Test', $headerFont, $black, 40, 260)
    $g.DrawString('Result', $headerFont, $black, 360, 260)
    $g.DrawString('Unit', $headerFont, $black, 520, 260)
    $g.DrawString('Reference', $headerFont, $black, 640, 260)
    $g.DrawLine($line, 40, 288, 860, 288)

    $y = 310
    foreach ($row in $report.Rows) {
        $g.DrawString($row.Name, $bodyFont, $black, 40, $y)
        $g.DrawString($row.Result, $headerFont, $black, 360, $y)
        $g.DrawString($row.Unit, $bodyFont, $black, 520, $y)
        $g.DrawString($row.Range, $bodyFont, $slate, 640, $y)
        $y += 42
    }

    $g.DrawLine($line, 40, 470, 860, 470)
    $g.DrawString('Method: HPLC (HbA1c), Jaffe (creatinine), IFCC (ALT).', $subFont, $slate, 40, 492)
    $g.DrawString('This printed report is a labelled supplementary fixture for MediTrail Round 2.', $subFont, $slate, 40, 516)
    $g.DrawString('End of report', $subFont, $slate, 40, 560)

    $path = Join-Path $outDir $report.File
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
    Write-Host "Wrote $path"
}

$titleFont.Dispose()
$subFont.Dispose()
$warnFont.Dispose()
$bodyFont.Dispose()
$headerFont.Dispose()
$slate.Dispose()
$amber.Dispose()
$line.Dispose()
