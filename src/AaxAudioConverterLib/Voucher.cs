using System;
using System.IO;
using Newtonsoft.Json.Linq;
using audiamus.aux.ex;
using static audiamus.aux.Logging;

namespace audiamus.aaxconv.lib {

  // Resolves the per-file AAXC decryption key/iv from the companion voucher that
  // audible-cli (".voucher") or other downloaders (".json") store next to an
  // ".aaxc" file. The voucher is JSON with the key/iv under
  //   content_license.license_response.{key,iv}
  // Results are cached per voucher path. Key/iv are never logged.
  static class Voucher {
    public const string EXT_VOUCHER = ".voucher";
    public const string EXT_JSON = ".json";

    private static readonly System.Collections.Generic.Dictionary<string, (string key, string iv)> __cache
      = new System.Collections.Generic.Dictionary<string, (string, string)> (StringComparer.OrdinalIgnoreCase);
    private static readonly object __lock = new object ();

    public static bool TryGet (string aaxcPath, out string key, out string iv) {
      key = null;
      iv = null;
      if (aaxcPath.IsNullOrWhiteSpace ())
        return false;

      string voucherPath = findVoucher (aaxcPath);
      if (voucherPath is null) {
        Log (2, typeof (Voucher), () => $"no voucher next to \"{aaxcPath.SubstitUser ()}\"");
        return false;
      }

      lock (__lock) {
        if (__cache.TryGetValue (voucherPath, out var cached)) {
          key = cached.key;
          iv = cached.iv;
          return key != null;
        }

        bool ok = parse (voucherPath, out key, out iv);
        __cache[voucherPath] = ok ? (key, iv) : (null, null);
        return ok;
      }
    }

    private static string findVoucher (string aaxcPath) {
      try {
        string dir = Path.GetDirectoryName (aaxcPath);
        string stub = Path.GetFileNameWithoutExtension (aaxcPath);
        string baseNoExt = Path.Combine (dir, stub);
        string v = baseNoExt + EXT_VOUCHER;
        if (File.Exists (v))
          return v;
        string j = baseNoExt + EXT_JSON;
        if (File.Exists (j))
          return j;
      } catch (Exception exc) {
        Log (1, typeof (Voucher), () => exc.ToShortString ());
      }
      return null;
    }

    private static bool parse (string voucherPath, out string key, out string iv) {
      key = null;
      iv = null;
      try {
        var root = JObject.Parse (File.ReadAllText (voucherPath));
        var lr = root["content_license"]?["license_response"];
        key = lr?["key"]?.ToString ();
        iv = lr?["iv"]?.ToString ();
        bool ok = !key.IsNullOrWhiteSpace () && !iv.IsNullOrWhiteSpace ();
        if (!ok)
          Log (2, typeof (Voucher), () => $"voucher has no key/iv: \"{voucherPath.SubstitUser ()}\"");
        else
          Log (3, typeof (Voucher), () => $"voucher key/iv loaded: \"{voucherPath.SubstitUser ()}\"");
        return ok;
      } catch (Exception exc) {
        Log (1, typeof (Voucher), () => exc.ToShortString ());
        key = null;
        iv = null;
        return false;
      }
    }
  }
}
