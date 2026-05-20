// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using HelloWorld.Assets.Scripts.Neo;
using NeoCompose.Runtime;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace HelloWorld.Assets.Scripts
{
    /// <summary>
    /// Small dialogue overlay for the sample. It exposes targeted UI APIs so
    /// HelloWorldBehaviour can show the SDK dialogue event flow in one place.
    /// </summary>
    internal sealed class DialogueUI : IDisposable, IOutpostAnimationPlayer
    {
        private GameObject root;
        private Image speakerImage;
        private SpeakerImageAnimator speakerAnimator;
        private Text speakerText;
        private Text bodyText;
        private RectTransform optionStack;

        public string SpeakerName
        {
            set
            {
                EnsureBuilt();
                speakerText.text = value;
            }
        }

        public Sprite SpeakerImage
        {
            set
            {
                EnsureBuilt();
                speakerImage.sprite = value;
                speakerImage.enabled = value != null;
            }
        }

        public string Text
        {
            set
            {
                EnsureBuilt();
                bodyText.text = value;
            }
        }

        public void Show(
            string name,
            Sprite image,
            string text
        )
        {
            EnsureBuilt();
            SpeakerName = name;
            SpeakerImage = image;
            Text = text;
            ClearOptionButtons();
            root.SetActive(true);
        }

        public void ClearOptionButtons()
        {
            EnsureBuilt();
            for (var i = optionStack.childCount - 1; i >= 0; i--)
            {
                DestroyObject(optionStack.GetChild(i).gameObject);
            }
        }

        public void PrepareOptionButton(
            string buttonText,
            bool selectable,
            Action onClick,
            bool alreadyChosen = false
        )
        {
            EnsureBuilt();
            CreateOptionButton(
                buttonText,
                selectable,
                onClick,
                alreadyChosen
            );
        }

        public void Reset()
        {
            speakerAnimator?.CancelActive();
            if (root != null)
            {
                root.SetActive(false);
            }
        }

        public void PlaySpeakerAnimation(
            ReadOnlyAnimationInfo animationInfo,
            NeoDeferredFunction<bool> deferred)
        {
            EnsureBuilt();
            var clip = Resources.Load<AnimationClip>(NeoAnimationClipResources.ResourcePath(animationInfo.Name));
            if (clip is null)
            {
                Debug.LogWarning(
                    $"Neo Compose: failed to play animation '{animationInfo.Name}' because the AnimationClip resource was not found.");
                deferred.Complete(false);
                return;
            }

            speakerAnimator.Play(speakerImage, clip, deferred);
        }

        public void Dispose()
        {
            Reset();
            if (root != null)
            {
                DestroyObject(root);
                root = null;
            }
        }

        private void EnsureBuilt()
        {
            if (root != null) return;

            EnsureEventSystem();

            root = new GameObject("Dialogue UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
            canvas.planeDistance = 1f;
            canvas.sortingOrder = 100;
            if (canvas.worldCamera == null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var overlay = CreateRect(root.transform, "Overlay");
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = Vector2.zero;
            overlay.offsetMax = Vector2.zero;
            var overlayImage = overlay.gameObject.AddComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.42f);

            var panel = CreatePanel(overlay);
            BuildPanelContent(panel);
            speakerAnimator = root.AddComponent<SpeakerImageAnimator>();
            root.SetActive(false);
        }

        private static RectTransform CreatePanel(Transform parent)
        {
            var panel = CreateRect(parent, "Dialogue Panel");
            panel.anchorMin = new Vector2(0.12f, 0f);
            panel.anchorMax = new Vector2(0.88f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0f, 34f);
            panel.sizeDelta = new Vector2(0f, 330f);

            var image = panel.gameObject.AddComponent<Image>();
            image.color = new Color(0.08f, 0.10f, 0.14f, 0.98f);

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 24, 26);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return panel;
        }

        private void BuildPanelContent(Transform parent)
        {
            var speakerRow = CreateRect(parent, "Speaker");
            speakerRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
            var speakerLayout = speakerRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            speakerLayout.spacing = 10f;
            speakerLayout.childAlignment = TextAnchor.MiddleLeft;
            speakerLayout.childControlHeight = true;
            speakerLayout.childControlWidth = true;
            speakerLayout.childForceExpandHeight = false;
            speakerLayout.childForceExpandWidth = false;

            var imageRect = CreateRect(speakerRow, "Image");
            var imageLayout = imageRect.gameObject.AddComponent<LayoutElement>();
            imageLayout.preferredWidth = 30f;
            imageLayout.preferredHeight = 30f;
            imageLayout.minWidth = 30f;
            imageLayout.minHeight = 30f;
            speakerImage = imageRect.gameObject.AddComponent<Image>();
            speakerImage.preserveAspect = true;
            speakerImage.raycastTarget = false;
            speakerImage.enabled = false;

            speakerText = CreateText(speakerRow, "", 22, new Color(0.58f, 0.72f, 1f), FontStyle.Bold);
            speakerText.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            speakerText.verticalOverflow = VerticalWrapMode.Overflow;

            bodyText = CreateText(parent, "", 28, new Color(0.96f, 0.98f, 1f), FontStyle.Normal);
            bodyText.alignment = TextAnchor.UpperLeft;
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Overflow;
            var bodyLayout = bodyText.gameObject.GetComponent<LayoutElement>();
            bodyLayout.preferredHeight = 92f;
            bodyLayout.flexibleHeight = 1f;

            optionStack = CreateRect(parent, "Options");
            optionStack.gameObject.AddComponent<LayoutElement>().preferredHeight = 150f;
            var layout = optionStack.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
        }

        private void CreateOptionButton(string label, bool selectable, Action action, bool alreadyChosen)
        {
            var rect = CreateRect(optionStack, label);
            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 44f;
            layout.minHeight = 44f;

            var image = rect.gameObject.AddComponent<Image>();
            image.color = alreadyChosen
                ? new Color(0.12f, 0.15f, 0.22f, 1f)
                : new Color(0.20f, 0.38f, 0.66f, 1f);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = ButtonColors(alreadyChosen);
            button.interactable = selectable;
            button.onClick.AddListener(() =>
            {
                button.interactable = false;
                action();
            });

            var text = CreateText(
                rect,
                label,
                17,
                alreadyChosen ? new Color(0.62f, 0.68f, 0.78f, 1f) : Color.white,
                alreadyChosen ? FontStyle.Normal : FontStyle.Bold);
            text.alignment = TextAnchor.MiddleLeft;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(18f, 0f);
            text.rectTransform.offsetMax = new Vector2(-18f, 0f);
        }

        private static Text CreateText(Transform parent, string value, int fontSize, Color color, FontStyle fontStyle)
        {
            var rect = CreateRect(parent, "Text");
            var text = rect.gameObject.AddComponent<Text>();
            text.text = value;
            text.font = BuiltInFont;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = Mathf.Ceil(fontSize * 1.45f);
            return text;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static ColorBlock ButtonColors(bool alreadyChosen = false)
        {
            if (alreadyChosen)
            {
                return new ColorBlock
                {
                    normalColor = new Color(0.12f, 0.15f, 0.22f, 1f),
                    highlightedColor = new Color(0.17f, 0.22f, 0.32f, 1f),
                    pressedColor = new Color(0.10f, 0.12f, 0.18f, 1f),
                    selectedColor = new Color(0.12f, 0.15f, 0.22f, 1f),
                    disabledColor = new Color(0.10f, 0.11f, 0.15f, 1f),
                    colorMultiplier = 1f,
                    fadeDuration = 0.08f,
                };
            }

            return new ColorBlock
            {
                normalColor = new Color(0.20f, 0.38f, 0.66f, 1f),
                highlightedColor = new Color(0.25f, 0.47f, 0.78f, 1f),
                pressedColor = new Color(0.15f, 0.29f, 0.50f, 1f),
                selectedColor = new Color(0.20f, 0.38f, 0.66f, 1f),
                disabledColor = new Color(0.15f, 0.18f, 0.24f, 1f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
        }

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;

            _ = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static void DestroyObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
                return;
            }

            UnityEngine.Object.DestroyImmediate(target);
        }

        private static Font BuiltInFont =>
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ??
            Resources.GetBuiltinResource<Font>("Arial.ttf");

        private sealed class SpeakerImageAnimator : MonoBehaviour
        {
            private Coroutine current;
            private Animator animator;
            private PlayableGraph playableGraph;

            public void Play(
                Image target,
                AnimationClip clip,
                NeoDeferredFunction<bool> deferred)
            {
                CancelActive();
                current = StartCoroutine(PlayRoutine(target, clip, deferred));
            }

            public void CancelActive()
            {
                if (current != null)
                {
                    StopCoroutine(current);
                    current = null;
                }

                // Clean up the playable graph when stopping
                if (playableGraph.IsValid())
                {
                    playableGraph.Destroy();
                }
            }

            private System.Collections.IEnumerator PlayRoutine(
                Image target,
                AnimationClip clip,
                NeoDeferredFunction<bool> deferred
            )
            {
                target.enabled = true;

                if (!target.gameObject.TryGetComponent(out animator))
                {
                    animator = target.gameObject.AddComponent<Animator>();
                }

                if (clip == null)
                {
                    Debug.LogWarning("Neo Compose: AnimationClip is null.");
                    deferred.Complete(false);
                    yield break;
                }

                playableGraph = PlayableGraph.Create($"SpeakerImageGraph_{gameObject.name}");
                var playableOutput = AnimationPlayableOutput.Create(playableGraph, "Animation", animator);
                var clipPlayable = AnimationClipPlayable.Create(playableGraph, clip);

                clipPlayable.SetDuration(clip.length);
                clipPlayable.SetTime(0f);
                clipPlayable.SetSpeed(1f);

                // Tells the playable engine to freeze/hold the final frame when it finishes
                clipPlayable.SetDone(false);

                playableOutput.SetSourcePlayable(clipPlayable);
                playableGraph.Play();

                try
                {
                    // This loop runs until the internal PlayableGraph time passes the asset length
                    while (clipPlayable.GetTime() < clip.length)
                    {
                        if (deferred.CancellationToken.IsCancellationRequested)
                        {
                            if (playableGraph.IsValid()) playableGraph.Stop();
                            current = null;
                            yield break;
                        }

                        yield return null;
                    }

                    clipPlayable.SetTime(clip.length);
                    playableGraph.Evaluate(0f);
                    yield return null; // Wait exactly 1 UI frame so the graphic card processes the change
                }
                finally
                {
                    if (playableGraph.IsValid())
                    {
                        playableGraph.Destroy();
                    }
                }

                if (!deferred.CancellationToken.IsCancellationRequested && deferred.Pending)
                {
                    deferred.Complete(true);
                }

                current = null;
            }

            internal void OnDisable()
            {
                CancelActive();
            }

            internal void OnDestroy()
            {
                CancelActive();
            }
        }

    }
}
