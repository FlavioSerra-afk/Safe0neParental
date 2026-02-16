using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Safe0ne.ParentApp;

public partial class MainWindow : Window
{
    private Process? _serverProcess;
    private static readonly Uri DashboardBaseUri = new("http://127.0.0.1:8765/");
    private static Uri GetDashboardHomeUri(bool cacheBust = false)
    {

if (cacheBust)
{
    // Cache-bust the HTML shell so updated inline scripts (e.g., devtap trigger) are not stuck behind 304 + stale cache.
    var v = DateTime.UtcNow.Ticks;
    return new Uri($"http://127.0.0.1:8765/?v={v}#/dashboard");
}

return new Uri("http://127.0.0.1:8765/#/dashboard");

    }
    private const string WebView2DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Runtime readiness check (Evergreen model for now; decision is captured in ADR-0002 as TBD).
        if (!IsWebView2RuntimeAvailable())
        {
            Overlay.Visibility = Visibility.Visible;
            return;
        }

        StartLocalDashboardServer();

        // Navigate once WebView2 is ready.
        try
        {
            await Browser.EnsureCoreWebView2Async();
            try { Browser.CoreWebView2.Settings.IsWebMessageEnabled = true; } catch { /* ignore */ }

            // Dev panel is host-driven. Listen for "devtap" from the inline index.html script.
            Browser.CoreWebView2.WebMessageReceived += Browser_WebMessageReceived;

            // Keep devtools enabled for debugging (dev panel can also open them).
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = true;

            // Dev-tools unlock is implemented in the dashboard shell (index.html):
            // 7 taps on the rail logo within 5 seconds => reveal "Dev Tools" in the rail.
            // Leaving WebMessageReceived handler in place for the "devtap" message.
            // (No additional script injection needed here.)


            NavigateHomeNoCache();
        }
        catch
        {
            MessageBox.Show("Failed to start the embedded browser. Please verify WebView2 is installed.", "Safe0ne", MessageBoxButton.OK, MessageBoxImage.Error);
            Overlay.Visibility = Visibility.Visible;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        try
        {
            if (_serverProcess is { HasExited: false })
            {
                _serverProcess.Kill(entireProcessTree: true);
            }
        }
        catch { /* ignore */ }
    }

    private static bool IsWebView2RuntimeAvailable()
    {
        try
        {
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void StartLocalDashboardServer()
    {
        // For P0 we run the dashboard server as a sibling EXE.
        // This keeps the architecture aligned with the "local host dashboard" requirement.
        try
        {
            var exePath = System.IO.Path.Combine(AppContext.BaseDirectory, "Safe0ne.DashboardServer.exe");
            if (!System.IO.File.Exists(exePath))
            {
                // Dev-friendly fallback: assume it's next to the solution output.
                exePath = "Safe0ne.DashboardServer.exe";
            }

            _serverProcess = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch
        {
            // If the server doesn't start, the UI will still render if run directly in a browser.
        }
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => Close();

    private void CopyWebView2Link_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(WebView2DownloadUrl);
            MessageBox.Show("Download link copied to clipboard.", "Safe0ne", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch
        {
            // ignore
        }
    }

    private void Browser_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var msg = e.TryGetWebMessageAsString();
            if (string.Equals(msg, "devtap", StringComparison.OrdinalIgnoreCase))
            {
                ToggleDevPanel();
            }
        }
        catch
        {
            // ignore
        }
    }

    private void DevPanel_Close_Click(object sender, RoutedEventArgs e)
    {
        DevPanel.Visibility = Visibility.Collapsed;
    }

    private void DevPanel_OpenDevTools_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Browser.CoreWebView2?.OpenDevToolsWindow();
        }
        catch { /* ignore */ }
    }

    private void DevPanel_GoHome_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            NavigateHomeNoCache();
        }
        catch { /* ignore */ }
    }

    private void DevPanel_CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var url = Browser.Source?.ToString() ?? Browser.CoreWebView2?.Source ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(url))
            {
                Clipboard.SetText(url);
            }
        }
        catch { /* ignore */ }
    }

    private void DevPanel_Reload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Browser.CoreWebView2?.Reload();
        }
        catch { /* ignore */ }
    }

    private async void DevPanel_HardReload_Click(object sender, RoutedEventArgs e)
    {
        await ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
        try { Browser.CoreWebView2?.Reload(); } catch { /* ignore */ }
    }

    private async void DevPanel_ClearCache_Click(object sender, RoutedEventArgs e)
    {
        // Broad clear for recovery when cached JS/HTML becomes inconsistent.
        await ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
        try { Browser.CoreWebView2?.Reload(); } catch { /* ignore */ }
    }

    private void ToggleDevPanel()
    {
        DevPanel.Visibility = DevPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        if (DevPanel.Visibility == Visibility.Visible)
        {
            try
            {
                var url = Browser.Source?.ToString() ?? Browser.CoreWebView2?.Source ?? string.Empty;
                if (DevPanelStatus != null)
                {
                    DevPanelStatus.Text = string.IsNullOrWhiteSpace(url) ? "" : $"Current URL: {url}";
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private async Task ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds kinds)
    {
        try
        {
            var core = Browser.CoreWebView2;
            if (core?.Profile is null)
            {
                MessageBox.Show("WebView2 is not ready yet.", "Safe0ne", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await core.Profile.ClearBrowsingDataAsync(kinds);
        }
        catch (Exception ex)
        {
            // Keep the dev panel resilient: never crash the app.
            try
            {
                MessageBox.Show($"Failed to clear browsing data: {ex.Message}", "Safe0ne", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { /* ignore */ }
        }
    }


    private void NavigateHomeNoCache()
    {
        try
        {
            var uri = GetDashboardHomeUri(cacheBust: true);
            if (Browser.CoreWebView2 != null)
            {
                // Force revalidation of the app shell (index.html) to avoid stale cached HTML preventing the devtap script.
                var headers = "Cache-Control: no-cache\r\nPragma: no-cache\r\n";
                var req = Browser.CoreWebView2.Environment.CreateWebResourceRequest(uri.ToString(), "GET", null, headers);
                Browser.CoreWebView2.NavigateWithWebResourceRequest(req);
                return;
            }

            Browser.Source = uri;
        }
        catch
        {
            // fall back
            try { Browser.Source = GetDashboardHomeUri(cacheBust: true); } catch { }
        }
    }

    private Task InjectDevTapScriptAsync()
    {
        try
        {
            if (Browser.CoreWebView2 == null) return Task.CompletedTask;

            // Capturing handler on the document so it works regardless of the element’s internal listeners.
            // Falls back to matching the version text if the id is missing (e.g., stale cached HTML).
            var script = @"
(function(){
  try {
    var THRESH = 7;
	    // Requirement: 7 taps within 5 seconds
	    var WINDOW_MS = 5000;
    var count = 0;
    var start = 0;

	    function isVersionTarget(t){
      if (!t) return false;
      try {
        var el = t.closest ? t.closest('#appVersion') : null;
        if (el) return true;
      } catch(_) {}
	      try {
	        // Fallback: allow the left-rail logo area as an alternative tap target.
	        var logo = t.closest ? t.closest('.rail__logo') : null;
	        if (logo) return true;
	      } catch(_) {}
      try {
        // Fallback: look for the common footer/version node.
        var node = t;
        while (node && node !== document) {
          if (node.classList && node.classList.contains('rail__small')) {
	            // Treat any click inside the version footer as a valid target.
	            return true;
          }
          node = node.parentNode;
        }
      } catch(_) {}
      return false;
    }

    function hit(){
      var now = Date.now();
      if (!start || (now - start) > WINDOW_MS) { start = now; count = 0; }
      count++;
      if (count >= THRESH) {
        count = 0; start = 0;
        try { window.chrome && window.chrome.webview && window.chrome.webview.postMessage && window.chrome.webview.postMessage('devtap'); } catch(_) {}
      }
    }

    document.addEventListener('pointerdown', function(ev){ if (isVersionTarget(ev.target)) hit(); }, true);
    document.addEventListener('click', function(ev){ if (isVersionTarget(ev.target)) hit(); }, true);
  } catch(_) {}
})();";
            return Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
        }
        catch
        {
            return Task.CompletedTask;
        }
    }

}
