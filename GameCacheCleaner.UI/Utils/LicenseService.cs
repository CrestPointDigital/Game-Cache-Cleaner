using System;
using System.IO;
using System.Security.Cryptography;

namespace GameCacheCleaner.UI
{
    public static class LicenseService
    {
        // TODO: replace with your Stripe/Worker public key (ES256) when ready
        private const string PublicKeyPem = @"-----BEGIN PUBLIC KEY-----
...your ES256 public key here...
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
                    return VerifyToken(token);
                }
                catch { return false; }
            }
        }

        public static bool ProEnabled => IsLicensed;

        public static bool Activate(string token)
        {
            if (!VerifyToken(token)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(LicensePath)!);
            File.WriteAllText(LicensePath, token);
            return true;
        }

        private static bool VerifyToken(string token)
        {
            // Token format: base64url(payload) + "." + base64url(derSignature)
            var parts = token.Split('.');
            if (parts.Length != 2) return false;
            byte[] payload = FromB64Url(parts[0]);
            byte[] sig = FromB64Url(parts[1]);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(PublicKeyPem);
            return ecdsa.VerifyData(payload, sig, HashAlgorithmName.SHA256);
        }

        private static byte[] FromB64Url(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }
    }
}
