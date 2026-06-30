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

        /// <summary>
        /// Set by the ShowRelic dialogue function: the NEXT text node stages
        /// this sprite LARGE in the center of the screen (the vault plaque is
        /// this game's cake — it earns the spotlight), then it stays up until
        /// the dialogue moves on.
        /// </summary>
        private Sprite pendingRelic;
        private GameObject relicStage;
        private Image relicImage;
        private Image dimmer;

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
            bool revealing = pendingRelic != null;
            relicStage.SetActive(revealing);
            if (revealing)
            {
                relicImage.sprite = pendingRelic;
                pendingRelic = null;
            }
            // Darken the world harder while a relic holds the stage.
            dimmer.color = new Color(0f, 0f, 0f, revealing ? 0.78f : 0.42f);
            ClearOptionButtons();
            root.SetActive(true);
        }

        public void ShowRelicSprite(Sprite relic)
        {
            pendingRelic = relic;
        }

        public void ClearOptionButtons()
        {
            EnsureBuilt();
            for (var i = optionStack.childCount - 1; i >= 0; i--)
            {
                SampleUI.DestroyObject(optionStack.GetChild(i).gameObject);
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
            pendingRelic = null;
            if (relicStage != null)
            {
                relicStage.SetActive(false);
            }
            if (root != null)
            {
                root.SetActive(false);
            }
        }

        public void PlaySpeakerAnimation(
            IReadOnlyAnimationInfo animationInfo,
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
                SampleUI.DestroyObject(root);
                root = null;
            }
        }

        private void EnsureBuilt()
        {
            if (root != null) return;

            SampleUI.EnsureEventSystem();

            root = new GameObject("Dialogue UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var overlay = SampleUI.CreateRect(root.transform, "Overlay");
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = Vector2.zero;
            overlay.offsetMax = Vector2.zero;
            var overlayImage = overlay.gameObject.AddComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.42f);
            dimmer = overlayImage;

            // The relic stage: a large centered presentation above the text
            // panel for ShowRelic reveals (the plaque deserves cake treatment).
            var relicRect = SampleUI.CreateRect(overlay, "RelicStage");
            relicRect.anchorMin = new Vector2(0.5f, 0.5f);
            relicRect.anchorMax = new Vector2(0.5f, 0.5f);
            relicRect.pivot = new Vector2(0.5f, 0.32f);
            relicRect.anchoredPosition = new Vector2(0f, 60f);
            relicRect.sizeDelta = new Vector2(560f, 560f);
            relicImage = relicRect.gameObject.AddComponent<Image>();
            relicImage.preserveAspect = true;
            relicImage.raycastTarget = false;
            relicRect.gameObject.SetActive(false);
            relicStage = relicRect.gameObject;

            var panel = CreatePanel(overlay);
            BuildPanelContent(panel);
            speakerAnimator = root.AddComponent<SpeakerImageAnimator>();
            root.SetActive(false);
        }

        private static RectTransform CreatePanel(Transform parent)
        {
            var panel = SampleUI.CreateRect(parent, "Dialogue Panel");
            // Centered column with a comfortable max width; height hugs the
            // content (no reserved dead space, no overflow at 3+ lines).
            panel.anchorMin = new Vector2(0.5f, 0f);
            panel.anchorMax = new Vector2(0.5f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0f, 44f);
            panel.sizeDelta = new Vector2(1180f, 0f);

            var image = panel.gameObject.AddComponent<Image>();
            image.color = new Color(0.07f, 0.09f, 0.13f, 0.985f);

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(32, 32, 22, 26);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // A thin accent line along the top edge gives the panel a frame
            // without a heavy border.
            var accent = SampleUI.CreateRect(panel, "Accent");
            accent.anchorMin = new Vector2(0f, 1f);
            accent.anchorMax = new Vector2(1f, 1f);
            accent.pivot = new Vector2(0.5f, 1f);
            accent.offsetMin = new Vector2(0f, -3f);
            accent.offsetMax = new Vector2(0f, 0f);
            var accentImage = accent.gameObject.AddComponent<Image>();
            accentImage.color = new Color(0.33f, 0.52f, 0.86f, 0.9f);
            accentImage.raycastTarget = false;
            accent.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return panel;
        }

        private void BuildPanelContent(Transform parent)
        {
            var speakerRow = SampleUI.CreateRect(parent, "Speaker");
            speakerRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
            var speakerLayout = speakerRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            speakerLayout.spacing = 10f;
            speakerLayout.childAlignment = TextAnchor.MiddleLeft;
            speakerLayout.childControlHeight = true;
            speakerLayout.childControlWidth = true;
            speakerLayout.childForceExpandHeight = false;
            speakerLayout.childForceExpandWidth = false;

            var imageRect = SampleUI.CreateRect(speakerRow, "Image");
            var imageLayout = imageRect.gameObject.AddComponent<LayoutElement>();
            imageLayout.preferredWidth = 38f;
            imageLayout.preferredHeight = 38f;
            imageLayout.minWidth = 38f;
            imageLayout.minHeight = 38f;
            speakerImage = imageRect.gameObject.AddComponent<Image>();
            speakerImage.preserveAspect = true;
            speakerImage.raycastTarget = false;
            speakerImage.enabled = false;

            speakerText = SampleUI.CreateText(speakerRow, "", 22, new Color(0.58f, 0.72f, 1f), FontStyle.Bold);
            speakerText.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            speakerText.verticalOverflow = VerticalWrapMode.Overflow;

            // The body sizes itself: Text's own preferred height drives the
            // layout, so one line and five lines both look intentional.
            bodyText = SampleUI.CreateText(parent, "", 25, new Color(0.96f, 0.98f, 1f), FontStyle.Normal);
            bodyText.alignment = TextAnchor.UpperLeft;
            bodyText.lineSpacing = 1.15f;
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Overflow;
            var bodyLayout = bodyText.gameObject.GetComponent<LayoutElement>();
            bodyLayout.preferredHeight = -1f;
            bodyLayout.flexibleHeight = 0f;
            bodyLayout.minHeight = 36f;

            optionStack = SampleUI.CreateRect(parent, "Options");
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
            // Quiet rows with a blue accent bar instead of solid full-width
            // slabs: the prose stays the loudest thing on the panel.
            var rect = SampleUI.CreateRect(optionStack, label);
            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 46f;
            layout.minHeight = 46f;

            var image = rect.gameObject.AddComponent<Image>();
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = ButtonColors(alreadyChosen);
            button.interactable = selectable;
            button.onClick.AddListener(() =>
            {
                button.interactable = false;
                action();
            });

            var bar = SampleUI.CreateRect(rect, "AccentBar");
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(0f, 1f);
            bar.pivot = new Vector2(0f, 0.5f);
            bar.offsetMin = Vector2.zero;
            bar.offsetMax = new Vector2(4f, 0f);
            var barImage = bar.gameObject.AddComponent<Image>();
            barImage.raycastTarget = false;
            barImage.color = alreadyChosen
                ? new Color(0.30f, 0.34f, 0.42f, 1f)
                : selectable
                    ? new Color(0.40f, 0.62f, 0.98f, 1f)
                    : new Color(0.30f, 0.34f, 0.42f, 0.6f);

            var text = SampleUI.CreateText(
                rect,
                alreadyChosen ? $"{label}  <size=13>(chosen)</size>" : label,
                18,
                alreadyChosen
                    ? new Color(0.58f, 0.64f, 0.74f, 1f)
                    : selectable ? Color.white : new Color(0.55f, 0.60f, 0.68f, 1f),
                alreadyChosen ? FontStyle.Normal : FontStyle.Bold);
            text.supportRichText = true;
            text.alignment = TextAnchor.MiddleLeft;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(20f, 0f);
            text.rectTransform.offsetMax = new Vector2(-18f, 0f);
        }
        private static ColorBlock ButtonColors(bool alreadyChosen = false)
        {
            if (alreadyChosen)
            {
                return new ColorBlock
                {
                    normalColor = new Color(0.10f, 0.12f, 0.17f, 1f),
                    highlightedColor = new Color(0.13f, 0.16f, 0.23f, 1f),
                    pressedColor = new Color(0.09f, 0.11f, 0.15f, 1f),
                    selectedColor = new Color(0.10f, 0.12f, 0.17f, 1f),
                    disabledColor = new Color(0.09f, 0.10f, 0.14f, 1f),
                    colorMultiplier = 1f,
                    fadeDuration = 0.08f,
                };
            }

            return new ColorBlock
            {
                normalColor = new Color(0.12f, 0.16f, 0.24f, 1f),
                highlightedColor = new Color(0.19f, 0.28f, 0.44f, 1f),
                pressedColor = new Color(0.10f, 0.13f, 0.20f, 1f),
                selectedColor = new Color(0.12f, 0.16f, 0.24f, 1f),
                disabledColor = new Color(0.09f, 0.11f, 0.15f, 1f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
        }
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
