// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.IO;

namespace NeoCompose.Unity.Editor
{
    public static class NeoComposePathUtility
    {
        public static bool TryNormalizeAssetDirectory(string path, out string normalized, out string error)
        {
            normalized = NormalizeSeparators(path).Trim();
            error = "";

            if (string.IsNullOrWhiteSpace(normalized))
            {
                error = "Directory path cannot be empty.";
                return false;
            }

            if (Path.IsPathRooted(normalized))
            {
                error = $"Directory path must be project-relative under Assets/: {path}";
                return false;
            }

            normalized = normalized.TrimEnd('/');
            if (normalized != "Assets" && !normalized.StartsWith("Assets/"))
            {
                error = $"Directory path must start with Assets/: {path}";
                return false;
            }

            foreach (var segment in normalized.Split('/'))
            {
                if (segment == "..")
                {
                    error = $"Directory path cannot contain '..': {path}";
                    return false;
                }
            }

            return true;
        }

        public static bool TryNormalizeResourcesDirectory(string path, out string normalized, out string error)
        {
            if (!TryNormalizeAssetDirectory(path, out normalized, out error))
            {
                return false;
            }

            if (normalized != "Assets/Resources" && !normalized.StartsWith("Assets/Resources/"))
            {
                error = $"Directory path must be under Assets/Resources/: {path}";
                return false;
            }

            return true;
        }

        public static bool TryNormalizeStreamingAssetsDirectory(string path, out string normalized, out string error)
        {
            if (!TryNormalizeAssetDirectory(path, out normalized, out error))
            {
                return false;
            }

            if (normalized != "Assets/StreamingAssets" && !normalized.StartsWith("Assets/StreamingAssets/"))
            {
                error = $"Directory path must be under Assets/StreamingAssets/: {path}";
                return false;
            }

            return true;
        }

        public static string CombineAssetPath(string assetDirectory, string fileName)
        {
            return $"{assetDirectory.TrimEnd('/')}/{fileName}";
        }

        public static string NormalizeSeparators(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
