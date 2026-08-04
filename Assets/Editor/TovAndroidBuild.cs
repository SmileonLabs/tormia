using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Tormia.Ontology.Core;

internal static class TovAndroidBuild
{
    private const string DefaultOutputPath = "Builds/Android/TOV-Multiplayer-Test.apk";
    private const string WorldAuthoritySettingsPath =
        "Assets/Data/Ontology/Networking/WorldAuthoritySettings.asset";
    private const string Stage14UdpHost = "tov-api.punkarena.app";
    private const int Stage14UdpPort = 5273;
    [MenuItem("TOV/Build Android Multiplayer Test")]
    public static void BuildMultiplayerTestApk()
    {
        Build(DefaultOutputPath);
    }

    [MenuItem("TOV/Validation/Build Stage 13 Android IL2CPP")]
    public static void BuildStage13AndroidIl2Cpp()
    {
        ValidateAndroidPlatformContract();
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        Build($"BuildArtifacts/Stage13/TOV-Stage13-{stamp}.apk");
    }

    [MenuItem("TOV/Validation/Build Stage 14 Android UDP Gate")]
    public static void BuildStage14AndroidUdpGate()
    {
        ValidateAndroidPlatformContract();
        var settings = AssetDatabase.LoadAssetAtPath<OntologyWorldAuthoritySettings>(
            WorldAuthoritySettingsPath);
        if (settings == null)
            throw new BuildFailedException(
                $"Stage 14 settings asset was not found: {WorldAuthoritySettingsPath}");

        var snapshot = new Stage14SettingsSnapshot(settings);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var output = $"BuildArtifacts/Stage14/TOV-Stage14-{stamp}.apk";
        try
        {
            settings.enableExperimentalUdpMotionTransport = true;
            settings.androidUdpHost = Stage14UdpHost;
            settings.udpPort = Stage14UdpPort;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);

            Console.WriteLine(
                $"TOV_STAGE14_BUILD_OVERRIDE=enabled;HOST={Stage14UdpHost};" +
                $"PORT={Stage14UdpPort};ASSET={WorldAuthoritySettingsPath}");
            Build(output);
        }
        finally
        {
            // BuildPipeline may unload and recreate ScriptableObject assets.
            // Resolve the asset again instead of retaining a destroyed Unity
            // object reference across the player build boundary.
            var restoredSettings =
                AssetDatabase.LoadAssetAtPath<OntologyWorldAuthoritySettings>(
                    WorldAuthoritySettingsPath);
            if (restoredSettings == null)
                throw new BuildFailedException(
                    "Stage 14 could not reload settings for restoration.");
            snapshot.Restore(restoredSettings);
            EditorUtility.SetDirty(restoredSettings);
            AssetDatabase.SaveAssetIfDirty(restoredSettings);
            Console.WriteLine(
                "TOV_STAGE14_BUILD_OVERRIDE=restored;" +
                $"GATE={restoredSettings.enableExperimentalUdpMotionTransport};" +
                $"HOST={restoredSettings.androidUdpHost};" +
                $"PORT={restoredSettings.udpPort}");
        }
    }

    private static void ValidateAndroidPlatformContract()
    {
        if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) !=
            ScriptingImplementation.IL2CPP)
            throw new BuildFailedException(
                "Stage 13 requires the Android IL2CPP scripting backend.");
        if ((PlayerSettings.Android.targetArchitectures &
             AndroidArchitecture.ARM64) == 0)
            throw new BuildFailedException(
                "Stage 13 requires the Android ARM64 architecture.");
        if (PlayerSettings.allowedAutorotateToPortrait ||
            PlayerSettings.allowedAutorotateToPortraitUpsideDown ||
            (!PlayerSettings.allowedAutorotateToLandscapeLeft &&
             !PlayerSettings.allowedAutorotateToLandscapeRight))
            throw new BuildFailedException(
                "Stage 13 requires landscape-only Android orientation.");
        if (!PlayerSettings.Android.forceInternetPermission)
            throw new BuildFailedException(
                "Stage 13 requires Android INTERNET permission.");
    }

    private static void Build(string relativeOutputPath,
        bool cleanBuildCache = true)
    {
        // A script-only Android build can combine newly compiled MonoBehaviour
        // layouts with stale serialized scene data and produce a player that
        // crashes while preloading a scene. Release/test APKs must always
        // rebuild their player data from the current project state.
        EditorUserBuildSettings.buildScriptsOnly = false;

        var projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
        var outputPath = Path.GetFullPath(Path.Combine(projectRoot, relativeOutputPath));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

        var scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        var buildOptions = BuildOptions.Development |
                           BuildOptions.CompressWithLz4;
        if (cleanBuildCache) buildOptions |= BuildOptions.CleanBuildCache;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = buildOptions
        });

        var summary = report.summary;
        Console.WriteLine(
            $"TOV_ANDROID_BUILD_RESULT={summary.result};OUTPUT={outputPath};" +
            $"SIZE={summary.totalSize};ERRORS={summary.totalErrors};" +
            $"WARNINGS={summary.totalWarnings};TIME={summary.totalTime}");

        if (summary.result != BuildResult.Succeeded)
            throw new BuildFailedException($"TOV Android build failed: {summary.result}");
    }

    private readonly struct Stage14SettingsSnapshot
    {
        private readonly bool enableExperimentalUdpMotionTransport;
        private readonly string androidUdpHost;
        private readonly int udpPort;

        public Stage14SettingsSnapshot(OntologyWorldAuthoritySettings settings)
        {
            enableExperimentalUdpMotionTransport =
                settings.enableExperimentalUdpMotionTransport;
            androidUdpHost = settings.androidUdpHost;
            udpPort = settings.udpPort;
        }

        public void Restore(OntologyWorldAuthoritySettings settings)
        {
            settings.enableExperimentalUdpMotionTransport =
                enableExperimentalUdpMotionTransport;
            settings.androidUdpHost = androidUdpHost;
            settings.udpPort = udpPort;
        }
    }
}
