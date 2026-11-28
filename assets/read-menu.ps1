param([int]$X,[int]$Y,[int]$Width,[int]$Height,[switch]$SelfTest,[string]$ImagePath)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Runtime.WindowsRuntime
Add-Type 'using System.Runtime.InteropServices; public static class MenuDpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }'
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System.Drawing;
using System.Drawing.Imaging;
public static class MenuContrast {
    public static Bitmap Prepare(Bitmap source,int width,int height) {
        var result=new Bitmap(width,height);
        using(var graphics=Graphics.FromImage(result)) using(var attributes=new ImageAttributes()) {
            graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            attributes.SetColorMatrix(new ColorMatrix(new float[][] {
                new float[]{-.4485f,-.4485f,-.4485f,0,0},new float[]{-.8805f,-.8805f,-.8805f,0,0},new float[]{-.171f,-.171f,-.171f,0,0},
                new float[]{0,0,0,1,0},new float[]{1.2f,1.2f,1.2f,0,1}}));
            graphics.DrawImage(source,new Rectangle(0,0,width,height),0,0,source.Width,source.Height,GraphicsUnit.Pixel,attributes);
        }
        return result;
    }
}
'@
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
if ($ImagePath) { $source=[Drawing.Image]::FromFile($ImagePath); try {$Width=$source.Width;$Height=$source.Height;$capture=[Drawing.Bitmap]::new($source)} finally {$source.Dispose()} }
if ($Width -lt 1 -or $Height -lt 1) { throw 'Invalid client rectangle.' }
$scale=[Math]::Min(2.0,[Windows.Media.Ocr.OcrEngine]::MaxImageDimension/[double][Math]::Max($Width,$Height))
if (-not $ImagePath) { $capture=[Drawing.Bitmap]::new($Width,$Height) }
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
    } elseif (-not $ImagePath) { $graphics.CopyFromScreen($X,$Y,0,0,[Drawing.Size]::new($Width,$Height)) }
    $graphics.Dispose();$graphics=$null
    $scaled=[MenuContrast]::Prepare($capture,[int]($Width*$scale),[int]($Height*$scale))
    $scaled.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
    $writer.WriteBytes($memory.ToArray());$null=Await-Result $writer.StoreAsync() ([uint32]);$stream.Seek(0)
    $decoder=Await-Result ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $bitmap=Await-Result ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
    $result=Await-Result ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
    $lines=@(foreach ($line in $result.Lines) {
        $left=[double]::PositiveInfinity;$top=$left;$right=0.0;$bottom=0.0
        foreach ($word in $line.Words) { $r=$word.BoundingRect;$left=[Math]::Min($left,$r.X);$top=[Math]::Min($top,$r.Y);$right=[Math]::Max($right,$r.X+$r.Width);$bottom=[Math]::Max($bottom,$r.Y+$r.Height) }
        if ($line.Words.Count -gt 0) { @{Text=$line.Text;X=[int]($left/$scale);Y=[int]($top/$scale);Width=[int](($right-$left)/$scale);Height=[int](($bottom-$top)/$scale)} }
        # OCR may merge adjacent buttons into one line. Preserve the actual word rectangles
        # for Ignore only; never estimate a position from a combined Disable/Ignore line.
        $ignoreLabels=@(([string][char]0x5FFD+[char]0x7565),'ignore')
        $lineText=($line.Text -replace '\s','').ToLowerInvariant()
        $wordRecords=@(foreach($entry in $line.Words){$entry})
        for($i=0;$i -lt $wordRecords.Length;$i++) {
            $joined='';$wordLeft=[double]::PositiveInfinity;$wordTop=$wordLeft;$wordRight=0.0;$wordBottom=0.0
            for($j=$i;$j -lt [Math]::Min($wordRecords.Length,$i+2);$j++) {
                $word=$wordRecords[$j];$joined+=($word.Text -replace '\s','').ToLowerInvariant();$r=$word.BoundingRect
                $wordLeft=[Math]::Min($wordLeft,$r.X);$wordTop=[Math]::Min($wordTop,$r.Y);$wordRight=[Math]::Max($wordRight,$r.X+$r.Width);$wordBottom=[Math]::Max($wordBottom,$r.Y+$r.Height)
                if($ignoreLabels -contains $joined -and $lineText -ne $joined) { @{Text=$joined;X=[int]($wordLeft/$scale);Y=[int]($wordTop/$scale);Width=[int](($wordRight-$wordLeft)/$scale);Height=[int](($wordBottom-$wordTop)/$scale)};break }
            }
        }
    })
    ConvertTo-Json -InputObject $lines -Compress
} finally { if($graphics){$graphics.Dispose()};if($bitmap){$bitmap.Dispose()};if($scaled){$scaled.Dispose()};$writer.Dispose();$stream.Dispose();$memory.Dispose();$capture.Dispose() }
