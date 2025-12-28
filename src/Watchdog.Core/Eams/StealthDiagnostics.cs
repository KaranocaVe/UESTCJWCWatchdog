using System.Text.Json;

namespace Watchdog.Core.Eams;

public static class StealthDiagnostics
{
    public static string GetFingerprintScript()
    {
        // Keep this script self-contained and JSON-string output so the C# side does not rely on complex binding.
        return """
(() => {
  function safe(fn, fallback) {
    try { return fn(); } catch (e) { return fallback; }
  }

  const nav = navigator;
  const data = {};

  data.userAgent = safe(() => nav.userAgent, null);
  data.platform = safe(() => nav.platform, null);
  data.webdriver = safe(() => nav.webdriver, null);
  data.languages = safe(() => nav.languages, null);
  data.language = safe(() => nav.language, null);
  data.hardwareConcurrency = safe(() => nav.hardwareConcurrency, null);
  data.deviceMemory = safe(() => nav.deviceMemory, null);
  data.pluginsLength = safe(() => nav.plugins && nav.plugins.length, null);
  data.mimeTypesLength = safe(() => nav.mimeTypes && nav.mimeTypes.length, null);
  data.maxTouchPoints = safe(() => nav.maxTouchPoints, null);
  data.cookieEnabled = safe(() => nav.cookieEnabled, null);

  data.timezone = safe(() => Intl.DateTimeFormat().resolvedOptions().timeZone, null);

  data.chrome = safe(() => {
    const c = window.chrome;
    return {
      exists: !!c,
      runtime: !!(c && c.runtime),
      app: !!(c && c.app),
      csi: !!(c && c.csi),
      loadTimes: !!(c && c.loadTimes),
    };
  }, null);

  data.screen = safe(() => ({
    width: screen.width,
    height: screen.height,
    availWidth: screen.availWidth,
    availHeight: screen.availHeight,
    colorDepth: screen.colorDepth,
    pixelDepth: screen.pixelDepth,
  }), null);

  data.window = safe(() => ({
    innerWidth: window.innerWidth,
    innerHeight: window.innerHeight,
    outerWidth: window.outerWidth,
    outerHeight: window.outerHeight,
    devicePixelRatio: window.devicePixelRatio,
  }), null);

  data.userAgentData = safe(() => {
    const uad = nav.userAgentData;
    if (!uad) return null;
    return {
      mobile: uad.mobile,
      platform: uad.platform,
      brands: uad.brands,
    };
  }, null);

  data.permissions = safe(() => {
    const perms = nav.permissions;
    if (!perms || !perms.query) return null;
    return { hasQuery: true };
  }, null);

  data.webgl = safe(() => {
    const canvas = document.createElement('canvas');
    const gl = canvas.getContext('webgl') || canvas.getContext('experimental-webgl');
    if (!gl) return { available: false };
    const dbg = gl.getExtension('WEBGL_debug_renderer_info');
    const vendor = dbg ? gl.getParameter(dbg.UNMASKED_VENDOR_WEBGL) : gl.getParameter(gl.VENDOR);
    const renderer = dbg ? gl.getParameter(dbg.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER);
    return { available: true, vendor, renderer };
  }, null);

  return JSON.stringify(data, null, 2);
})()
""";
    }

    public static string PrettyPrint(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }
}

