// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace HelloWorld.Assets.Scripts
{
    /// <summary>
    /// Immediate-mode UI for the HelloWorld sample. Keeps layout and
    /// presentation separate from the SDK-facing behaviour.
    /// </summary>
    internal sealed class HelloWorldUi
    {
        private static readonly Planet[] AllPlanets =
        {
            Planet.mercury,
            Planet.venus,
            Planet.earth,
            Planet.mars,
            Planet.jupiter,
        };

        private Vector2 scrollPosition;

        public void Render(
            string fullText,
            Planet currentPlanet,
            IReadOnlyList<PlanetVisitSaved> visitedPlanets,
            Action<Planet> visitPlanet,
            Action save,
            Action reset)
        {
            const float margin = 24f;
            GUILayout.BeginArea(new Rect(
                margin,
                margin,
                Screen.width - margin * 2f,
                Screen.height - margin * 2f));

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Save", GUILayout.Width(96f), GUILayout.Height(32f)))
            {
                save();
            }
            if (GUILayout.Button("Reset", GUILayout.Width(96f), GUILayout.Height(32f)))
            {
                reset();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(12f);

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label(fullText, HeaderStyle());
            GUILayout.Space(12f);

            GUILayout.Label("Visited planets", SectionStyle());
            if (visitedPlanets.Count == 0)
            {
                GUILayout.Label("None yet");
            }
            else
            {
                foreach (var visit in visitedPlanets)
                {
                    GUILayout.Label(DisplayName(visit.world));
                }
            }

            GUILayout.Space(16f);
            GUILayout.Label("Travel", SectionStyle());
            foreach (var planet in AllPlanets)
            {
                if (planet.Equals(currentPlanet))
                {
                    continue;
                }
                if (GUILayout.Button($"Visit {DisplayName(planet)}", GUILayout.Height(36f)))
                {
                    visitPlanet(planet);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static string DisplayName(Planet planet)
        {
            var value = planet.optionId;
            return value.Length == 0
                ? value
                : char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static GUIStyle HeaderStyle()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
            };
            return style;
        }

        private static GUIStyle SectionStyle()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
            };
            return style;
        }
    }
}
