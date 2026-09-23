using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace TecladoFlotante
{
    public class ReleaseInfo
    {
        public Version Version;
        public string Tag, PageUrl, SetupUrl, Sha256;
        public long Size;
    }

    /// <summary>
    /// Actualizaciones desde las Releases públicas de GitHub.
    /// Privacidad: solo se hace una petición GET anónima a api.github.com (y la descarga del instalador
    /// si el usuario acepta). No se envía ningún dato del usuario ni del equipo.
    /// Seguridad: la descarga debe venir del propio repositorio y su SHA-256 debe coincidir con el publicado.
    /// </summary>
    public static class Updater
    {
        public const string SetupAssetName = "TecladoFlotante-Setup.exe";

        public static bool Enabled { get { return !string.IsNullOrEmpty(BuildInfo.Repo); } }

        public static Version Current { get { return new Version(BuildInfo.Version); } }

        static string TempDir { get { return Path.Combine(Path.GetTempPath(), "TecladoFlotante-update"); } }

        static Updater()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; } // TLS 1.3
            catch (NotSupportedException) { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
        }

        /// <summary>
        /// Versión más alta publicada, o null si no hay ninguna utilizable. Lanza excepción si falla la red.
        /// Las compilaciones del canal beta también tienen en cuenta las «Pre-release»; las estables, no.
        /// </summary>
        public static ReleaseInfo GetLatest()
        {
            if (!Enabled) return null;
            string json;
            try { json = GetString("https://api.github.com/repos/" + BuildInfo.Repo + "/releases?per_page=30", "application/vnd.github+json"); }
            catch (WebException ex)
            {
                // 404: el repositorio no existe o no es público
                HttpWebResponse resp = ex.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.NotFound) return null;
                throw;
            }
            object[] releases = new JavaScriptSerializer().DeserializeObject(json) as object[];
            if (releases == null) return null;

            Dictionary<string, object> best = null;
            Version bestVersion = null;
            foreach (object item in releases)
            {
                Dictionary<string, object> rel = item as Dictionary<string, object>;
                if (rel == null || IsTrue(rel, "draft")) continue;
                if (IsTrue(rel, "prerelease") && !BuildInfo.IsBeta) continue;
                Version rv = ParseTag(rel.ContainsKey("tag_name") ? rel["tag_name"] as string : null);
                if (rv != null && (bestVersion == null || rv > bestVersion)) { best = rel; bestVersion = rv; }
            }
            return best == null ? null : ToReleaseInfo(best, bestVersion);
        }

        static bool IsTrue(Dictionary<string, object> o, string key)
        {
            return o.ContainsKey(key) && o[key] is bool && (bool)o[key];
        }

        /// <summary>"v1.2.0" o "v1.2.0-beta" → 1.2.0 (el sufijo solo es informativo).</summary>
        static Version ParseTag(string tag)
        {
            if (tag == null) return null;
            string s = tag.TrimStart('v', 'V');
            int dash = s.IndexOf('-');
            if (dash >= 0) s = s.Substring(0, dash);
            Version v;
            return Version.TryParse(s, out v) ? v : null;
        }

        static ReleaseInfo ToReleaseInfo(Dictionary<string, object> o, Version v)
        {
            ReleaseInfo r = new ReleaseInfo { Version = v, Tag = o["tag_name"] as string, PageUrl = o["html_url"] as string };
            string shaUrl = null;
            object[] assets = o.ContainsKey("assets") ? o["assets"] as object[] : null;
            if (assets != null)
                foreach (object a in assets)
                {
                    Dictionary<string, object> ad = a as Dictionary<string, object>;
                    if (ad == null) continue;
                    string name = ad["name"] as string;
                    if (name == SetupAssetName)
                    {
                        r.SetupUrl = ad["browser_download_url"] as string;
                        r.Size = Convert.ToInt64(ad["size"]);
                        string digest = ad.ContainsKey("digest") ? ad["digest"] as string : null;
                        if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                            r.Sha256 = digest.Substring(7).ToLowerInvariant();
                    }
                    else if (name == SetupAssetName + ".sha256")
                        shaUrl = ad["browser_download_url"] as string;
                }

            if (r.Sha256 == null && shaUrl != null && IsFromRepo(shaUrl))
                r.Sha256 = GetString(shaUrl, "application/octet-stream").Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();

            return r.SetupUrl != null && r.Sha256 != null && r.Sha256.Length == 64 ? r : null;
        }

        public static bool IsNewer(ReleaseInfo r) { return r != null && r.Version > Current; }

        /// <summary>Descarga y verifica el instalador. Devuelve la ruta al archivo verificado.</summary>
        public static string Download(ReleaseInfo r)
        {
            if (!IsFromRepo(r.SetupUrl)) throw new InvalidDataException("La descarga no procede del repositorio oficial.");
            Directory.CreateDirectory(TempDir);
            string path = Path.Combine(TempDir, "TecladoFlotante-Setup-" + r.Version + ".exe");
            using (WebClient wc = NewClient("application/octet-stream"))
                wc.DownloadFile(r.SetupUrl, path);

            long size = new FileInfo(path).Length;
            string hash = Sha256Of(path);
            if ((r.Size > 0 && size != r.Size) || !string.Equals(hash, r.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(path);
                throw new InvalidDataException("El archivo descargado no coincide con la huella publicada (SHA-256). No se ha instalado nada.");
            }
            return path;
        }

        /// <summary>Lanza el instalador verificado; este cierra la versión actual, se instala y la vuelve a abrir.</summary>
        public static void Launch(string setupPath, bool startHidden)
        {
            Process.Start(new ProcessStartInfo(setupPath, "--update" + (startHidden ? " --hidden" : "")) { UseShellExecute = false });
        }

        public static void CleanupDownloads()
        {
            try { if (Directory.Exists(TempDir)) Directory.Delete(TempDir, true); } catch { }
        }

        static bool IsFromRepo(string url)
        {
            return url != null && url.StartsWith("https://github.com/" + BuildInfo.Repo + "/releases/download/", StringComparison.OrdinalIgnoreCase);
        }

        static WebClient NewClient(string accept)
        {
            WebClient wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "TecladoFlotante/" + BuildInfo.Version;
            wc.Headers[HttpRequestHeader.Accept] = accept;
            wc.Encoding = Encoding.UTF8;
            return wc;
        }

        static string GetString(string url, string accept)
        {
            using (WebClient wc = NewClient(accept)) return wc.DownloadString(url);
        }

        static string Sha256Of(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        static void TryDelete(string path) { try { File.Delete(path); } catch { } }
    }
}
