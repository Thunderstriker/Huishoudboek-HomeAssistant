using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Web;

namespace Huishoudboek.Service;

/// <summary>One bounded local job at a time. Documents go through pipes, never temporary document files or a cloud API.</summary>
public sealed class ReceiptRecognition : IDisposable
{
    private readonly SemaphoreSlim _worker = new(1, 1);

    public async Task<(IReadOnlyList<string> Pages, ReceiptRecognitionState State)> ReadTariffPagesAsync(byte[] content, CancellationToken cancellationToken)
    {
        if (!await _worker.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)) return ([], ReceiptRecognitionState.Busy);
        try
        {
            var bytes = await RunAsync("pdftotext", ["-f", "1", "-l", "2", "-layout", "-enc", "UTF-8", "-", "-"], content, 160_000, cancellationToken);
            var pages = Encoding.UTF8.GetString(bytes).Split('\f').Take(2).ToArray();
            return (pages, pages.All(string.IsNullOrWhiteSpace) ? ReceiptRecognitionState.NoText : ReceiptRecognitionState.Recognized);
        }
        catch (Win32Exception) { return ([], ReceiptRecognitionState.Unavailable); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return ([], ReceiptRecognitionState.TimedOut); }
        catch (ReceiptToolException) { return ([], ReceiptRecognitionState.Failed); }
        catch (IOException) { cancellationToken.ThrowIfCancellationRequested(); return ([], ReceiptRecognitionState.Failed); }
        finally { _worker.Release(); }
    }

    public async Task<(string Text, ReceiptRecognitionState State)> ReadAsync(ReceiptDocument document, CancellationToken cancellationToken)
    {
        if (!await _worker.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)) return ("", ReceiptRecognitionState.Busy);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            var token = timeout.Token;
            string text;
            if (document.ContentType == "application/pdf")
            {
                var info = Encoding.UTF8.GetString(await RunAsync("pdfinfo", ["-"], document.Content, 64_000, token));
                var match = Regex.Match(info, @"^Pages:\s*(\d+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
                if (!match.Success || !int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var pages) || pages is < 1 or > 5)
                    throw new InvalidDataException("Kies een PDF-bon van maximaal vijf pagina's.");
                var result = new StringBuilder();
                for (var page = 1; page <= pages; page++)
                {
                    var number = page.ToString(CultureInfo.InvariantCulture);
                    var pageText = Encoding.UTF8.GetString(await RunAsync("pdftotext", ["-f", number, "-l", number, "-layout", "-nopgbrk", "-", "-"],
                        document.Content, ReceiptTextParser.MaximumTextLength * 4, token));
                    if (pageText.Count(char.IsLetter) < 10 || !Regex.IsMatch(pageText, @"\d[,.]\d{2}\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)))
                    {
                        var image = await RunAsync("pdftoppm", ["-f", number, "-l", number, "-scale-to", "2600", "-singlefile", "-png", "-"],
                            document.Content, 32 * 1024 * 1024, token);
                        pageText = await OcrAsync(image, token);
                    }
                    result.AppendLine(pageText);
                    if (result.Length > ReceiptTextParser.MaximumTextLength) throw new InvalidDataException("De herkende bontekst is te lang.");
                }
                text = result.ToString();
            }
            else text = await OcrAsync(document.Content, token);
            return (text, string.IsNullOrWhiteSpace(text) ? ReceiptRecognitionState.NoText : ReceiptRecognitionState.Recognized);
        }
        catch (Win32Exception) { return ("", ReceiptRecognitionState.Unavailable); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return ("", ReceiptRecognitionState.TimedOut); }
        catch (ReceiptToolException) { return ("", ReceiptRecognitionState.Failed); }
        catch (IOException) { cancellationToken.ThrowIfCancellationRequested(); return ("", ReceiptRecognitionState.Failed); }
        finally { _worker.Release(); }
    }

    private static async Task<string> OcrAsync(byte[] content, CancellationToken token)
    {
        var tsv = Encoding.UTF8.GetString(await RunAsync("tesseract", ["stdin", "stdout", "-l", "nld+eng", "--psm", "6", "tsv"], content, 4 * 1024 * 1024, token));
        var lines = new List<string>();
        string? key = null;
        var words = new List<(int Left, string Text)>();
        foreach (var row in tsv.Split('\n').Skip(1))
        {
            var columns = row.TrimEnd('\r').Split('\t', 12);
            if (columns.Length != 12 || columns[0] != "5" || string.IsNullOrWhiteSpace(columns[11])) continue;
            var current = string.Join('/', columns.Skip(1).Take(4));
            if (key != current && words.Count > 0) { lines.Add(string.Join(' ', words.OrderBy(w => w.Left).Select(w => w.Text))); words.Clear(); }
            key = current;
            if (int.TryParse(columns[6], CultureInfo.InvariantCulture, out var left)) words.Add((left, columns[11]));
        }
        if (words.Count > 0) lines.Add(string.Join(' ', words.OrderBy(w => w.Left).Select(w => w.Text)));
        var text = string.Join('\n', lines);
        if (text.Length > ReceiptTextParser.MaximumTextLength) throw new InvalidDataException("De herkende bontekst is te lang.");
        return text;
    }

    private static async Task<byte[]> RunAsync(string executable, string[] arguments, byte[] input, int outputLimit, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["OMP_THREAD_LIMIT"] = "1";
        start.Environment["LC_ALL"] = "C";
        using var process = new Process { StartInfo = start };
        process.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        void Kill() { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (Win32Exception) { } }
        using var registration = timeout.Token.Register(Kill);
        async Task<byte[]> ReadAsync(Stream stream, int limit)
        {
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (output.Length + count > limit) { Kill(); throw new ReceiptToolException(); }
                await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
            }
            return output.ToArray();
        }
        async Task WriteAsync()
        {
            try { await process.StandardInput.BaseStream.WriteAsync(input, timeout.Token); }
            catch (IOException) { /* A rejected document can close stdin early; check the exit code below. */ }
            finally { process.StandardInput.Close(); }
        }
        try
        {
            var output = ReadAsync(process.StandardOutput.BaseStream, outputLimit);
            var errors = ReadAsync(process.StandardError.BaseStream, 128_000);
            await Task.WhenAll(WriteAsync(), output, errors, process.WaitForExitAsync(timeout.Token));
            timeout.Token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new ReceiptToolException();
            return await output;
        }
        finally { Kill(); }
    }

    private sealed class ReceiptToolException : Exception;
    public void Dispose() => _worker.Dispose();
}
