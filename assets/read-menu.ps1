param([int]$X,[int]$Y,[int]$Width,[int]$Height,[switch]$SelfTest)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Runtime.WindowsRuntime
Add-Type 'using System.Runtime.InteropServices; public static class MenuDpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }'
$null=[MenuDpi]::SetProcessDPIAware()
$null=[Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime]
$null=[Windows.Globalization.Language,Windows.Foundation,ContentType=WindowsRuntime]
$null=[Windows.Storage.Streams.InMemoryRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
$null=[Windows.Storage.Streams.DataWriter,Windows.Storage.Streams,ContentType=WindowsRuntime]
$null=[Windows.Graphics.Imaging.BitmapDecoder,Windows.Graphics.Imaging,ContentType=WindowsRuntime]
$null=[Windows.Graphics.Imaging.SoftwareBitmap,Windows.Graphics.Imaging,ContentType=WindowsRuntime]
$null=[Windows.Media.Ocr.OcrResult,Windows.Foundation,ContentType=WindowsRuntime]
$asTask=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetGenericArguments().Count -eq 1 -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
function Await-Result($Operation,$ResultType) { $task=$asTask.MakeGenericMethod($ResultType).Invoke($null,@($Operation)); $task.GetAwaiter().GetResult() }
$engine=[Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage([Windows.Globalization.Language]::new('zh-Hans'))
if ($null -eq $engine) { $engine=[Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages() }
if ($null -eq $engine) { throw 'Windows OCR language component is unavailable.' }
if ($SelfTest) { $Width=800; $Height=200 }
if ($Width -lt 1 -or $Height -lt 1) { throw 'Invalid client rectangle.' }
$scale=[Math]::Min(1.0,[Windows.Media.Ocr.OcrEngine]::MaxImageDimension/[double][Math]::Max($Width,$Height))
$capture=[Drawing.Bitmap]::new($Width,$Height)
$graphics=[Drawing.Graphics]::FromImage($capture)
$memory=[IO.MemoryStream]::new()
$stream=[Windows.Storage.Streams.InMemoryRandomAccessStream]::new()
$writer=[Windows.Storage.Streams.DataWriter]::new($stream)
$bitmap=$null;$scaled=$null
try {
    if ($SelfTest) {
        $graphics.Clear([Drawing.Color]::Black)
        $font=[Drawing.Font]::new('Microsoft YaHei',32)
        try { $graphics.DrawString(([string][char]0x8FDB+[char]0x5165+[char]0x9B54+[char]0x517D+[char]0x4E16+[char]0x754C),$font,[Drawing.Brushes]::White,40,50) } finally { $font.Dispose() }
    } else { $graphics.CopyFromScreen($X,$Y,0,0,[Drawing.Size]::new($Width,$Height)) }
    $graphics.Dispose();$graphics=$null
    $scaled=[Drawing.Bitmap]::new($capture,[Drawing.Size]::new([int]($Width*$scale),[int]($Height*$scale)))
    $scaled.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
    $writer.WriteBytes($memory.ToArray());$null=Await-Result $writer.StoreAsync() ([uint32]);$stream.Seek(0)
    $decoder=Await-Result ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $bitmap=Await-Result ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
    $result=Await-Result ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
    $lines=@(foreach ($line in $result.Lines) {
        $left=[double]::PositiveInfinity;$top=$left;$right=0.0;$bottom=0.0
        foreach ($word in $line.Words) { $r=$word.BoundingRect;$left=[Math]::Min($left,$r.X);$top=[Math]::Min($top,$r.Y);$right=[Math]::Max($right,$r.X+$r.Width);$bottom=[Math]::Max($bottom,$r.Y+$r.Height) }
        if ($line.Words.Count -gt 0) { @{Text=$line.Text;X=[int]($left/$scale);Y=[int]($top/$scale);Width=[int](($right-$left)/$scale);Height=[int](($bottom-$top)/$scale)} }
    })
    ConvertTo-Json -InputObject $lines -Compress
} finally { if($graphics){$graphics.Dispose()};if($bitmap){$bitmap.Dispose()};if($scaled){$scaled.Dispose()};$writer.Dispose();$stream.Dispose();$memory.Dispose();$capture.Dispose() }
