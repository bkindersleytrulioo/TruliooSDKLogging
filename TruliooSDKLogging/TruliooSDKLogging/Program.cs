using System.IO.Compression;
using Trulioo.Client.V1;
using Trulioo.Client.V1.Exceptions;

var context = new Context("username", "password", TimeSpan.FromSeconds(120), new LoggingHandler(new Logger()));
var client = new TruliooApiClient(context);
try
{
    var res = await client.Connection.TestAuthenticationAsync();
    Console.WriteLine(res);
}
catch (RequestException ex)
{
    Console.WriteLine($"HTTP Non-Success Exception: {ex}");
}
catch (Exception e)
{
    Console.WriteLine($"Connectivity Exception: {e}");
}

/// <summary>
/// TODO: Implement as desired
/// </summary>
public class Logger
{
    public void Log(HttpRequestMessage request)
    {
        Console.WriteLine($"REQUEST: {request.RequestUri} : {request.Method}");
    }
    public void Log(HttpResponseMessage response)
    {
        Console.WriteLine($"RESPONSE: {response?.StatusCode}");
    }
}

internal class LoggingHandler : GZipDecompressionHandler
{
    private readonly Logger _logger;

    public LoggingHandler(Logger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _logger.Log(request);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _logger.Log(response);
        return response;
    }
}

//essentially duplicated from https://github.com/Trulioo/sdk-csharp-v1/tree/master/src/Trulioo.Client.V1/Compressor
//for access to internal classes

internal class GZipCompressor
{
    #region Methods

    /// <summary>
    /// Compress the stream.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="destination"></param>
    /// <returns></returns>
    public static Task Compress(Stream source, Stream destination)
    {
        var compressed = CreateCompressionStream(destination);
        return Pump(source, compressed).ContinueWith(task => compressed.Dispose());
    }

    /// <summary>
    /// Decompress the stream.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="destination"></param>
    /// <returns></returns>
    public static Task Decompress(Stream source, Stream destination)
    {
        var decompressed = CreateDecompressionStream(source);
        return Pump(decompressed, destination).ContinueWith(task => decompressed.Dispose());
    }

    #endregion

    #region Privates/internals

    /// <summary>
    /// Create compression stream.
    /// </summary>
    /// <param name="output"></param>
    /// <returns></returns>
    private static Stream CreateCompressionStream(Stream output)
    {
        return new GZipStream(output, CompressionMode.Compress, true);
    }

    /// <summary>
    /// Create decompression stream.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    private static Stream CreateDecompressionStream(Stream input)
    {
        return new GZipStream(input, CompressionMode.Decompress, true);
    }

    /// <summary>
    /// Pump the stream.
    /// </summary>
    /// <param name="input"></param>
    /// <param name="output"></param>
    /// <returns></returns>
    private static Task Pump(Stream input, Stream output)
    {
        return input.CopyToAsync(output);
    }

    #endregion
}

internal class GZipDecompressionHandler : HttpClientHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        //this is not enabled in the client by default; initial experiments showed little gain in most cases
        request.Content = await CompressContentAsync(request.Content).ConfigureAwait(false);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Content.Headers.ContentEncoding != null && response.Content.Headers.ContentEncoding.Any())
        {
            var encoding = response.Content.Headers.ContentEncoding.First();

            if (encoding.Equals("gzip", StringComparison.OrdinalIgnoreCase))
            {
                response.Content = await DecompressContentAsync(response.Content, new GZipCompressor()).ConfigureAwait(false);
            }
        }
        return response;
    }

    private static async Task<HttpContent> CompressContentAsync(HttpContent rawContent)
    {
        if (rawContent == null) return null;
        using (rawContent)
        {
            var compressed = new MemoryStream();
            await GZipCompressor.Compress(await rawContent.ReadAsStreamAsync(), compressed).ConfigureAwait(false);

            // set position back to 0 so it can be read again
            compressed.Position = 0;

            var newContent = new StreamContent(compressed);
            newContent.Headers.Add("Content-Encoding", "gzip");
            // copy content type so we know how to load correct formatter
            newContent.Headers.ContentType = rawContent.Headers.ContentType;
            return newContent;
        }
    }

    private static async Task<HttpContent> DecompressContentAsync(HttpContent compressedContent, GZipCompressor compressor)
    {
        using (compressedContent)
        {
            var decompressed = new MemoryStream();
            await GZipCompressor.Decompress(await compressedContent.ReadAsStreamAsync(), decompressed).ConfigureAwait(false);

            // set position back to 0 so it can be read again
            decompressed.Position = 0;

            var newContent = new StreamContent(decompressed);
            // copy content type so we know how to load correct formatter
            newContent.Headers.ContentType = compressedContent.Headers.ContentType;
            return newContent;
        }
    }
}