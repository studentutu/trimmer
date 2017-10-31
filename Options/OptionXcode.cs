#if UNITY_EDITOR

using sttz.Workbench.BaseOptions;
using sttz.Workbench.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace sttz.Workbench.Options
{

/// <summary>
/// Collection of patches to the XCode project generated by Unity.
/// </summary>
/// <remarks>
/// This collection of patches addresses some annoyances with the XCode project
/// generated by Unity.
/// 
/// Included are:
/// * <see cref="OptionForceDisableRemoteNotifications"/>
/// * <see cref="OptionAddEncryptionExemption"/>
/// * <see cref="OptionRenameScheme"/>
/// </remarks>
[Capabilities(OptionCapabilities.ConfiguresBuild)]
public class OptionXcode : OptionToggle
{
    protected override void Configure()
    {
        Category = "Build";

        SupportedTargets = new BuildTarget[] {
            BuildTarget.iOS
        };
    }

    /// <summary>
    /// Older versions of Unity had the issue that they mis-detected the
    /// usage of remote notifications, which would trigger an error email
    /// with iTunes Connect if the entitilement wasn't present.
    /// 
    /// This Option force disables remote notifications, only enable if
    /// you know you don't use them!
    /// </summary>
    public class OptionForceDisableRemoteNotifications : OptionToggle
    {
        protected override void Configure() { }

        override public void PostprocessBuild(BuildTarget target, string path, OptionInclusion inclusion)
        {
            base.PostprocessBuild(target, path, inclusion);

            if (!Value) return;

            var preprocessorPath = System.IO.Path.Combine(path, "Classes/Preprocessor.h");
            if (!File.Exists(preprocessorPath)) {
                Debug.LogError("Could not find Preprocessor.h at path: " + preprocessorPath);
                return;
            }

            var contents = File.ReadAllText(preprocessorPath, Encoding.UTF8);
            if (!contents.Contains("#define UNITY_USES_REMOTE_NOTIFICATIONS ")) {
                Debug.LogError("Could not find UNITY_USES_REMOTE_NOTIFICATIONS define in Preprocessor.h");
                return;
            } else if (!contents.Contains("#define UNITY_USES_REMOTE_NOTIFICATIONS 1")) {
                Debug.Log("Remote notifications already disabled, nothing to do.");
                return;
            }

            contents = contents.Replace(
                "#define UNITY_USES_REMOTE_NOTIFICATIONS 1",
                "#define UNITY_USES_REMOTE_NOTIFICATIONS 0"
            );
            File.WriteAllText(preprocessorPath, contents);

            Debug.Log("Force disabled remote notifications.");
        }
    }

    /// <summary>
    /// This Option sets `ITSAppUsesNonExemptEncryption` to `false` in
    /// the plist, indicating the app doesn't use any non-exempt encryption.
    /// Only enable this after familiarizing yourself with the export
    /// restrictions, the exemptions and the paperwork requirements.
    /// </summary>
    public class OptionAddEncryptionExemption : OptionToggle
    {
        protected override void Configure() { }

        override public void PostprocessBuild(BuildTarget target, string path, OptionInclusion inclusion)
        {
            base.PostprocessBuild(target, path, inclusion);

            if (!Value) return;

            var plistPath = System.IO.Path.Combine(path, "Info.plist");
            if (!File.Exists(plistPath)) {
                Debug.LogError("Could not find Info.plist at path: " + plistPath);
                return;
            }

            var info = new PlistDocument();
            info.ReadFromFile(plistPath);

            info.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);

            info.WriteToFile(plistPath);

            Debug.Log("Added Info.plist entry indicating the app only uses exempt encryption.");
        }
    }

    /// <summary>
    /// This Option is only cosmetical. It renames the scheme in the generated
    /// XCode project to the product name set in Unity. This will display your
    /// product name in XCode's scheme selection in the title bar and especially
    /// in the organizer side bar (instead of Unity-iPhone for all projects).
    /// </summary>
    public class OptionRenameScheme : OptionToggle
    {
        protected override void Configure() { }

        override public void PostprocessBuild(BuildTarget target, string path, OptionInclusion inclusion)
        {
            base.PostprocessBuild(target, path, inclusion);

            if (!Value) return;

            var projectPath = System.IO.Path.Combine(path, "Unity-iPhone.xcodeproj");
            if (!Directory.Exists(projectPath)) {
                Debug.LogError("Could not find Unity-iPhone.xcodeproj at path: " + projectPath);
                return;
            }

            var basePath = System.IO.Path.Combine(projectPath, "xcshareddata/xcschemes");
            var schemePath = System.IO.Path.Combine(basePath, "Unity-iPhone.xcscheme");
            if (!File.Exists(schemePath)) {
                Debug.Log("No default Unity scheme at path, possibly already renamed: " + schemePath);
                return;
            }

            var newName = System.IO.Path.Combine(basePath, Application.productName + ".xcscheme");
            File.Move(schemePath, newName);

            Debug.Log("Renamed Unity's default scheme to " + Application.productName);
        }
    }
}

}
#endif