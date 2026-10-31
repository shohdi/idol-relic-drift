using System;
using System.IO;
using System.Linq;
using Idol.Unity;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RelicDrift.Editor
{
    /// <summary>Rebuilds the one bootstrap scene from the three original Idol-generated assets.</summary>
    public static class RelicDriftBuild
    {
        public const string ScenePath = "Assets/Game/Scenes/RelicDrift.unity";
        private static readonly string[] ModelNames = { "courier", "sun-crystal", "void-rock" };
        private static readonly string[] ForbiddenModelExtensions =
            { ".fbx", ".obj", ".blend", ".dae", ".3ds", ".glb", ".gltf", ".stl", ".ply" };

        [MenuItem("Relic Drift/1. Set up game")]
        public static void Setup()
        {
            ConfigurePlayer();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Idol/Runtime/IdolGaussian.shader");
            Require(shader != null, "Missing Idol/GaussianSplat shader.");
            IncludeShader(shader);

            Directory.CreateDirectory(Path.Combine(ProjectRoot, Path.GetDirectoryName(ScenePath)));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Imports may release unused assets. Finish all imports before retaining subasset references.
            foreach (var name in ModelNames)
            {
                var path = "Assets/Game/Art/Splats/" + name + ".idolply";
                Require(File.Exists(Path.Combine(ProjectRoot, path)), "Missing generated model " + path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
            var assets = ModelNames.Select(LoadSplat).ToArray();
            var bootstrap = new GameObject("Relic Drift");
            var game = bootstrap.AddComponent<RelicDriftGame>();
            game.CourierAsset = assets[0];
            game.CrystalAsset = assets[1];
            game.RockAsset = assets[2];
            game.GaussianShader = shader;
            bootstrap.AddComponent<RelicDriftSmokeTest>();
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene, ScenePath), "Could not save game scene.");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Audit(scene, assets);
            AssetDatabase.SaveAssets();
            Debug.Log("RELIC_DRIFT_SETUP_OK — Three Idol assets, one bootstrap, no modeled 3D assets.");
        }

        [MenuItem("Relic Drift/2. Validate project")]
        public static void ValidateProject()
        {
            Setup();
            var report = new ValidationReport
            {
                unityVersion = Application.unityVersion,
                scene = ScenePath,
                modelNames = ModelNames,
                gaussianCounts = ModelNames.Select(name => LoadSplat(name).Count).ToArray(),
                importedMeshCount = AssetDatabase.FindAssets("t:Mesh", new[] { "Assets" }).Length,
                passed = true,
                utc = DateTime.UtcNow.ToString("O")
            };
            var output = Path.Combine(ProjectRoot, "Builds", "Checks");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "project-validation.json"), JsonUtility.ToJson(report, true));
            Debug.Log("RELIC_DRIFT_VALIDATION_OK\n" + JsonUtility.ToJson(report, true));
        }

        [MenuItem("Relic Drift/3. Build Windows game")]
        public static void Build()
        {
            ValidateProject();
            var development = string.Equals(Environment.GetEnvironmentVariable("RELIC_DRIFT_DEVELOPMENT"), "1", StringComparison.Ordinal);
            var output = Environment.GetEnvironmentVariable("RELIC_DRIFT_BUILD_PATH");
            if (string.IsNullOrWhiteSpace(output)) output = Path.Combine(ProjectRoot, "Builds", "Windows", "RelicDrift.exe");
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = development ? BuildOptions.Development : BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"RELIC_DRIFT_BUILD_{summary.result}: {summary.totalSize} bytes, {summary.totalTime}, {summary.totalErrors} errors. {output}");
            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Relic Drift Windows build failed; inspect the Unity log.");
        }

        private static IdolGaussianAsset LoadSplat(string name)
        {
            var path = "Assets/Game/Art/Splats/" + name + ".idolply";
            Require(File.Exists(Path.Combine(ProjectRoot, path)), "Missing generated model " + path + ". Generate the Idol assets before setup/build.");
            var asset = AssetDatabase.LoadAllAssetsAtPath(path).OfType<IdolGaussianAsset>().SingleOrDefault();
            Require(asset != null, "The Idol importer did not create Gaussian data for " + path);
            Require(asset.Count >= 32768, path + " must contain at least 32,768 original Gaussians.");
            Require(asset.scales != null && asset.scales.Length == asset.Count &&
                    asset.rotations != null && asset.rotations.Length == asset.Count &&
                    asset.colors != null && asset.colors.Length == asset.Count, "Mismatched Gaussian arrays in " + path);
            for (var i = 0; i < asset.Count; i++)
            {
                var p = asset.positions[i];
                var s = asset.scales[i];
                Require(Finite(p.x) && Finite(p.y) && Finite(p.z) &&
                        Finite(s.x) && Finite(s.y) && Finite(s.z) && s.x > 0 && s.y > 0 && s.z > 0,
                        "Invalid Gaussian position/scale in " + path + " at " + i);
            }
            return asset;
        }

        private static void Audit(Scene scene, IdolGaussianAsset[] assets)
        {
            var assetRoot = Path.Combine(ProjectRoot, "Assets");
            foreach (var path in Directory.GetFiles(assetRoot, "*", SearchOption.AllDirectories))
                Require(!ForbiddenModelExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()),
                    "Only original .idolply models are permitted; found " + path);
            Require(Directory.GetFiles(assetRoot, "*.idolply", SearchOption.AllDirectories).Length == 3,
                "Expected exactly three Idol-generated 3D model files.");
            Require(AssetDatabase.FindAssets("t:Mesh", new[] { "Assets" }).Length == 0,
                "A conventional mesh asset was imported. The world must use Idol splats only.");
            var roots = scene.GetRootGameObjects();
            Require(roots.Length == 1, "The bootstrap scene must have exactly one root.");
            Require(roots[0].GetComponent<RelicDriftGame>() != null, "Missing runtime game controller.");
            Require(roots[0].GetComponentsInChildren<MeshRenderer>(true).Length == 0 &&
                    roots[0].GetComponentsInChildren<MeshFilter>(true).Length == 0 &&
                    roots[0].GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0,
                    "The bootstrap scene contains a modeled 3D object.");
            Require(assets.All(a => a != null), "Missing original Idol splat assets.");
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Shohdi";
            PlayerSettings.productName = "Relic Drift";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.shohdi.relicdrift");
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            EditorSettings.serializationMode = SerializationMode.ForceText;
            QualitySettings.vSyncCount = 0;
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input = settings.FindProperty("activeInputHandler");
            if (input != null) input.intValue = 0;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void IncludeShader(Shader shader)
        {
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var included = settings.FindProperty("m_AlwaysIncludedShaders");
            Require(included != null, "Cannot find always-included shaders in Graphics Settings.");
            for (var i = 0; i < included.arraySize; i++)
                if (included.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            var index = included.arraySize;
            included.InsertArrayElementAtIndex(index);
            included.GetArrayElementAtIndex(index).objectReferenceValue = shader;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        [Serializable]
        private sealed class ValidationReport
        {
            public string unityVersion, scene, utc;
            public string[] modelNames;
            public int[] gaussianCounts;
            public int importedMeshCount;
            public bool passed;
        }
    }
}
