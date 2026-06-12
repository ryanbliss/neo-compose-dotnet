// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using HelloWorld.Assets.Scripts.Neo;
using UnityEngine;
using UnityEngine.UI;

namespace HelloWorld.Assets.Scripts
{
    /// <summary>
    /// The pixel-art system map: planet sprites laid out in solar order, a
    /// little ship that flies between them when you travel, and a flare-static
    /// overlay that thickens with the storm index (hello-world-plot.md §8).
    /// Replaces the old outpost button grid. All art ships through the project
    /// schema's Files (synced into Resources/Neo/Files/Sprites).
    /// </summary>
    public sealed class SystemMapUI
    {
        private const string ShipResource = "Neo/Files/Sprites/8c2b0f9f-f5e5-42a5-9ce7-7e15dc89b6c7-Ship (thruster)";
        private const string FlareResource = "Neo/Files/Sprites/1e4112b5-503c-4cfc-a026-4f5d38e6999c-Flare static";

        private static readonly string[] SolarOrder =
        {
            "mercury", "venus", "earth", "mars", "jupiter",
            "saturn", "uranus", "neptune", "pluto",
        };

        private RectTransform map;
        private RawImage ship;
        private RawImage flareOverlay;
        private MapAnimator animator;
        private readonly Dictionary<string, Button> planetButtons = new();
        private readonly Dictionary<string, Image> planetImages = new();
        private string shipAtValueId;

        public void Build(Transform parent)
        {
            map = SampleUI.CreateRect(parent, "SystemMap");
            map.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var background = map.gameObject.AddComponent<Image>();
            background.color = new Color(0.03f, 0.04f, 0.09f, 0.9f);

            var shipRect = SampleUI.CreateRect(map, "Ship");
            shipRect.sizeDelta = new Vector2(72f, 30f);
            ship = shipRect.gameObject.AddComponent<RawImage>();
            ship.texture = Resources.Load<Texture2D>(ShipResource);
            ship.raycastTarget = false;
            ship.enabled = ship.texture != null;
            ship.uvRect = new Rect(0f, 0f, 0.25f, 1f);

            var flareRect = SampleUI.CreateRect(map, "FlareStatic");
            flareRect.anchorMin = Vector2.zero;
            flareRect.anchorMax = Vector2.one;
            flareRect.offsetMin = Vector2.zero;
            flareRect.offsetMax = Vector2.zero;
            flareOverlay = flareRect.gameObject.AddComponent<RawImage>();
            flareOverlay.texture = Resources.Load<Texture2D>(FlareResource);
            flareOverlay.raycastTarget = false;
            flareOverlay.color = new Color(1f, 1f, 1f, 0f);
            flareOverlay.uvRect = new Rect(0f, 0f, 1f / 3f, 1f);

            animator = map.gameObject.AddComponent<MapAnimator>();
            animator.Bind(ship, flareOverlay);
        }

        public void Render(
            IReadOnlyList<ReadOnlyOutpost> outposts,
            ReadOnlyOutpost currentOutpost,
            int storm,
            Action<ReadOnlyOutpost> onVisitOutpost)
        {
            var positions = LayoutPositions(outposts);
            foreach (var outpost in outposts)
            {
                var captured = outpost;
                var key = outpost.valueId;
                var unlocked = outpost.Save.Unlocked;
                var isCurrent = outpost.valueId == currentOutpost.valueId;
                if (!planetButtons.TryGetValue(key, out var button))
                {
                    button = CreatePlanetMarker(outpost, positions[key]);
                    planetButtons[key] = button;
                }
                var rect = (RectTransform)button.transform;
                rect.anchorMin = rect.anchorMax = positions[key];
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                {
                    if (animator.Traveling) return;
                    animator.FlyTo(
                        ((RectTransform)planetButtons[captured.valueId].transform),
                        () => onVisitOutpost(captured));
                });
                button.interactable = unlocked && !isCurrent && !animator.Traveling;

                var image = planetImages[key];
                image.color = unlocked
                    ? Color.white
                    : new Color(0.32f, 0.35f, 0.42f, 0.6f);
            }

            animator.SetStorm(storm);
            if (shipAtValueId != currentOutpost.valueId &&
                planetButtons.TryGetValue(currentOutpost.valueId, out var home))
            {
                shipAtValueId = currentOutpost.valueId;
                animator.SnapTo((RectTransform)home.transform);
            }
        }

        private Button CreatePlanetMarker(ReadOnlyOutpost outpost, Vector2 anchor)
        {
            var rect = SampleUI.CreateRect(map, $"Planet {outpost.FullDisplayText}");
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = new Vector2(56f, 72f);
            var button = rect.gameObject.AddComponent<Button>();

            var iconRect = SampleUI.CreateRect(rect, "Image");
            iconRect.anchorMin = new Vector2(0.5f, 1f);
            iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 1f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = new Vector2(48f, 48f);
            var icon = iconRect.gameObject.AddComponent<Image>();
            icon.sprite = TryImage(outpost);
            icon.preserveAspect = true;
            icon.enabled = icon.sprite != null;
            planetImages[outpost.valueId] = icon;
            button.targetGraphic = icon;

            var label = SampleUI.CreateText(rect, outpost.Name, 12, new Color(0.78f, 0.85f, 0.95f), FontStyle.Normal);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = new Vector2(0.5f, 0f);
            labelRect.anchorMax = new Vector2(0.5f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = new Vector2(96f, 18f);
            label.alignment = TextAnchor.LowerCenter;
            return button;
        }

        /// <summary>Solar order left-to-right; moon outposts fan out vertically.</summary>
        private static Dictionary<string, Vector2> LayoutPositions(
            IReadOnlyList<ReadOnlyOutpost> outposts)
        {
            var positions = new Dictionary<string, Vector2>();
            var byPlanet = outposts
                .GroupBy(outpost => outpost.Planet.optionId)
                .ToDictionary(group => group.Key, group => group.ToList());
            var planetCount = SolarOrder.Count(planet => byPlanet.ContainsKey(planet));
            var column = 0;
            foreach (var planet in SolarOrder)
            {
                if (!byPlanet.TryGetValue(planet, out var locals)) continue;
                var x = planetCount <= 1
                    ? 0.5f
                    : 0.06f + 0.88f * (column / (float)(planetCount - 1));
                // Gentle orbital wave so it reads as a system, not a chart.
                var baseY = 0.58f + 0.16f * Mathf.Sin(column * 1.05f);
                for (var i = 0; i < locals.Count; i++)
                {
                    var y = locals.Count == 1
                        ? baseY
                        : baseY + (i - (locals.Count - 1) / 2f) * 0.24f;
                    positions[locals[i].valueId] = new Vector2(x, Mathf.Clamp(y, 0.14f, 0.9f));
                }
                column++;
            }
            return positions;
        }

        private static Sprite TryImage(ReadOnlyOutpost outpost)
        {
            try { return outpost.Image; }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Drives the ship's thruster frames, idle bob, travel flights, and the
        /// flare static. A MonoBehaviour so the map needs no external runner.
        /// </summary>
        private sealed class MapAnimator : MonoBehaviour
        {
            private RawImage ship;
            private RawImage flare;
            private RectTransform target;
            private Vector2 from;
            private float flight;
            private float flightDuration;
            private Action onArrive;
            private int storm;
            private float frameTimer;
            private int frame;

            public bool Traveling => target != null;

            public void Bind(RawImage shipImage, RawImage flareImage)
            {
                ship = shipImage;
                flare = flareImage;
            }

            public void SetStorm(int value) => storm = value;

            public void SnapTo(RectTransform planet)
            {
                var rect = (RectTransform)ship.transform;
                rect.anchorMin = rect.anchorMax = planet.anchorMin;
                rect.anchoredPosition = new Vector2(0f, 44f);
            }

            public void FlyTo(RectTransform planet, Action arrived)
            {
                var rect = (RectTransform)ship.transform;
                from = rect.anchorMin;
                target = planet;
                flight = 0f;
                flightDuration = Mathf.Max(
                    0.55f,
                    Vector2.Distance(from, planet.anchorMin) * 3.2f);
                onArrive = arrived;
                // Face the direction of travel (sheet faces right).
                ship.transform.localScale = new Vector3(
                    planet.anchorMin.x >= from.x ? 1f : -1f, 1f, 1f);
            }

            private void Update()
            {
                if (ship == null) return;

                // Thruster frames: quick pulse while flying, lazy idle otherwise.
                frameTimer += Time.deltaTime;
                var frameLength = Traveling ? 0.07f : 0.22f;
                if (frameTimer >= frameLength)
                {
                    frameTimer = 0f;
                    frame = (frame + 1) % 4;
                    ship.uvRect = new Rect(frame * 0.25f, 0f, 0.25f, 1f);
                }

                var rect = (RectTransform)ship.transform;
                if (Traveling)
                {
                    flight += Time.deltaTime / flightDuration;
                    var eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(flight));
                    var anchor = Vector2.Lerp(from, target.anchorMin, eased);
                    rect.anchorMin = rect.anchorMax = anchor;
                    rect.anchoredPosition = new Vector2(
                        0f,
                        44f + Mathf.Sin(eased * Mathf.PI) * 26f);
                    if (flight >= 1f)
                    {
                        var arrived = onArrive;
                        target = null;
                        onArrive = null;
                        arrived?.Invoke();
                    }
                }
                else
                {
                    rect.anchoredPosition = new Vector2(
                        0f,
                        44f + Mathf.Sin(Time.time * 1.6f) * 3f);
                }

                if (flare != null && flare.texture != null)
                {
                    // Storm static: frames jitter, alpha scales with the index.
                    var alpha = Mathf.Clamp01(storm / 14f) * 0.38f;
                    flare.color = new Color(1f, 1f, 1f, alpha);
                    if (alpha > 0f && Time.frameCount % 6 == 0)
                    {
                        var slice = UnityEngine.Random.Range(0, 3);
                        flare.uvRect = new Rect(slice / 3f, 0f, 1f / 3f, 1f);
                    }
                }
            }
        }
    }
}
