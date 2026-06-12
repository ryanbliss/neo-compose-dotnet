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
    /// The living system map: a pixel sun at the center, planets on slow
    /// elliptical orbits (angular speed falls off Kepler-style with radius),
    /// moons circling their planet, the ship riding its current outpost, and
    /// flare static that thickens as the storm builds. All art ships through
    /// the project schema's Files (synced into Resources/Neo/Files/Sprites).
    /// </summary>
    public sealed class SystemMapUI
    {
        private static readonly string[] SolarOrder =
        {
            "mercury", "venus", "earth", "mars", "jupiter",
            "saturn", "uranus", "neptune", "pluto",
        };

        private RectTransform map;
        private Image background;
        private Image sun;
        private Image ship;
        private Image flareOverlay;
        private MapAnimator animator;
        private readonly Dictionary<string, Button> planetButtons = new();
        private readonly Dictionary<string, Image> planetImages = new();
        private readonly Dictionary<string, GameObject> planetBadges = new();
        private string shipAtValueId;

        public void Build(Transform parent)
        {
            map = SampleUI.CreateRect(parent, "SystemMap");
            map.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            background = map.gameObject.AddComponent<Image>();
            background.color = CalmSpace;

            var sunRect = SampleUI.CreateRect(map, "Sun");
            sunRect.anchorMin = sunRect.anchorMax = new Vector2(0.5f, 0.5f);
            sunRect.sizeDelta = new Vector2(96f, 96f);
            sun = sunRect.gameObject.AddComponent<Image>();
            sun.raycastTarget = false;
            sun.preserveAspect = true;
            sun.enabled = false;

            var shipRect = SampleUI.CreateRect(map, "Ship");
            shipRect.sizeDelta = new Vector2(48f, 48f);
            ship = shipRect.gameObject.AddComponent<Image>();
            ship.raycastTarget = false;
            ship.preserveAspect = true;
            ship.enabled = false;

            var flareRect = SampleUI.CreateRect(map, "FlareStatic");
            flareRect.anchorMin = Vector2.zero;
            flareRect.anchorMax = Vector2.one;
            flareRect.offsetMin = Vector2.zero;
            flareRect.offsetMax = Vector2.zero;
            flareOverlay = flareRect.gameObject.AddComponent<Image>();
            flareOverlay.raycastTarget = false;
            flareOverlay.color = new Color(1f, 1f, 1f, 0f);
            flareOverlay.type = Image.Type.Tiled;

            animator = map.gameObject.AddComponent<MapAnimator>();
            animator.Bind(ship, sun, flareOverlay, background);
        }

        private static readonly Color CalmSpace = new(0.03f, 0.04f, 0.09f, 0.96f);

        public void Render(
            IReadOnlyList<ReadOnlyOutpost> outposts,
            ReadOnlyOutpost currentOutpost,
            int storm,
            ReadOnlyAnimationInfo shipAnimation,
            ReadOnlyAnimationInfo flareAnimation,
            Sprite sunSprite,
            AudioClip thrustSfx,
            Func<ReadOnlyOutpost, bool> hasNewContent,
            Action<ReadOnlyOutpost> onVisitOutpost)
        {
            // Animations & art are authored data (AnimationInfo records and
            // sprite values) — driven from the schema, never hand-rolled.
            animator.SetAnimations(shipAnimation, flareAnimation);
            animator.SetThrustClip(thrustSfx);
            if (sunSprite != null)
            {
                sun.sprite = sunSprite;
                sun.enabled = true;
            }

            var orbits = BuildOrbits(outposts);
            foreach (var outpost in outposts)
            {
                var captured = outpost;
                var key = outpost.valueId;
                var unlocked = outpost.Save.Unlocked;
                var isCurrent = outpost.valueId == currentOutpost.valueId;
                if (!planetButtons.TryGetValue(key, out var button))
                {
                    button = CreatePlanetMarker(outpost);
                    planetButtons[key] = button;
                }
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

                // "There is something to DO here" — a live dialogue would
                // trigger right now (intro or a newly opened conversation).
                planetBadges[key].SetActive(
                    unlocked && !isCurrent && hasNewContent(outpost));
            }

            animator.SetStorm(storm);
            animator.SetOrbits(orbits, planetButtons);
            if (shipAtValueId != currentOutpost.valueId &&
                planetButtons.TryGetValue(currentOutpost.valueId, out var home))
            {
                shipAtValueId = currentOutpost.valueId;
                animator.RideWith((RectTransform)home.transform);
            }
        }

        private Button CreatePlanetMarker(ReadOnlyOutpost outpost)
        {
            var rect = SampleUI.CreateRect(map, $"Planet {outpost.FullDisplayText}");
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
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

            var badgeRect = SampleUI.CreateRect(iconRect, "ContentBadge");
            badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(1f, 1f);
            badgeRect.pivot = new Vector2(0.7f, 0.5f);
            badgeRect.sizeDelta = new Vector2(20f, 20f);
            badgeRect.anchoredPosition = Vector2.zero;
            var badgeImage = badgeRect.gameObject.AddComponent<Image>();
            badgeImage.color = new Color(0.55f, 0.85f, 1f, 1f);
            badgeImage.raycastTarget = false;
            var badgeText = SampleUI.CreateText(badgeRect, "!", 14, new Color(0.05f, 0.10f, 0.18f), FontStyle.Bold);
            badgeText.alignment = TextAnchor.MiddleCenter;
            badgeText.raycastTarget = false;
            var badgeTextRect = badgeText.rectTransform;
            badgeTextRect.anchorMin = Vector2.zero;
            badgeTextRect.anchorMax = Vector2.one;
            badgeTextRect.offsetMin = Vector2.zero;
            badgeTextRect.offsetMax = Vector2.zero;
            planetBadges[outpost.valueId] = badgeRect.gameObject;
            badgeRect.gameObject.SetActive(false);

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

        /// <summary>
        /// One orbit per outpost: planets ride ellipses around the sun with
        /// Kepler-ish periods (outer = slower); co-orbiting outposts (moons of
        /// the same world) circle their shared planet point.
        /// </summary>
        private static Dictionary<string, OrbitSpec> BuildOrbits(
            IReadOnlyList<ReadOnlyOutpost> outposts)
        {
            var orbits = new Dictionary<string, OrbitSpec>();
            var byPlanet = outposts
                .GroupBy(outpost => outpost.Planet.optionId)
                .ToDictionary(group => group.Key, group => group.ToList());
            var present = SolarOrder.Where(byPlanet.ContainsKey).ToArray();
            for (var ring = 0; ring < present.Length; ring++)
            {
                var locals = byPlanet[present[ring]];
                float t = present.Length <= 1 ? 0f : ring / (float)(present.Length - 1);
                float rx = Mathf.Lerp(0.085f, 0.47f, t);
                float ry = rx * 0.62f;
                // Kepler-flavored: T ~ r^1.5; innermost ring ~70s per lap.
                float period = 70f * Mathf.Pow((ring + 1f), 1.5f) / 1f;
                float phase = ring * 2.39996f; // golden-angle spread
                for (var i = 0; i < locals.Count; i++)
                {
                    orbits[locals[i].valueId] = new OrbitSpec
                    {
                        rx = rx,
                        ry = ry,
                        angularSpeed = 2f * Mathf.PI / period,
                        phase = phase,
                        moonIndex = locals.Count == 1 ? -1 : i,
                        moonCount = locals.Count,
                    };
                }
            }
            return orbits;
        }

        private static Sprite TryImage(ReadOnlyOutpost outpost)
        {
            try { return outpost.Image; }
            catch (Exception) { return null; }
        }

        internal struct OrbitSpec
        {
            public float rx;
            public float ry;
            public float angularSpeed;
            public float phase;
            public int moonIndex;
            public int moonCount;
        }

        /// <summary>
        /// Drives orbital motion, the sun's breathing glow, the ship's
        /// thruster frames + flights, and the storm static/tint. A
        /// MonoBehaviour so the map needs no external runner.
        /// </summary>
        private sealed class MapAnimator : MonoBehaviour
        {
            private Image ship;
            private Image sun;
            private Image flare;
            private Image background;
            private ReadOnlyAnimationInfo shipAnimation;
            private ReadOnlyAnimationInfo flareAnimation;
            private Dictionary<string, OrbitSpec> orbits;
            private Dictionary<string, Button> markers;
            private RectTransform ride;
            private RectTransform target;
            private Vector2 from;
            private float flight;
            private float flightDuration;
            private Action onArrive;
            private int storm;
            private AudioSource thrust;
            private AudioClip thrustClip;
            private float frameTimer;
            private int frame;
            private float flareTimer;
            private int flareFrame;

            public bool Traveling => target != null;

            public void Bind(Image shipImage, Image sunImage, Image flareImage, Image backgroundImage)
            {
                ship = shipImage;
                sun = sunImage;
                flare = flareImage;
                background = backgroundImage;
            }

            public void SetAnimations(
                ReadOnlyAnimationInfo shipInfo,
                ReadOnlyAnimationInfo flareInfo)
            {
                shipAnimation = shipInfo;
                flareAnimation = flareInfo;
            }

            public void SetOrbits(
                Dictionary<string, OrbitSpec> orbitSpecs,
                Dictionary<string, Button> planetMarkers)
            {
                orbits = orbitSpecs;
                markers = planetMarkers;
            }

            public void SetStorm(int value) => storm = value;

            public void SetThrustClip(AudioClip clip) => thrustClip = clip;

            /// <summary>The ship follows this marker as it orbits.</summary>
            public void RideWith(RectTransform planet)
            {
                ride = planet;
                if (!Traveling)
                {
                    var rect = (RectTransform)ship.transform;
                    rect.anchorMin = rect.anchorMax = planet.anchorMin;
                }
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
                StartThrust();
            }

            private void StartThrust()
            {
                if (thrustClip == null) return;
                if (thrust == null)
                {
                    thrust = gameObject.AddComponent<AudioSource>();
                    thrust.playOnAwake = false;
                    thrust.loop = true;
                    thrust.volume = 0.55f;
                }
                thrust.clip = thrustClip;
                thrust.Play();
            }

            private void Update()
            {
                if (ship == null) return;

                AdvanceOrbits();
                PulseSun();
                AnimateShipFrames();
                MoveShip();
                AnimateStorm();
            }

            private void AdvanceOrbits()
            {
                if (orbits == null || markers == null) return;
                float now = Time.time;
                foreach (var pair in orbits)
                {
                    if (!markers.TryGetValue(pair.Key, out var button)) continue;
                    var spec = pair.Value;
                    float angle = spec.phase + now * spec.angularSpeed;
                    var center = new Vector2(
                        0.5f + spec.rx * Mathf.Cos(angle),
                        0.5f + spec.ry * Mathf.Sin(angle));
                    if (spec.moonIndex >= 0)
                    {
                        // Moons share the planet point and circle it briskly.
                        float moonAngle = now * spec.angularSpeed * 7f
                            + spec.moonIndex * (2f * Mathf.PI / spec.moonCount);
                        center += new Vector2(
                            0.05f * Mathf.Cos(moonAngle),
                            0.038f * Mathf.Sin(moonAngle));
                    }
                    var rect = (RectTransform)button.transform;
                    rect.anchorMin = rect.anchorMax = center;
                }
            }

            private void PulseSun()
            {
                if (sun == null || !sun.enabled) return;
                float pulse = 1f + 0.05f * Mathf.Sin(Time.time * 1.1f);
                sun.transform.localScale = new Vector3(pulse, pulse, 1f);
            }

            private void AnimateShipFrames()
            {
                if (shipAnimation == null || shipAnimation.Frames.Count == 0) return;
                frameTimer += Time.deltaTime;
                var fps = Mathf.Max(1, shipAnimation.FPS) * (Traveling ? 2f : 1f);
                if (frameTimer >= 1f / fps)
                {
                    frameTimer = 0f;
                    frame = (frame + 1) % shipAnimation.Frames.Count;
                    var sprite = shipAnimation.Frames[frame];
                    if (sprite != null)
                    {
                        ship.sprite = sprite;
                        ship.enabled = true;
                    }
                }
            }

            private void MoveShip()
            {
                var rect = (RectTransform)ship.transform;
                if (Traveling)
                {
                    flight += Time.deltaTime / flightDuration;
                    var eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(flight));
                    // The destination keeps orbiting mid-flight; chase it.
                    var anchor = Vector2.Lerp(from, target.anchorMin, eased);
                    ship.transform.localScale = new Vector3(
                        target.anchorMin.x >= from.x ? 1f : -1f, 1f, 1f);
                    rect.anchorMin = rect.anchorMax = anchor;
                    rect.anchoredPosition = new Vector2(
                        0f,
                        44f + Mathf.Sin(eased * Mathf.PI) * 26f);
                    if (flight >= 1f)
                    {
                        var arrived = onArrive;
                        ride = target;
                        target = null;
                        onArrive = null;
                        if (thrust != null) thrust.Stop();
                        arrived?.Invoke();
                    }
                    return;
                }

                if (ride != null)
                {
                    rect.anchorMin = rect.anchorMax = ride.anchorMin;
                }
                rect.anchoredPosition = new Vector2(
                    0f,
                    44f + Mathf.Sin(Time.time * 1.6f) * 3f);
            }

            private void AnimateStorm()
            {
                // The dread ramps: static density eases in quadratically and
                // space itself reddens as the clock climbs toward overflow.
                float intensity = Mathf.Clamp01(storm / 12f);
                if (background != null)
                {
                    background.color = Color.Lerp(
                        CalmSpace,
                        new Color(0.16f, 0.04f, 0.06f, 0.97f),
                        intensity * intensity);
                }
                if (flare == null || flareAnimation == null || flareAnimation.Frames.Count == 0)
                {
                    return;
                }
                float alpha = intensity * intensity * 0.5f;
                flare.color = new Color(1f, 1f, 1f, alpha);
                if (alpha <= 0f) return;
                flareTimer += Time.deltaTime;
                // Static flickers faster as the storm worsens.
                float fps = Mathf.Max(1, flareAnimation.FPS) * (1f + intensity * 2f);
                if (flareTimer >= 1f / fps)
                {
                    flareTimer = 0f;
                    flareFrame = (flareFrame + 1) % flareAnimation.Frames.Count;
                    var sprite = flareAnimation.Frames[flareFrame];
                    if (sprite != null) flare.sprite = sprite;
                }
            }
        }
    }
}
