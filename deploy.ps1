param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$downloadsChuvadi = Join-Path $env:USERPROFILE "Downloads\chuvadi"

if (-not (Test-Path $downloadsChuvadi)) {
    Write-Error "Source folder not found: $downloadsChuvadi"
    exit 1
}

$projectRoot = $PSScriptRoot

if (-not (Test-Path (Join-Path $projectRoot "Chuvadi.slnx"))) {
    Write-Error "Run this script from the repository root (where Chuvadi.slnx lives)."
    exit 1
}

$fileMap = @(
    @{ File = "CLAUDE.md";                          Dest = ".";                                    As = "CLAUDE.md" }
    @{ File = "README.md";                          Dest = ".";                                    As = "README.md" }
    @{ File = "PdfObjectId.cs";                     Dest = "src\Chuvadi.Pdf.Primitives";           As = "PdfObjectId.cs" }
    @{ File = "PdfPrimitive.cs";                    Dest = "src\Chuvadi.Pdf.Primitives";           As = "PdfPrimitive.cs" }
    @{ File = "PdfNull.cs";                         Dest = "src\Chuvadi.Pdf.Primitives";           As = "PdfNull.cs" }
    @{ File = "PdfBoolean.cs";                      Dest = "src\Chuvadi.Pdf.Primitives";           As = "PdfBoolean.cs" }
    @{ File = "PdfInteger.cs";                      Dest = "src\Chuvadi.Pdf.Primitives";           As = "PdfInteger.cs" }
    @{ File = "PdfReal.cs";                         Dest = "src\Chuvadi.Pdf.Primitives";           As = "PdfReal.cs" }
    @{ File = "PdfName.cs";                         Dest = "src\Chuvadi.Pdf.Primitives";           As = "PdfName.cs" }
    @{ File = "PdfString.cs";                       Dest = "src\Chuvadi.Pdf.Primitives";           As = "PdfString.cs" }
    @{ File = "PdfArray.cs";                        Dest = "src\Chuvadi.Pdf.Primitives";           As = "PdfArray.cs" }
    @{ File = "PdfDictionary.cs";                   Dest = "src\Chuvadi.Pdf.Primitives";           As = "PdfDictionary.cs" }
    @{ File = "PdfStream.cs";                       Dest = "src\Chuvadi.Pdf.Primitives";           As = "PdfStream.cs" }
    @{ File = "PdfReference.cs";                    Dest = "src\Chuvadi.Pdf.Primitives";           As = "PdfReference.cs" }
    @{ File = "PdfTokenType.cs";                    Dest = "src\Chuvadi.Pdf.Primitives";           As = "PdfTokenType.cs" }
    @{ File = "PdfToken.cs";                        Dest = "src\Chuvadi.Pdf.Primitives";           As = "PdfToken.cs" }
    @{ File = "PdfTokenizer.cs";                    Dest = "src\Chuvadi.Pdf.Primitives";           As = "PdfTokenizer.cs" }
    @{ File = "PdfTokenizerException.cs";           Dest = "src\Chuvadi.Pdf.Primitives";           As = "PdfTokenizerException.cs" }
    @{ File = "IStreamFilter.cs";                   Dest = "src\Chuvadi.Pdf.Filters";              As = "IStreamFilter.cs" }
    @{ File = "FilterException.cs";                 Dest = "src\Chuvadi.Pdf.Filters";              As = "FilterException.cs" }
    @{ File = "Adler32.cs";                         Dest = "src\Chuvadi.Pdf.Filters";              As = "Adler32.cs" }
    @{ File = "DeflateFilter.cs";                   Dest = "src\Chuvadi.Pdf.Filters";              As = "DeflateFilter.cs" }
    @{ File = "AsciiHexFilter.cs";                  Dest = "src\Chuvadi.Pdf.Filters";              As = "AsciiHexFilter.cs" }
    @{ File = "Ascii85Filter.cs";                   Dest = "src\Chuvadi.Pdf.Filters";              As = "Ascii85Filter.cs" }
    @{ File = "RunLengthFilter.cs";                 Dest = "src\Chuvadi.Pdf.Filters";              As = "RunLengthFilter.cs" }
    @{ File = "LzwFilter.cs";                       Dest = "src\Chuvadi.Pdf.Filters";              As = "LzwFilter.cs" }
    @{ File = "FilterPipeline.cs";                  Dest = "src\Chuvadi.Pdf.Filters";              As = "FilterPipeline.cs" }
    @{ File = "FilterRegistry.cs";                  Dest = "src\Chuvadi.Pdf.Filters";              As = "FilterRegistry.cs" }
    @{ File = "PdfIndirectObject.cs";               Dest = "src\Chuvadi.Pdf.Objects";              As = "PdfIndirectObject.cs" }
    @{ File = "IPdfObjectResolver.cs";              Dest = "src\Chuvadi.Pdf.Objects";              As = "IPdfObjectResolver.cs" }
    @{ File = "PdfObjectException.cs";              Dest = "src\Chuvadi.Pdf.Objects";              As = "PdfObjectException.cs" }
    @{ File = "PdfObjectStore.cs";                  Dest = "src\Chuvadi.Pdf.Objects";              As = "PdfObjectStore.cs" }
    @{ File = "XrefEntry.cs";                       Dest = "src\Chuvadi.Pdf.Objects";              As = "XrefEntry.cs" }
    @{ File = "XrefTable.cs";                       Dest = "src\Chuvadi.Pdf.Objects";              As = "XrefTable.cs" }
    @{ File = "XrefStreamTable.cs";                 Dest = "src\Chuvadi.Pdf.Objects";              As = "XrefStreamTable.cs" }
    @{ File = "PdfReaderException.cs";              Dest = "src\Chuvadi.Pdf.IO";                   As = "PdfReaderException.cs" }
    @{ File = "PdfObjectParser.cs";                 Dest = "src\Chuvadi.Pdf.IO";                   As = "PdfObjectParser.cs" }
    @{ File = "PdfReader.cs";                       Dest = "src\Chuvadi.Pdf.IO";                   As = "PdfReader.cs" }
    @{ File = "PdfWriter.cs";                       Dest = "src\Chuvadi.Pdf.IO";                   As = "PdfWriter.cs" }
    @{ File = "PdfDocumentException.cs";            Dest = "src\Chuvadi.Pdf.Documents";            As = "PdfDocumentException.cs" }
    @{ File = "PdfPage.cs";                         Dest = "src\Chuvadi.Pdf.Documents";            As = "PdfPage.cs" }
    @{ File = "PdfPageCollection.cs";               Dest = "src\Chuvadi.Pdf.Documents";            As = "PdfPageCollection.cs" }
    @{ File = "PdfDocument.cs";                     Dest = "src\Chuvadi.Pdf.Documents";            As = "PdfDocument.cs" }
    @{ File = "FontException.cs";                   Dest = "src\Chuvadi.Pdf.Fonts";                As = "FontException.cs" }
    @{ File = "PdfFontEncoding.cs";                 Dest = "src\Chuvadi.Pdf.Fonts";                As = "PdfFontEncoding.cs" }
    @{ File = "CMapParser.cs";                      Dest = "src\Chuvadi.Pdf.Fonts";                As = "CMapParser.cs" }
    @{ File = "PdfFont.cs";                         Dest = "src\Chuvadi.Pdf.Fonts";                As = "PdfFont.cs" }
    @{ File = "Chuvadi.Pdf.Fonts.csproj";           Dest = "src\Chuvadi.Pdf.Fonts";                As = "Chuvadi.Pdf.Fonts.csproj" }
    @{ File = "ContentException.cs";                Dest = "src\Chuvadi.Pdf.Content";              As = "ContentException.cs" }
    @{ File = "GraphicsState.cs";                   Dest = "src\Chuvadi.Pdf.Content";              As = "GraphicsState.cs" }
    @{ File = "TextFragment.cs";                    Dest = "src\Chuvadi.Pdf.Content";              As = "TextFragment.cs" }
    @{ File = "ContentStreamParser.cs";             Dest = "src\Chuvadi.Pdf.Content";              As = "ContentStreamParser.cs" }
    @{ File = "Chuvadi.Pdf.Content.csproj";         Dest = "src\Chuvadi.Pdf.Content";              As = "Chuvadi.Pdf.Content.csproj" }
    @{ File = "OperatorExtractor.cs";               Dest = "src\Chuvadi.Pdf.Text";                 As = "OperatorExtractor.cs" }
    @{ File = "LayoutExtractor.cs";                 Dest = "src\Chuvadi.Pdf.Text";                 As = "LayoutExtractor.cs" }
    @{ File = "TextExtractor.cs";                   Dest = "src\Chuvadi.Pdf.Text";                 As = "TextExtractor.cs" }
    @{ File = "OperationsException.cs";             Dest = "src\Chuvadi.Pdf.Operations";           As = "OperationsException.cs" }
    @{ File = "PageOperations.cs";                  Dest = "src\Chuvadi.Pdf.Operations";           As = "PageOperations.cs" }
    @{ File = "Chuvadi.Pdf.Graphics.csproj";        Dest = "src\Chuvadi.Pdf.Graphics";             As = "Chuvadi.Pdf.Graphics.csproj" }
    @{ File = "PointF.cs";                          Dest = "src\Chuvadi.Pdf.Graphics";             As = "PointF.cs" }
    @{ File = "SizeF.cs";                           Dest = "src\Chuvadi.Pdf.Graphics";             As = "SizeF.cs" }
    @{ File = "RectangleF.cs";                      Dest = "src\Chuvadi.Pdf.Graphics";             As = "RectangleF.cs" }
    @{ File = "ColorSpace.cs";                      Dest = "src\Chuvadi.Pdf.Graphics";             As = "ColorSpace.cs" }
    @{ File = "ColorF.cs";                          Dest = "src\Chuvadi.Pdf.Graphics";             As = "ColorF.cs" }
    @{ File = "Transform.cs";                       Dest = "src\Chuvadi.Pdf.Graphics";             As = "Transform.cs" }
    @{ File = "FillRule.cs";                        Dest = "src\Chuvadi.Pdf.Graphics";             As = "FillRule.cs" }
    @{ File = "LineCap.cs";                         Dest = "src\Chuvadi.Pdf.Graphics";             As = "LineCap.cs" }
    @{ File = "LineJoin.cs";                        Dest = "src\Chuvadi.Pdf.Graphics";             As = "LineJoin.cs" }
    @{ File = "StrokeStyle.cs";                     Dest = "src\Chuvadi.Pdf.Graphics";             As = "StrokeStyle.cs" }
    @{ File = "PathSegment.cs";                     Dest = "src\Chuvadi.Pdf.Graphics";             As = "PathSegment.cs" }
    @{ File = "Path.cs";                            Dest = "src\Chuvadi.Pdf.Graphics";             As = "Path.cs" }
    @{ File = "PixelBuffer.cs";                     Dest = "src\Chuvadi.Pdf.Graphics";             As = "PixelBuffer.cs" }
    @{ File = "PathFlattener.cs";                   Dest = "src\Chuvadi.Pdf.Graphics";             As = "PathFlattener.cs" }
    @{ File = "Chuvadi.Pdf.Graphics.Tests.csproj";  Dest = "tests\Chuvadi.Pdf.Graphics.Tests";     As = "Chuvadi.Pdf.Graphics.Tests.csproj" }
    @{ File = "GraphicsTests.cs";                   Dest = "tests\Chuvadi.Pdf.Graphics.Tests";     As = "GraphicsTests.cs" }
    @{ File = "Chuvadi.Pdf.Images.csproj";        Dest = "src\Chuvadi.Pdf.Images";              As = "Chuvadi.Pdf.Images.csproj" }
    @{ File = "ImageException.cs";              Dest = "src\Chuvadi.Pdf.Images";              As = "ImageException.cs" }
    @{ File = "ImageFrame.cs";                  Dest = "src\Chuvadi.Pdf.Images";              As = "ImageFrame.cs" }
    @{ File = "BmpEncoder.cs";                  Dest = "src\Chuvadi.Pdf.Images";              As = "BmpEncoder.cs" }
    @{ File = "PngEncoder.cs";                  Dest = "src\Chuvadi.Pdf.Images";              As = "PngEncoder.cs" }
    @{ File = "PngDecoder.cs";                  Dest = "src\Chuvadi.Pdf.Images";              As = "PngDecoder.cs" }
    @{ File = "JpegDecoder.cs";                 Dest = "src\Chuvadi.Pdf.Images";              As = "JpegDecoder.cs" }
    @{ File = "Chuvadi.Pdf.Images.Tests.csproj"; Dest = "tests\Chuvadi.Pdf.Images.Tests";    As = "Chuvadi.Pdf.Images.Tests.csproj" }
    @{ File = "ImageTests.cs";                  Dest = "tests\Chuvadi.Pdf.Images.Tests";    As = "ImageTests.cs" }
    @{ File = "Chuvadi.Pdf.Fonts.Rendering.csproj";          Dest = "src\Chuvadi.Pdf.Fonts.Rendering";             As = "Chuvadi.Pdf.Fonts.Rendering.csproj" }
    @{ File = "FontRenderingException.cs";                   Dest = "src\Chuvadi.Pdf.Fonts.Rendering";             As = "FontRenderingException.cs" }
    @{ File = "GlyphMetrics.cs";                             Dest = "src\Chuvadi.Pdf.Fonts.Rendering";             As = "GlyphMetrics.cs" }
    @{ File = "GlyphOutline.cs";                             Dest = "src\Chuvadi.Pdf.Fonts.Rendering";             As = "GlyphOutline.cs" }
    @{ File = "TrueTypeLoader.cs";                           Dest = "src\Chuvadi.Pdf.Fonts.Rendering";             As = "TrueTypeLoader.cs" }
    @{ File = "FontRenderer.cs";                             Dest = "src\Chuvadi.Pdf.Fonts.Rendering";             As = "FontRenderer.cs" }
    @{ File = "Chuvadi.Pdf.Fonts.Rendering.Tests.csproj";   Dest = "tests\Chuvadi.Pdf.Fonts.Rendering.Tests";    As = "Chuvadi.Pdf.Fonts.Rendering.Tests.csproj" }
    @{ File = "FontRenderingTests.cs";                       Dest = "tests\Chuvadi.Pdf.Fonts.Rendering.Tests";    As = "FontRenderingTests.cs" }
    @{ File = "Chuvadi.Pdf.Rendering.csproj";        Dest = "src\Chuvadi.Pdf.Rendering";               As = "Chuvadi.Pdf.Rendering.csproj" }
    @{ File = "RenderingException.cs";                Dest = "src\Chuvadi.Pdf.Rendering";               As = "RenderingException.cs" }
    @{ File = "RenderOptions.cs";                     Dest = "src\Chuvadi.Pdf.Rendering";               As = "RenderOptions.cs" }
    @{ File = "ScanlineRasterizer.cs";                Dest = "src\Chuvadi.Pdf.Rendering";               As = "ScanlineRasterizer.cs" }
    @{ File = "StrokeExpander.cs";                    Dest = "src\Chuvadi.Pdf.Rendering";               As = "StrokeExpander.cs" }
    @{ File = "PageRasterizer.cs";                    Dest = "src\Chuvadi.Pdf.Rendering";               As = "PageRasterizer.cs" }
    @{ File = "Chuvadi.Pdf.Rendering.Tests.csproj";  Dest = "tests\Chuvadi.Pdf.Rendering.Tests";      As = "Chuvadi.Pdf.Rendering.Tests.csproj" }
    @{ File = "RenderingTests.cs";                    Dest = "tests\Chuvadi.Pdf.Rendering.Tests";      As = "RenderingTests.cs" }
    @{ File = "Chuvadi.Pdf.Watermark.csproj";       Dest = "src\Chuvadi.Pdf.Watermark";              As = "Chuvadi.Pdf.Watermark.csproj" }
    @{ File = "WatermarkException.cs";               Dest = "src\Chuvadi.Pdf.Watermark";              As = "WatermarkException.cs" }
    @{ File = "TextWatermarkOptions.cs";             Dest = "src\Chuvadi.Pdf.Watermark";              As = "TextWatermarkOptions.cs" }
    @{ File = "ImageWatermarkOptions.cs";            Dest = "src\Chuvadi.Pdf.Watermark";              As = "ImageWatermarkOptions.cs" }
    @{ File = "WatermarkStamper.cs";                 Dest = "src\Chuvadi.Pdf.Watermark";              As = "WatermarkStamper.cs" }
    @{ File = "Chuvadi.Pdf.Watermark.Tests.csproj"; Dest = "tests\Chuvadi.Pdf.Watermark.Tests";     As = "Chuvadi.Pdf.Watermark.Tests.csproj" }
    @{ File = "WatermarkTests.cs";                   Dest = "tests\Chuvadi.Pdf.Watermark.Tests";     As = "WatermarkTests.cs" }
    @{ File = "Chuvadi.Pdf.Redaction.csproj";        Dest = "src\Chuvadi.Pdf.Redaction";              As = "Chuvadi.Pdf.Redaction.csproj" }
    @{ File = "RedactionException.cs";                Dest = "src\Chuvadi.Pdf.Redaction";              As = "RedactionException.cs" }
    @{ File = "RedactionRect.cs";                     Dest = "src\Chuvadi.Pdf.Redaction";              As = "RedactionRect.cs" }
    @{ File = "RedactionOptions.cs";                  Dest = "src\Chuvadi.Pdf.Redaction";              As = "RedactionOptions.cs" }
    @{ File = "Redactor.cs";                          Dest = "src\Chuvadi.Pdf.Redaction";              As = "Redactor.cs" }
    @{ File = "Chuvadi.Pdf.Redaction.Tests.csproj";  Dest = "tests\Chuvadi.Pdf.Redaction.Tests";     As = "Chuvadi.Pdf.Redaction.Tests.csproj" }
    @{ File = "RedactionTests.cs";                    Dest = "tests\Chuvadi.Pdf.Redaction.Tests";     As = "RedactionTests.cs" }
    @{ File = "Chuvadi.Pdf.Forms.csproj";            Dest = "src\Chuvadi.Pdf.Forms";                  As = "Chuvadi.Pdf.Forms.csproj" }
    @{ File = "FormException.cs";                    Dest = "src\Chuvadi.Pdf.Forms";                  As = "FormException.cs" }
    @{ File = "FormFieldType.cs";                    Dest = "src\Chuvadi.Pdf.Forms";                  As = "FormFieldType.cs" }
    @{ File = "FormField.cs";                        Dest = "src\Chuvadi.Pdf.Forms";                  As = "FormField.cs" }
    @{ File = "OutlineItem.cs";                      Dest = "src\Chuvadi.Pdf.Forms";                  As = "OutlineItem.cs" }
    @{ File = "FormReader.cs";                       Dest = "src\Chuvadi.Pdf.Forms";                  As = "FormReader.cs" }
    @{ File = "FormFiller.cs";                       Dest = "src\Chuvadi.Pdf.Forms";                  As = "FormFiller.cs" }
    @{ File = "OutlineReader.cs";                    Dest = "src\Chuvadi.Pdf.Forms";                  As = "OutlineReader.cs" }
    @{ File = "Chuvadi.Pdf.Forms.Tests.csproj";     Dest = "tests\Chuvadi.Pdf.Forms.Tests";         As = "Chuvadi.Pdf.Forms.Tests.csproj" }
    @{ File = "FormsTests.cs";                       Dest = "tests\Chuvadi.Pdf.Forms.Tests";         As = "FormsTests.cs" }
    @{ File = "Chuvadi.Pdf.Cli.csproj";              Dest = "tools\Chuvadi.Pdf.Cli";                  As = "Chuvadi.Pdf.Cli.csproj" }
    @{ File = "Program.cs";                          Dest = "tools\Chuvadi.Pdf.Cli";                  As = "Program.cs" }
    @{ File = "UserCommands.cs";                     Dest = "tools\Chuvadi.Pdf.Cli\Commands";       As = "UserCommands.cs" }
    @{ File = "DebugCommands.cs";                    Dest = "tools\Chuvadi.Pdf.Cli\Commands";       As = "DebugCommands.cs" }
    @{ File = "Chuvadi.Pdf.Cli.Tests.csproj";       Dest = "tests\Chuvadi.Pdf.Cli.Tests";           As = "Chuvadi.Pdf.Cli.Tests.csproj" }
    @{ File = "CliTests.cs";                         Dest = "tests\Chuvadi.Pdf.Cli.Tests";           As = "CliTests.cs" }
    @{ File = "PdfPrimitivesTests.cs";              Dest = "tests\Chuvadi.Pdf.Primitives.Tests";   As = "PdfPrimitivesTests.cs" }
    @{ File = "PdfTokenizerTests.cs";               Dest = "tests\Chuvadi.Pdf.Primitives.Tests";   As = "PdfTokenizerTests.cs" }
    @{ File = "DeflateFilterTests.cs";              Dest = "tests\Chuvadi.Pdf.Filters.Tests";      As = "DeflateFilterTests.cs" }
    @{ File = "RemainingFiltersTests.cs";           Dest = "tests\Chuvadi.Pdf.Filters.Tests";      As = "RemainingFiltersTests.cs" }
    @{ File = "PdfObjectsTests.cs";                 Dest = "tests\Chuvadi.Pdf.Objects.Tests";      As = "PdfObjectsTests.cs" }
    @{ File = "PdfIOTests.cs";                      Dest = "tests\Chuvadi.Pdf.IO.Tests";           As = "PdfIOTests.cs" }
    @{ File = "PdfDocumentsTests.cs";               Dest = "tests\Chuvadi.Pdf.Documents.Tests";    As = "PdfDocumentsTests.cs" }
    @{ File = "PdfFontTests.cs";                    Dest = "tests\Chuvadi.Pdf.Fonts.Tests";        As = "PdfFontTests.cs" }
    @{ File = "ContentTests.cs";                    Dest = "tests\Chuvadi.Pdf.Content.Tests";      As = "ContentTests.cs" }
    @{ File = "TextExtractionTests.cs";             Dest = "tests\Chuvadi.Pdf.Text.Tests";         As = "TextExtractionTests.cs" }
    @{ File = "PageOperationsTests.cs";             Dest = "tests\Chuvadi.Pdf.Operations.Tests";   As = "PageOperationsTests.cs" }
    @{ File = "PlaceholderTests_Objects.cs";        Dest = "tests\Chuvadi.Pdf.Objects.Tests";      As = "PlaceholderTests.cs" }
    @{ File = "PlaceholderTests_IO.cs";             Dest = "tests\Chuvadi.Pdf.IO.Tests";           As = "PlaceholderTests.cs" }
    @{ File = "PlaceholderTests_Fonts.cs";          Dest = "tests\Chuvadi.Pdf.Fonts.Tests";        As = "PlaceholderTests.cs" }
    @{ File = "PlaceholderTests_Documents.cs";      Dest = "tests\Chuvadi.Pdf.Documents.Tests";    As = "PlaceholderTests.cs" }
    @{ File = "PlaceholderTests_Content.cs";        Dest = "tests\Chuvadi.Pdf.Content.Tests";      As = "PlaceholderTests.cs" }
    @{ File = "PlaceholderTests_Text.cs";           Dest = "tests\Chuvadi.Pdf.Text.Tests";         As = "PlaceholderTests.cs" }
    @{ File = "PlaceholderTests_Operations.cs";     Dest = "tests\Chuvadi.Pdf.Operations.Tests";   As = "PlaceholderTests.cs" }
    @{ File = "PlaceholderTests_Integration.cs";    Dest = "tests\Chuvadi.Pdf.Integration.Tests";  As = "PlaceholderTests.cs" }
    @{ File = "CHANGE-LOG.md";                      Dest = "docs";                                 As = "CHANGE-LOG.md" }
    @{ File = "BASELINE.md";                        Dest = "docs";                                 As = "BASELINE.md" }
    @{ File = "SESSION-STATE.md";                   Dest = "docs";                                 As = "SESSION-STATE.md" }
    @{ File = "BACKLOG.md";                         Dest = "docs";                                 As = "BACKLOG.md" }
)

$copied  = 0
$missing = 0

Write-Host ""
Write-Host "Chuvadi Deploy" -ForegroundColor Cyan
Write-Host "Source : $downloadsChuvadi" -ForegroundColor Gray
Write-Host "Project: $projectRoot" -ForegroundColor Gray
Write-Host ""

foreach ($entry in $fileMap) {
    $sourcePath = Join-Path $downloadsChuvadi $entry.File
    $destFolder = Join-Path $projectRoot $entry.Dest
    $destName   = $entry.As
    $destPath   = Join-Path $destFolder $destName

    if (-not (Test-Path $sourcePath)) {
        Write-Host "  MISSING  $($entry.File)" -ForegroundColor DarkYellow
        $missing++
        continue
    }

    if (-not (Test-Path $destFolder)) {
        New-Item -ItemType Directory -Path $destFolder -Force | Out-Null
    }

    Copy-Item -Path $sourcePath -Destination $destPath -Force
    Write-Host "  COPIED   $($entry.File)  ->  $($entry.Dest)\$destName" -ForegroundColor Green
    $copied++
}

Write-Host ""
Write-Host "----------------------------------------" -ForegroundColor Gray
Write-Host "  Copied : $copied" -ForegroundColor Green

if ($missing -gt 0) {
    Write-Host "  Missing: $missing" -ForegroundColor Yellow
}

Write-Host ""

if ($missing -eq 0) {
    Write-Host "All files deployed. Run:" -ForegroundColor Cyan
    Write-Host "  dotnet build" -ForegroundColor White
    Write-Host "  dotnet test" -ForegroundColor White
} else {
    Write-Host "Some files were missing. Download them and re-run." -ForegroundColor Yellow
}

Write-Host ""
