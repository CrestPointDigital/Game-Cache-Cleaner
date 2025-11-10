using System;
using System.IO;
using System.Security.Cryptography;

namespace GameCacheCleaner.UI
{
    public static class LicenseService
    {
        // Public key used to verify license tokens (ES256). Prefer reading from Assets/public.pem.
        private const string PublicKeyPemFallback = @"-----BEGIN PUBLIC KEY-----
...paste-your-ES256-public-key-here...
-----END PUBLIC KEY-----";

        private static string LocalApp => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        private static string LicensePath => Path.Combine(ProgramData, "CrestPoint", "GCC", "license.json");

        public static bool IsLicensed
        {
            get
            {
                try
                {
                    if (!File.Exists(LicensePath)) return false;
                    var token = File.ReadAllText(LicensePath).Trim(); // payloadBase64Url + "." + sigBase64Url
                    var pub = GetPublicKeyPem();
                    return VerifyToken(token, pub);
                }
                catch { return false; }
            }
        }

        public static bool ProEnabled => IsLicensed;

        public static bool Activate(string token)
        {
            var pub = GetPublicKeyPem();
            if (!VerifyToken(token, pub)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(LicensePath)!);
            File.WriteAllText(LicensePath, token);
            return true;
        }

        public static string PaymentLinkUrl { get; } = "https://buy.stripe.com/test_REPLACE_WITH_LINK"; // TODO: replace with your Stripe Payment Link (TEST/live)

        private static string GetPublicKeyPem()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "public.pem");
                if (File.Exists(path)) return File.ReadAllText(path);
            }
            catch { }
            return PublicKeyPemFallback;
        }

        private static bool VerifyToken(string token, string publicKeyPem)
        {
            // Token format: base64url(payload) + "." + base64url(derSignature)
            var parts = token.Split('.');
            if (parts.Length != 2) return false;
            byte[] payload = FromB64Url(parts[0]);
            byte[] sig = FromB64Url(parts[1]);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            var ok = ecdsa.VerifyData(payload, sig, HashAlgorithmName.SHA256);
            if (!ok) return false;
            // Optional: validate payload fields (product, issuedAt)
            try
            {
                var json = System.Text.Encoding.UTF8.GetString(payload);
                if (json.Contains("\"product\":\"gcc-pro\"")) return true;
                return false;
            }
            catch { return false; }
        }

        private static byte[] FromB64Url(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }
    }
}
