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

        /// <summary>Última versión publicada, o null si no hay ninguna utilizable. Lanza excepción si falla la red.</summary>
        public static ReleaseInfo GetLatest()
        {
            if (!Enabled) return null;
            string json;
            try { json = GetString("https://api.github.com/repos/" + BuildInfo.Repo + "/releases/latest", "application/vnd.github+json"); }
            catch (WebException ex)
            {
                // 404: todavía no hay ninguna versión publicada (o el repositorio no es público)
                HttpWebResponse resp = ex.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.NotFound) return null;
                throw;
            }
            Dictionary<string, object> o = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (o == null) return null;

            string tag = o.ContainsKey("tag_name") ? o["tag_name"] as string : null;
            Version v;
            if (tag == null || !Version.TryParse(tag.TrimStart('v', 'V'), out v)) return null;

            ReleaseInfo r = new ReleaseInfo { Version = v, Tag = tag, PageUrl = o["html_url"] as string };
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
