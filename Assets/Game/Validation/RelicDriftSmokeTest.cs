using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Idol.Unity;
using UnityEngine;

namespace RelicDrift
{
    /// <summary>Opt-in development-player checks against the actual shipping game and renderer.</summary>
    public sealed class RelicDriftSmokeTest : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly List<string> passed = new List<string>();
        private readonly List<string> runtimeErrors = new List<string>();
        private string output;
        private bool active;
        private Exception captureFailure;
        private bool restoredPrefs, hadBest, hadMute;
        private int savedBest, savedMute;
        private bool encounterPassed;
        private int capturedGaussianCount;
        private const string BestKey = "RelicDrift.HighScore.v1";
        private const string MuteKey = "RelicDrift.Muted";

        private void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            active = Array.IndexOf(args, "--smoke-test") >= 0;
            if (!active) return;
            hadBest = PlayerPrefs.HasKey(BestKey);
            hadMute = PlayerPrefs.HasKey(MuteKey);
            savedBest = PlayerPrefs.GetInt(BestKey, 0);
            savedMute = PlayerPrefs.GetInt(MuteKey, 0);
            var index = Array.IndexOf(args, "--smoke-output");
            output = index >= 0 && index + 1 < args.Length
                ? Path.GetFullPath(args[index + 1])
                : Path.Combine(Application.persistentDataPath, "SmokeTest");
            Directory.CreateDirectory(output);
            Application.logMessageReceived += OnLog;
        }

        private IEnumerator Start()
        {
            if (!active) yield break;
            // RelicDriftGame.Start constructs the real camera, UGUI, audio and Idol world.
            yield return null;
            yield return null;
            var splashDeadline = Time.realtimeSinceStartup + 10;
            while (!UnityEngine.Rendering.SplashScreen.isFinished && Time.realtimeSinceStartup < splashDeadline)
                yield return null;
            var game = GetComponent<RelicDriftGame>();
            Exception failure = null;
            try
            {
                Check(game != null, "game controller exists");
                Check(game.IsReady, "all three original Idol models loaded");
                Check(game.State == RunPhase.Title, "starts at title");
                AuditWorld();
            }
            catch (Exception ex) { failure = ex; }
            if (failure != null) { Finish(game, failure); yield break; }

            yield return Capture("title.png");
            try
            {
                game.DebugStartRun();
                Check(game.State == RunPhase.Playing && game.Score == 0 && game.Lives == 3,
                    "start resets score and lives");
                Check(Mathf.Abs(game.SecondsRemaining - 60) < 1, "run starts with sixty seconds");
            }
            catch (Exception ex) { failure = ex; }
            if (failure != null) { Finish(game, failure); yield break; }

            // Let real obstacle/collectible movement and render callbacks run before capture.
            yield return new WaitForSecondsRealtime(2.2f);
            // An unattended player can lose OS focus. Pause itself is verified explicitly below.
            if (game.State == RunPhase.Paused) game.DebugTogglePause();
            yield return null;
            yield return Capture("playing.png");
            game.DebugStartRun();
            yield return FollowSpawnedActor(game, true);
            try { Check(encounterPassed, "moving into a spawned crystal lane triggers a real pickup"); }
            catch (Exception ex) { failure = ex; }
            if (failure != null) { Finish(game, failure); yield break; }

            game.DebugStartRun();
            yield return FollowSpawnedActor(game, false);
            try { Check(encounterPassed, "moving into a spawned rock lane triggers a real collision"); }
            catch (Exception ex) { failure = ex; }
            if (failure != null) { Finish(game, failure); yield break; }

            try
            {
                AuditWorld();
                Check(captureFailure == null, "screenshots captured without errors");
                Check(capturedGaussianCount > 0, "GPU submitted original Gaussians");
                game.DebugStartRun();
                game.DebugCollect();
                Check(game.Score == 100, "collect adds one hundred points");
                for (var i = 0; i < 4; i++) game.DebugCollect();
                Check(game.Score == 500, "first five crystals score five hundred points");
                game.DebugCollect();
                Check(game.Score == 700, "sixth consecutive crystal receives double score");
                game.DebugHit();
                Check(game.Lives == 2, "collision removes one shield");
                game.DebugCollect();
                Check(game.Score == 800, "collision resets score multiplier");
                game.DebugTogglePause();
                Check(game.State == RunPhase.Paused, "pause changes state");
                var timer = game.SecondsRemaining;
                game.DebugAdvanceTime(2);
                Check(Mathf.Abs(game.SecondsRemaining - timer) < .001f, "pause freezes game clock");
                game.DebugTogglePause();
                Check(game.State == RunPhase.Playing, "resume restores playing state");
                game.DebugAdvanceTime(61);
                Check(game.State == RunPhase.Completed, "surviving sixty seconds completes run");
                game.DebugStartRun();
                game.DebugHit(); game.DebugHit(); game.DebugHit();
                Check(game.State == RunPhase.GameOver && game.Lives == 0, "three hits end run");
                game.DebugStartRun();
                Check(game.State == RunPhase.Playing && game.Score == 0 && game.Lives == 3,
                    "restart restores a clean run");
                Check(game.HighScore >= 800, "best score persists across runs");
                Check(runtimeErrors.Count == 0, "no runtime errors or exceptions");
                Check(File.Exists(Path.Combine(output, "title.png")) && File.Exists(Path.Combine(output, "playing.png")),
                    "title and gameplay screenshots written");
            }
            catch (Exception ex) { failure = ex; }
            if (failure == null)
            {
                game.DebugTogglePause();
                yield return Capture("paused.png");
                game.DebugTogglePause();
                game.DebugAdvanceTime(61);
                yield return Capture("completed.png");
                game.DebugStartRun();
                game.DebugHit(); game.DebugHit(); game.DebugHit();
                yield return Capture("game-over.png");
                game.DebugStartRun();
                try
                {
                    Check(captureFailure == null && runtimeErrors.Count == 0,
                        "pause and both result dialogs captured without runtime errors");
                }
                catch (Exception ex) { failure = ex; }
            }
            Finish(game, failure);
        }

        private IEnumerator FollowSpawnedActor(RelicDriftGame game, bool crystal)
        {
            encounterPassed = false;
            var wanted = crystal ? game.CrystalAsset : game.RockAsset;
            Transform target = null;
            var deadline = Time.realtimeSinceStartup + 7;
            while (target == null && Time.realtimeSinceStartup < deadline)
            {
                if (game.State == RunPhase.Paused) game.DebugTogglePause();
                foreach (var renderer in FindObjectsByType<IdolGaussianRenderer>(FindObjectsSortMode.None))
                {
                    var candidate = renderer.transform.parent;
                    if (renderer.asset == wanted && candidate != null && candidate.position.z > 1)
                    {
                        target = candidate;
                        break;
                    }
                }
                yield return null;
            }
            if (target == null) yield break;
            var selectedLane = target.position.x < -.1f ? 0 : target.position.x > .1f ? 2 : 1;
            game.SelectLane(selectedLane);
            while ((game.State == RunPhase.Playing || game.State == RunPhase.Paused) && Time.realtimeSinceStartup < deadline)
            {
                if (game.State == RunPhase.Paused) game.DebugTogglePause();
                if ((crystal && game.Score > 0) || (!crystal && game.Lives < 3))
                {
                    encounterPassed = true;
                    yield break;
                }
                yield return null;
            }
        }

        private IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            Texture2D texture = null;
            RenderTexture target = null;
            var camera = Camera.main;
            var previousTarget = camera == null ? null : camera.targetTexture;
            var previousMask = camera == null ? 0 : camera.cullingMask;
            var previousActive = RenderTexture.active;
            var overlays = new List<Canvas>();
            var originalLayers = new List<KeyValuePair<GameObject, int>>();
            Camera overlayCamera = null;
            try
            {
                if (camera == null) throw new InvalidOperationException("No game camera for smoke capture.");
                // Windows can skip the swap chain of a hidden unattended player. Render the actual
                // game camera and existing HUD into an explicit target; keep all world actors/data.
                target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target;
                overlayCamera = new GameObject("Smoke capture UI camera", typeof(Camera)).GetComponent<Camera>();
                overlayCamera.enabled = false;
                overlayCamera.CopyFrom(camera);
                overlayCamera.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
                overlayCamera.clearFlags = CameraClearFlags.Depth;
                overlayCamera.cullingMask = 1 << 31;
                overlayCamera.targetTexture = target;
                camera.cullingMask &= ~(1 << 31);
                foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    overlays.Add(canvas);
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = overlayCamera;
                    canvas.planeDistance = .5f;
                    foreach (var child in canvas.GetComponentsInChildren<Transform>(true))
                    {
                        originalLayers.Add(new KeyValuePair<GameObject, int>(child.gameObject, child.gameObject.layer));
                        child.gameObject.layer = 31;
                    }
                }
                Canvas.ForceUpdateCanvases();
                camera.Render();
                capturedGaussianCount = Mathf.Max(capturedGaussianCount, IdolGaussianRenderer.LastRenderedGaussianCount);
                // Match ScreenSpaceOverlay: draw the existing UI after every world Gaussian.
                overlayCamera.Render();
                RenderTexture.active = target;
                texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false);
                texture.Apply(false, false);
                File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
            }
            catch (Exception ex) { captureFailure = ex; }
            finally
            {
                foreach (var canvas in overlays)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.worldCamera = null;
                }
                foreach (var pair in originalLayers)
                    if (pair.Key != null) pair.Key.layer = pair.Value;
                if (camera != null)
                {
                    camera.targetTexture = previousTarget;
                    camera.cullingMask = previousMask;
                }
                if (overlayCamera != null) Destroy(overlayCamera.gameObject);
                RenderTexture.active = previousActive;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (texture != null) Destroy(texture);
                Canvas.ForceUpdateCanvases();
            }
        }

        private void AuditWorld()
        {
            Check(FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Length == 0 &&
                  FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Length == 0 &&
                  FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Length == 0,
                  "world has no conventional 3D meshes");
            var splats = FindObjectsByType<IdolGaussianRenderer>(FindObjectsSortMode.None);
            Check(splats.Length > 0, "world contains Idol Gaussian renderers");
            foreach (var renderer in splats)
                Check(renderer.asset != null && renderer.asset.Count >= 32768 && renderer.shader != null,
                      "world actor uses full original Idol asset");
        }

        private void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            if (!passed.Contains(description)) passed.Add(description);
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                runtimeErrors.Add(message + "\n" + stack);
        }

        private void Finish(RelicDriftGame game, Exception failure)
        {
            var actors = FindObjectsByType<IdolGaussianRenderer>(FindObjectsSortMode.None);
            var count = 0;
            foreach (var renderer in actors)
                if (renderer.isActiveAndEnabled && renderer.asset != null) count += renderer.asset.Count;
            RestorePrefs();
            var preferencesRestored = PlayerPrefs.HasKey(BestKey) == hadBest &&
                                      (!hadBest || PlayerPrefs.GetInt(BestKey) == savedBest) &&
                                      PlayerPrefs.HasKey(MuteKey) == hadMute &&
                                      (!hadMute || PlayerPrefs.GetInt(MuteKey) == savedMute);
            if (preferencesRestored) passed.Add("user best-score and mute preferences restored");
            else if (failure == null) failure = new InvalidOperationException("Failed to restore user preferences.");
            var result = new Result
            {
                success = failure == null && runtimeErrors.Count == 0,
                unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                utc = DateTime.UtcNow.ToString("O"),
                passed = passed.ToArray(),
                runtimeErrors = runtimeErrors.ToArray(),
                failure = failure?.ToString() ?? "",
                visibleGaussianCount = count,
                submittedGaussianCount = capturedGaussianCount,
                activeSplatActors = actors.Length,
                userPreferencesRestored = preferencesRestored,
                finalState = game == null ? "missing" : game.State.ToString()
            };
            File.WriteAllText(Path.Combine(output, "smoke-result.json"), JsonUtility.ToJson(result, true));
            Debug.Log("RELIC_DRIFT_SMOKE_" + (result.success ? "OK" : "FAILED") + "\n" + JsonUtility.ToJson(result, true));
            Application.logMessageReceived -= OnLog;
            Application.Quit(result.success ? 0 : 1);
        }

        private void RestorePrefs()
        {
            if (!active || restoredPrefs) return;
            if (hadBest) PlayerPrefs.SetInt(BestKey, savedBest); else PlayerPrefs.DeleteKey(BestKey);
            if (hadMute) PlayerPrefs.SetInt(MuteKey, savedMute); else PlayerPrefs.DeleteKey(MuteKey);
            PlayerPrefs.Save();
            restoredPrefs = true;
        }

        private void OnApplicationQuit() { RestorePrefs(); }
        private void OnDestroy() { RestorePrefs(); }

        [Serializable]
        private sealed class Result
        {
            public bool success, userPreferencesRestored;
            public string unityVersion, graphicsDevice, graphicsApi, utc, failure, finalState;
            public string[] passed, runtimeErrors;
            public int visibleGaussianCount, submittedGaussianCount, activeSplatActors;
        }
#endif
    }
}
