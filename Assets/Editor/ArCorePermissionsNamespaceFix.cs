using System.IO;
using System.IO.Compression;
using UnityEditor.Android;
using UnityEngine;

namespace TalesTensor.EditorTools
{
    /// <summary>
    /// Works around a packaging bug in the ARCore XR Plugin (com.unity.xr.arcore):
    /// both <c>arcore_client.aar</c> and <c>unityandroidpermissions.aar</c> declare the
    /// same manifest package <c>com.google.ar.core</c>. Android Gradle Plugin 8+ (Unity 6)
    /// derives each module's namespace from that package and rejects the duplicate with:
    ///
    ///   Namespace 'com.google.ar.core' is used in multiple modules and/or libraries:
    ///   :arcore_client:, :unityandroidpermissions:
    ///
    /// The permissions helper has an empty manifest (no components, no resources, no R
    /// class), so renaming its package is safe — its Java classes are unaffected. This
    /// hook runs after Unity generates the Gradle project but before Gradle builds, and
    /// rewrites the freshly-copied AAR's package to a unique value. Because it patches the
    /// generated copy each build, it survives package reimports/updates (unlike editing
    /// the immutable PackageCache).
    /// </summary>
    public class ArCorePermissionsNamespaceFix : IPostGenerateGradleAndroidProject
    {
        const string AarName = "unityandroidpermissions.aar";
        const string OldPackage = "package=\"com.google.ar.core\"";
        const string NewPackage = "package=\"com.google.ar.core.unityandroidpermissions\"";

        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // `path` is the unityLibrary module; search it (and the wider project) for the
            // copied AAR so we're robust to layout changes across Unity versions.
            string root = Directory.GetParent(path)?.FullName ?? path;
            string[] matches = Directory.GetFiles(root, AarName, SearchOption.AllDirectories);
            if (matches.Length == 0)
            {
                Debug.LogWarning($"[ArCoreFix] {AarName} not found under {root}; nothing to patch.");
                return;
            }

            foreach (string aar in matches)
                PatchAar(aar);
        }

        static void PatchAar(string aarPath)
        {
            // An AAR is a zip. We must NOT edit it in place with ZipArchive.Update — that
            // writes entries with data descriptors that Jetifier/Java reject ("invalid
            // entry size"). Instead fully extract it and re-zip with CreateFromDirectory,
            // which writes clean local headers. Re-zipping every build also self-heals a
            // copy a previous (buggy) edit may have corrupted.
            string dir = Path.GetDirectoryName(aarPath);
            string extractDir = Path.Combine(dir, "uap_namespace_tmp");
            string tempAar = aarPath + ".patched";

            try
            {
                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                if (File.Exists(tempAar)) File.Delete(tempAar);

                ZipFile.ExtractToDirectory(aarPath, extractDir);

                string manifestPath = Path.Combine(extractDir, "AndroidManifest.xml");
                if (!File.Exists(manifestPath)) return;

                string xml = File.ReadAllText(manifestPath);
                bool hasOld = xml.Contains(OldPackage);
                bool hasNew = xml.Contains(NewPackage);
                if (!hasOld && !hasNew) return; // not the AAR we expect — leave it alone

                if (hasOld)
                    File.WriteAllText(manifestPath, xml.Replace(OldPackage, NewPackage));

                ZipFile.CreateFromDirectory(extractDir, tempAar);
                File.Delete(aarPath);
                File.Move(tempAar, aarPath);

                Debug.Log($"[ArCoreFix] Rebuilt {Path.GetFileName(aarPath)} with a unique namespace " +
                          $"({(hasOld ? "patched" : "already patched; re-zipped cleanly")}).");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ArCoreFix] Failed to patch {aarPath}: {e.Message}");
            }
            finally
            {
                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                if (File.Exists(tempAar)) File.Delete(tempAar);
            }
        }
    }
}
