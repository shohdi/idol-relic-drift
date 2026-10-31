// Adapted from Idol Studio: https://github.com/shohdi/idol-csharp (Apache-2.0).
// Models remain the complete Idol-generated Gaussian data. These six-vertex
// procedural quads are the renderer for the Gaussians, not modeled game assets.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Idol.Unity
{
    [ExecuteAlways]
    public sealed class IdolGaussianRenderer : MonoBehaviour
    {
        public IdolGaussianAsset asset;
        public Shader shader;
        [Range(0f, 3f)] public float opacity = 1f;

        public static int ActiveInstanceCount => Instances.Count;
        public static int LastRenderedInstanceCount { get; private set; }
        public static int LastRenderedGaussianCount { get; private set; }
        public static int LastSortCount { get; private set; }

        private static readonly HashSet<IdolGaussianRenderer> Instances = new HashSet<IdolGaussianRenderer>();
        private static readonly Dictionary<IdolGaussianAsset, AssetBuffers> Assets = new Dictionary<IdolGaussianAsset, AssetBuffers>();
        private static readonly Dictionary<Shader, Material> Materials = new Dictionary<Shader, Material>();
        private static readonly HashSet<int> ReportedProblems = new HashSet<int>();
        private static readonly List<DrawItem> Draws = new List<DrawItem>(32);
        private static readonly Plane[] Frustum = new Plane[6];
        private static MaterialPropertyBlock properties;
        private static readonly Comparison<DrawItem> CompareDraws = (a, b) => a.depth.CompareTo(b.depth);
        private static CommandBuffer commands;
        private static bool installed, rendering;
        private static long renderPass;

        private static int splatsId, orderId, objectToWorldId, viewId, projectionId, viewportId, opacityId;

        [StructLayout(LayoutKind.Sequential)]
        private struct Splat
        {
            public Vector4 positionOpacity, scale, rotation, color;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] public float number;
            [FieldOffset(0)] public uint bits;
        }

        private struct DrawItem
        {
            public IdolGaussianRenderer renderer;
            public AssetBuffers buffers;
            public Material material;
            public Matrix4x4 matrix;
            public float depth;
        }

        private sealed class SortedOrder : IDisposable
        {
            public readonly ComputeBuffer buffer;
            public Vector3 direction;
            public long lastUsedPass;
            public SortedOrder(int count) { buffer = new ComputeBuffer(count, sizeof(int), ComputeBufferType.Structured); }
            public void Dispose() { buffer.Release(); }
        }

        private sealed class AssetBuffers : IDisposable
        {
            // Instances of one model share immutable splats and any matching view
            // orders. Moving an object never changes its internal depth order.
            public readonly ComputeBuffer splats;
            public readonly Bounds bounds;
            public readonly int count;
            private readonly Vector3[] positions;
            private readonly List<SortedOrder> orders = new List<SortedOrder>(16);
            private readonly uint[] keys, scratchKeys;
            private readonly int[] indices, scratchIndices;
            private readonly int[] histogram = new int[256];

            public AssetBuffers(IdolGaussianAsset source)
            {
                count = source.Count;
                positions = source.positions;
                keys = new uint[count]; scratchKeys = new uint[count];
                indices = new int[count]; scratchIndices = new int[count];
                var data = new Splat[count];
                var localBounds = new Bounds(positions[0], Vector3.zero);
                for (int i = 0; i < count; ++i)
                {
                    Vector3 p = positions[i], s = source.scales[i];
                    Quaternion q = source.rotations[i]; Color c = source.colors[i];
                    if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z) ||
                        !Finite(s.x) || !Finite(s.y) || !Finite(s.z) ||
                        !Finite(q.x) || !Finite(q.y) || !Finite(q.z) || !Finite(q.w) ||
                        !Finite(c.r) || !Finite(c.g) || !Finite(c.b) || !Finite(c.a))
                        throw new ArgumentException("The Gaussian asset contains a non-finite value.");
                    data[i] = new Splat
                    {
                        positionOpacity = new Vector4(p.x, p.y, p.z, c.a),
                        scale = new Vector4(s.x, s.y, s.z, 0),
                        rotation = new Vector4(q.x, q.y, q.z, q.w),
                        color = new Vector4(c.r, c.g, c.b, 1)
                    };
                    // Include the whole 3-sigma Gaussian, not only its center,
                    // so frustum culling does not clip a model at screen edges.
                    float radius = 3f * s.magnitude;
                    localBounds.Encapsulate(p - Vector3.one * radius);
                    localBounds.Encapsulate(p + Vector3.one * radius);
                }
                bounds = localBounds;
                splats = new ComputeBuffer(count, 64, ComputeBufferType.Structured);
                try { splats.SetData(data); }
                catch { splats.Release(); throw; }
            }

            public ComputeBuffer GetOrder(Vector3 direction)
            {
                // A very small tolerance absorbs normalization roundoff between
                // differently scaled copies. This is under 0.0001 degrees per
                // component; it does not quantize the Gaussian positions.
                for (int i = 0; i < orders.Count; ++i)
                {
                    SortedOrder cached = orders[i];
                    Vector3 delta = cached.direction - direction;
                    if (Mathf.Abs(delta.x) <= 0.000001f && Mathf.Abs(delta.y) <= 0.000001f && Mathf.Abs(delta.z) <= 0.000001f)
                    {
                        cached.lastUsedPass = renderPass;
                        return cached.buffer;
                    }
                }

                // Recycle old views, but never overwrite a buffer referenced by
                // another draw in the current command buffer.
                SortedOrder order = null;
                if (orders.Count >= 16)
                {
                    for (int i = 0; i < orders.Count; ++i)
                        if (orders[i].lastUsedPass != renderPass && (order == null || orders[i].lastUsedPass < order.lastUsedPass))
                            order = orders[i];
                }
                if (order == null) { order = new SortedOrder(count); orders.Add(order); }
                order.direction = direction;
                order.lastUsedPass = renderPass;

                for (int i = 0; i < count; ++i)
                {
                    float depth = Vector3.Dot(direction, positions[i]);
                    uint bits = new FloatBits { number = depth }.bits;
                    // IEEE float bits mapped to monotonically increasing uints.
                    keys[i] = (bits & 0x80000000u) != 0 ? ~bits : bits ^ 0x80000000u;
                    indices[i] = i;
                }

                // Four stable byte-radix passes are O(N), rather than a full
                // comparison sort for every copy of every model every frame.
                uint[] fromKeys = keys, toKeys = scratchKeys;
                int[] fromIndices = indices, toIndices = scratchIndices;
                for (int shift = 0; shift < 32; shift += 8)
                {
                    Array.Clear(histogram, 0, histogram.Length);
                    for (int i = 0; i < count; ++i) ++histogram[(fromKeys[i] >> shift) & 255];
                    int offset = 0;
                    for (int bucket = 0; bucket < histogram.Length; ++bucket)
                    {
                        int frequency = histogram[bucket]; histogram[bucket] = offset; offset += frequency;
                    }
                    for (int i = 0; i < count; ++i)
                    {
                        uint key = fromKeys[i]; int target = histogram[(key >> shift) & 255]++;
                        toKeys[target] = key; toIndices[target] = fromIndices[i];
                    }
                    var keySwap = fromKeys; fromKeys = toKeys; toKeys = keySwap;
                    var indexSwap = fromIndices; fromIndices = toIndices; toIndices = indexSwap;
                }
                order.buffer.SetData(fromIndices);
                ++LastSortCount;
                return order.buffer;
            }

            public void Dispose()
            {
                splats.Release();
                for (int i = 0; i < orders.Count; ++i) orders[i].Dispose();
                orders.Clear();
            }
        }

        private void OnEnable()
        {
            // Importer-created prefab assets are not world instances.
            if (!gameObject.scene.IsValid()) return;
            Instances.Add(this);
            Install();
        }

        private void OnDisable()
        {
            Instances.Remove(this);
            if (Instances.Count == 0) ReleaseBuffers();
        }

        private void OnDestroy() { OnDisable(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Shutdown();
            Instances.Clear();
            ReportedProblems.Clear();
            LastRenderedInstanceCount = LastRenderedGaussianCount = LastSortCount = 0;
            renderPass = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RestoreRegistration()
        {
            // Also supports Enter Play Mode with domain/scene reload disabled.
            foreach (var instance in FindObjectsByType<IdolGaussianRenderer>(FindObjectsSortMode.None))
                if (instance.isActiveAndEnabled && instance.gameObject.scene.IsValid()) Instances.Add(instance);
            if (Instances.Count > 0) Install();
        }

        private static void Install()
        {
            if (installed) return;
            Camera.onPostRender += RenderBuiltin;
            RenderPipelineManager.endCameraRendering += RenderSrp;
            Application.quitting += Shutdown;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
#endif
            installed = true;
        }

        private static void Shutdown()
        {
            Camera.onPostRender -= RenderBuiltin;
            RenderPipelineManager.endCameraRendering -= RenderSrp;
            Application.quitting -= Shutdown;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
#endif
            installed = false;
            ReleaseBuffers();
        }

        private static void ReleaseBuffers()
        {
            foreach (AssetBuffers buffers in Assets.Values) buffers.Dispose();
            Assets.Clear();
            foreach (Material material in Materials.Values)
                if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
            Materials.Clear();
            commands?.Release(); commands = null;
            properties = null;
            Draws.Clear();
        }

        private static void RenderBuiltin(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline == null) RenderCamera(camera);
        }

        private static void RenderSrp(ScriptableRenderContext context, Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline == null || rendering) return;
            rendering = true;
            try { if (BuildCommands(camera)) context.ExecuteCommandBuffer(commands); }
            finally { rendering = false; }
        }

        /// <summary>
        /// Submits complete splat models to the currently rendering Built-in
        /// camera. Camera.Render already invokes this through onPostRender.
        /// Call manually only from an equivalent camera rendering callback.
        /// </summary>
        public static void RenderCamera(Camera camera)
        {
            if (rendering) return;
            rendering = true;
            try { if (BuildCommands(camera)) Graphics.ExecuteCommandBuffer(commands); }
            finally { rendering = false; }
        }

        private static bool BuildCommands(Camera camera)
        {
            LastRenderedInstanceCount = LastRenderedGaussianCount = LastSortCount = 0;
            if (camera == null || Instances.Count == 0 || camera.pixelWidth < 1 || camera.pixelHeight < 1) return false;
            if (!SystemInfo.supportsComputeShaders)
            {
                ReportOnce(0, "Idol Gaussian rendering requires a graphics device with Shader Model 4.5 structured buffers. Use Direct3D 11 or newer.", null);
                return false;
            }
            // Unity can initialize this MonoBehaviour type on an import worker.
            // Native graphics objects and property IDs must be created here,
            // inside the actual main-thread camera rendering callback.
            if (properties == null)
            {
                properties = new MaterialPropertyBlock();
                splatsId = Shader.PropertyToID("_Splats");
                orderId = Shader.PropertyToID("_Order");
                objectToWorldId = Shader.PropertyToID("_ObjectToWorld");
                viewId = Shader.PropertyToID("_View");
                projectionId = Shader.PropertyToID("_Projection");
                viewportId = Shader.PropertyToID("_Viewport");
                opacityId = Shader.PropertyToID("_Opacity");
            }
            ++renderPass;
            Draws.Clear();
            GeometryUtility.CalculateFrustumPlanes(camera, Frustum);
            Matrix4x4 view = camera.worldToCameraMatrix;

            foreach (IdolGaussianRenderer instance in Instances)
            {
                if (instance == null || !instance.isActiveAndEnabled || instance.opacity <= 0f || instance.asset == null || instance.asset.Count == 0 ||
                    (camera.cullingMask & (1 << instance.gameObject.layer)) == 0) continue;
                if (!TryGetBuffers(instance.asset, out AssetBuffers buffers)) continue;
                Matrix4x4 matrix = instance.transform.localToWorldMatrix;
                Bounds worldBounds = TransformBounds(buffers.bounds, matrix);
                if (!GeometryUtility.TestPlanesAABB(Frustum, worldBounds)) continue;
                Shader selected = instance.shader != null ? instance.shader : Shader.Find("Idol/GaussianSplat");
                if (selected == null || !selected.isSupported)
                {
                    ReportOnce(instance.GetInstanceID(), "Idol/GaussianSplat is missing or unsupported. Include it in the build's Always Included Shaders.", instance);
                    continue;
                }
                if (!Materials.TryGetValue(selected, out Material material))
                {
                    material = new Material(selected) { name = "Idol shared Gaussian material", hideFlags = HideFlags.HideAndDontSave };
                    Materials.Add(selected, material);
                }
                Draws.Add(new DrawItem { renderer = instance, buffers = buffers, matrix = matrix, material = material,
                    depth = view.MultiplyPoint3x4(worldBounds.center).z });
            }

            // Separate lane actors are sorted as whole objects, far to near;
            // every Gaussian inside each actor is also sorted far to near.
            // Interpenetrating splat clouds would require a global point sort.
            Draws.Sort(CompareDraws);
            if (commands == null) commands = new CommandBuffer { name = "Relic Drift / complete Idol Gaussian assets" };
            commands.Clear();
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, camera.targetTexture != null);
            Vector4 viewport = new Vector4(camera.pixelWidth, camera.pixelHeight, 0, 0);
            for (int i = 0; i < Draws.Count; ++i)
            {
                DrawItem draw = Draws[i];
                Matrix4x4 localView = view * draw.matrix;
                Vector3 direction = new Vector3(localView.m20, localView.m21, localView.m22);
                if (direction.sqrMagnitude < 1e-12f) continue;
                direction.Normalize();
                properties.Clear();
                properties.SetBuffer(splatsId, draw.buffers.splats);
                properties.SetBuffer(orderId, draw.buffers.GetOrder(direction));
                properties.SetMatrix(objectToWorldId, draw.matrix);
                properties.SetMatrix(viewId, view);
                properties.SetMatrix(projectionId, projection);
                properties.SetVector(viewportId, viewport);
                properties.SetFloat(opacityId, draw.renderer.opacity);
                commands.DrawProcedural(Matrix4x4.identity, draw.material, 0, MeshTopology.Triangles, 6, draw.buffers.count, properties);
                ++LastRenderedInstanceCount;
                LastRenderedGaussianCount += draw.buffers.count;
            }
            return LastRenderedInstanceCount > 0;
        }

        private static bool TryGetBuffers(IdolGaussianAsset asset, out AssetBuffers buffers)
        {
            if (Assets.TryGetValue(asset, out buffers)) return true;
            if (ReportedProblems.Contains(asset.GetInstanceID())) return false;
            try
            {
                int count = asset.Count;
                if (asset.scales == null || asset.scales.Length != count || asset.rotations == null || asset.rotations.Length != count ||
                    asset.colors == null || asset.colors.Length != count)
                    throw new ArgumentException("Gaussian position, scale, rotation and color arrays must have the same length.");
                buffers = new AssetBuffers(asset);
                Assets.Add(asset, buffers);
                return true;
            }
            catch (Exception error)
            {
                ReportOnce(asset.GetInstanceID(), "Unable to prepare Idol Gaussian asset: " + error.Message, asset);
                return false;
            }
        }

        private static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
        {
            Vector3 x = matrix.MultiplyVector(new Vector3(local.extents.x, 0, 0));
            Vector3 y = matrix.MultiplyVector(new Vector3(0, local.extents.y, 0));
            Vector3 z = matrix.MultiplyVector(new Vector3(0, 0, local.extents.z));
            Vector3 extent = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(matrix.MultiplyPoint3x4(local.center), extent * 2f);
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        private static void ReportOnce(int key, string message, UnityEngine.Object context)
        {
            if (ReportedProblems.Add(key)) Debug.LogError(message, context);
        }
    }
}
