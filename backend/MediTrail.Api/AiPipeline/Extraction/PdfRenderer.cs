using PDFtoImage;
using SkiaSharp;

namespace MediTrail.Api.AiPipeline.Extraction;

/// <summary>
/// Renders PDF pages to images so they go through the same vision pipeline as a photo (FR-2.7).
/// </summary>
public interface IPdfRenderer
{
    /// <summary>
    /// One PNG per page. Throws <see cref="PdfRenderException"/> with a readable reason when the
    /// file cannot be opened — encrypted, corrupt, or not really a PDF.
    /// </summary>
    IReadOnlyList<byte[]> Render(byte[] pdf);
}

public sealed class PdfRenderException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class PdfRenderer(ILogger<PdfRenderer> logger) : IPdfRenderer
{
    /// <summary>
    /// Every page goes into one extraction call, so a long PDF is a cost and context risk.
    /// Beyond this the document is almost certainly not a prescription or lab report.
    /// </summary>
    private const int MaxPages = 10;

    /// <summary>
    /// 200 DPI. Enough for small print on a lab report; 300 roughly doubles the tokens for
    /// legibility the model does not gain from.
    /// </summary>
    private const int Dpi = 200;

    public IReadOnlyList<byte[]> Render(byte[] pdf)
    {
        int pageCount;
        try
        {
            pageCount = Conversion.GetPageCount(pdf);
        }
        catch (Exception ex)
        {
            // Password-protected is the common case and worth naming, since the user can fix it.
            throw new PdfRenderException(
                "This PDF could not be opened. If it is password-protected, remove the password and upload it again.",
                ex);
        }

        if (pageCount == 0)
        {
            throw new PdfRenderException("This PDF has no pages.");
        }

        var pages = new List<byte[]>();
        var limit = Math.Min(pageCount, MaxPages);

        for (var index = 0; index < limit; index++)
        {
            try
            {
                using var bitmap = Conversion.ToImage(pdf, page: index, options: new(Dpi: Dpi));
                using var data = bitmap.Encode(SKEncodedImageFormat.Png, 90);

                pages.Add(data.ToArray());
            }
            catch (Exception ex)
            {
                // One unreadable page must not lose the rest of the document.
                logger.LogWarning(ex, "Could not render page {Page} of {Total}", index + 1, pageCount);
            }
        }

        if (pages.Count == 0)
        {
            throw new PdfRenderException("None of the pages in this PDF could be read as an image.");
        }

        if (pageCount > MaxPages)
        {
            logger.LogWarning("PDF has {Total} pages; only the first {Limit} were read", pageCount, MaxPages);
        }

        return pages;
    }
}
