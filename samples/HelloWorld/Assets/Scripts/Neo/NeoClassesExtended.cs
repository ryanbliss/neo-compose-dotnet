using System.IO;
using UnityEngine;
using NeoCompose.Runtime;
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEditor;
#endif

#nullable enable

namespace HelloWorld.Assets.Scripts.Neo
{
    public partial class CollisionTileLayer
    {
        protected override void OnRenderTargetCreated(
            NeoTileLayerRenderTargetContext context)
        {
            base.OnRenderTargetCreated(context);
            context.Target.Tilemap.gameObject.AddComponent<UnityEngine.Tilemaps.TilemapCollider2D>();
        }
    }

    public partial class PlayerSpawnObject
    {
        protected override void OnObjectSpawned(NeoObjectBehaviour behaviour)
        {
            base.OnObjectSpawned(behaviour);
            var body = behaviour.gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
        }
    }

    public partial class Outpost
    {
        protected override void LazyInitialize()
        {
            base.LazyInitialize();
            FunctionHandler ??= new OutpostFunctionHandler(this);

            if (SaveUnsafe is not null)
            {
                return;
            }

            HelloWorldNeo.Instance.Save.OutpostSaveMap.Add(valueId!, new());
        }
    }

    public interface IOutpostAnimationPlayer
    {
        void PlaySpeakerAnimation(
            IReadOnlyAnimationInfo animation,
            NeoDeferredFunction<bool> deferred);

        /// <summary>Presents an authored relic sprite on the next dialogue node.</summary>
        void ShowRelicSprite(Sprite relic);
    }

    internal sealed class OutpostFunctionHandler : IOutpostFunctionHandler
    {
        public static IOutpostAnimationPlayer? AnimationPlayer { get; set; }

        private readonly Outpost Outpost;

        public OutpostFunctionHandler(Outpost Outpost)
        {
            this.Outpost = Outpost;
        }

        public void PlayAnimation(NeoDeferredFunction<bool> deferred)
        {
            IReadOnlyAnimationInfo? animation = Outpost.AnimatedImage;
            if (animation is null || AnimationPlayer is null || !Application.isPlaying)
            {
                deferred.Complete(false);
                return;
            }

            try
            {
                AnimationPlayer.PlaySpeakerAnimation(animation, deferred);
            }
            catch (System.Exception exception)
            {
                deferred.Fail(exception);
            }
        }

        public string DebugLog(string text)
        {
            string log = $"<color=green>{Outpost.Name}:</color> {text}";
            Debug.Log(log);
            return log;
        }

        public bool ShowRelic()
        {
            // The relic is authored art from the project schema (Assets.Art),
            // not a hard-coded asset path.
            if (AnimationPlayer is null) return false;
            AnimationPlayer.ShowRelicSprite(HelloWorldNeo.Instance.Assets.Art.VaultPlaqueSprite);
            return true;
        }
    }

    public partial class BlockedPath
    {
        protected override void LazyInitialize()
        {
            base.LazyInitialize();
            FunctionHandler ??= new BlockedPathFunctionHandler(this);
        }
    }

    internal sealed class BlockedPathFunctionHandler : IBlockedPathFunctionHandler
    {
        private readonly BlockedPath blockedPath;

        public BlockedPathFunctionHandler(BlockedPath blockedPath)
        {
            this.blockedPath = blockedPath;
        }

        public bool ClearPath()
        {
            blockedPath.Tiles.Clear();
            return true;
        }
    }

    internal static class NeoAnimationClipResources
    {
        public const string AssetDirectory = "Assets/Resources/Neo/Animations";
        public const string ResourceDirectory = "Neo/Animations";

        public static string AssetName(string name)
        {
            string trimmed = string.IsNullOrWhiteSpace(name) ? "AnimationInfo" : name.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                trimmed = trimmed.Replace(invalid, '_');
            }

            return trimmed;
        }

        public static string AssetPath(string name) => $"{AssetDirectory}/{AssetName(name)}.anim";

        public static string ResourcePath(string name) => $"{ResourceDirectory}/{AssetName(name)}";
    }

    internal static class OldConsoleLandingPresentation
    {
        public static ConsoleObjectColliderShape ObjectCollider(object obj)
        {
            if (obj is ExitPromptObject)
            {
                return new ConsoleObjectColliderShape(
                    new Vector2(2f, 1f),
                    new Vector2(0.5f, 0f),
                    isTrigger: true);
            }

            return new ConsoleObjectColliderShape(
                Vector2.one,
                Vector2.zero,
                isTrigger: true);
        }
    }

    public sealed class ConsoleObjectColliderShape
    {
        public ConsoleObjectColliderShape(Vector2 size, Vector2 offset, bool isTrigger)
        {
            Size = size;
            Offset = offset;
            IsTrigger = isTrigger;
        }

        public Vector2 Size { get; }
        public Vector2 Offset { get; }
        public bool IsTrigger { get; }
    }

#if UNITY_EDITOR
    // Example showing how you can hook into asset synchronization for things like creating AnimationClip assets from a list of Sprites
    public partial class AnimationInfo
    {
        public override void OnDidSynchronize()
        {
            var frames = new List<Sprite>();
            foreach (Sprite frame in Frames)
            {
                if (frame != null) frames.Add(frame);
            }

            if (frames.Count == 0)
            {
                Debug.LogWarning($"Neo Compose animation '{Name}' did not create an AnimationClip because it has no synchronized frames.");
                return;
            }

            int fps = FPS;
            string assetPath = NeoAnimationClipResources.AssetPath(Name);
            Directory.CreateDirectory(NeoAnimationClipResources.AssetDirectory);

            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            bool isNewAsset = false;

            if (clip == null)
            {
                clip = new AnimationClip();
                isNewAsset = true;
            }
            else
            {
                clip.ClearCurves();
            }

            clip.name = NeoAnimationClipResources.AssetName(Name);
            clip.frameRate = fps;

            // 1. Force the clip to be modern (Mecanim/Playables)
            clip.legacy = false;

            // 2. Configure Modern Looping / Wrap settings
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false; // Set to true if you want this clip to repeat automatically
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var keyframes = new ObjectReferenceKeyframe[frames.Count + 1];
            for (var i = 0; i < frames.Count; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i / (float)fps,
                    value = frames[i],
                };
            }

            keyframes[keyframes.Length - 1] = new ObjectReferenceKeyframe
            {
                time = frames.Count / (float)fps,
                value = frames[frames.Count - 1],
            };

            AnimationUtility.SetObjectReferenceCurve(
                clip,
                new EditorCurveBinding
                {
                    path = "",
                    type = typeof(Image),
                    propertyName = "m_Sprite",
                },
                keyframes);

            // 3. Proper asset pipeline handling
            if (isNewAsset)
            {
                AssetDatabase.CreateAsset(clip, assetPath);
            }
            else
            {
                EditorUtility.SetDirty(clip);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath);
        }
    }
#endif
}
