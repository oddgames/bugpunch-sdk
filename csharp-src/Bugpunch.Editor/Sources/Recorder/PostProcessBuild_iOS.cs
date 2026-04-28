#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace ODDGames.Recorder.Editor
{
    public static class PostProcessBuild_iOS
    {
        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;

            string projPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projPath);

            string frameworkTarget = project.GetUnityFrameworkTargetGuid();

            project.AddFrameworkToProject(frameworkTarget, "AVFoundation.framework", false);
            project.AddFrameworkToProject(frameworkTarget, "VideoToolbox.framework", false);
            project.AddFrameworkToProject(frameworkTarget, "CoreMedia.framework", false);
            project.AddFrameworkToProject(frameworkTarget, "ReplayKit.framework", false);

            project.WriteToFile(projPath);
        }
    }
}
#endif
