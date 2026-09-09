using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Overseer.Services.Privacy;

/// <summary>
/// Scans buffers through AMSI, the Windows Antimalware Scan Interface, which routes to whatever
/// engine is registered on the host -- Microsoft Defender on a default Windows Server.
/// </summary>
/// <remarks>
/// In-process and synchronous, so it needs no service, no port and no container. The AMSI
/// context is created once and shared; a session is opened per scan, which is what AMSI asks
/// for when correlating several buffers of one logical object.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsAmsiScanner : IAntiMalwareScanner, IDisposable
{
    /* AMSI_RESULT: values at or above 32768 are malware. Anything below is clean, "not
       detected" or "blocked by admin", none of which is a detection. */
    private const int AmsiResultDetected = 32768;

    private readonly ILogger<WindowsAmsiScanner> _logger;
    private readonly IntPtr _context;
    private bool _disposed;

    public WindowsAmsiScanner(ILogger<WindowsAmsiScanner> logger)
    {
        _logger = logger;

        try
        {
            int hr = AmsiInitialize("GnollHackOverseer", out _context);
            if (hr != 0 || _context == IntPtr.Zero)
            {
                _context = IntPtr.Zero;
                _logger.LogWarning("AmsiInitialize failed with HRESULT 0x{HResult:X8}; AMSI scanning is unavailable.", hr);
            }
        }
        catch (DllNotFoundException)
        {
            _context = IntPtr.Zero;
            _logger.LogWarning("amsi.dll is not present; AMSI scanning is unavailable.");
        }
        catch (EntryPointNotFoundException)
        {
            _context = IntPtr.Zero;
            _logger.LogWarning("amsi.dll lacks the expected entry points; AMSI scanning is unavailable.");
        }
    }

    public string Name => "AMSI";

    public bool IsAvailable => _context != IntPtr.Zero;

    public Task<MalwareScanResult> ScanAsync(byte[] content, string contentName, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
            return Task.FromResult(MalwareScanResult.Failed("AMSI is not initialized."));

        if (content.Length == 0)
            return Task.FromResult(MalwareScanResult.Clean);

        IntPtr session = IntPtr.Zero;
        try
        {
            int hr = AmsiOpenSession(_context, out session);
            if (hr != 0)
                return Task.FromResult(MalwareScanResult.Failed($"AmsiOpenSession returned 0x{hr:X8}."));

            hr = AmsiScanBuffer(_context, content, (uint)content.Length, contentName, session, out int result);
            if (hr != 0)
                return Task.FromResult(MalwareScanResult.Failed($"AmsiScanBuffer returned 0x{hr:X8}."));

            return Task.FromResult(result >= AmsiResultDetected
                ? MalwareScanResult.Malware($"AMSI_RESULT {result}")
                : MalwareScanResult.Clean);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AMSI scan of {ContentName} threw.", contentName);
            return Task.FromResult(MalwareScanResult.Failed(ex.Message));
        }
        finally
        {
            if (session != IntPtr.Zero)
                AmsiCloseSession(_context, session);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_context != IntPtr.Zero)
            AmsiUninitialize(_context);
    }

    [DllImport("amsi.dll", CharSet = CharSet.Unicode)]
    private static extern int AmsiInitialize(string appName, out IntPtr amsiContext);

    [DllImport("amsi.dll")]
    private static extern void AmsiUninitialize(IntPtr amsiContext);

    [DllImport("amsi.dll")]
    private static extern int AmsiOpenSession(IntPtr amsiContext, out IntPtr session);

    [DllImport("amsi.dll")]
    private static extern void AmsiCloseSession(IntPtr amsiContext, IntPtr session);

    [DllImport("amsi.dll", CharSet = CharSet.Unicode)]
    private static extern int AmsiScanBuffer(
        IntPtr amsiContext, byte[] buffer, uint length, string contentName, IntPtr session, out int result);
}
