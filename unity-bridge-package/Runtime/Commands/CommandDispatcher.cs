// Unity Editor command dispatcher: MVP with a few sample commands, extensible later.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Tristin.MCPBridge
{
    /// <summary>
    /// Dispatches tool calls to UnityEditor API handlers on the main thread.
    /// </summary>
    public static class CommandDispatcher
    {
        /// <summary>
        /// Command registry: name -> handler(argsJson) => resultJson
        /// </summary>
        private static readonly Dictionary<string, Func<string, string>> _handlers = new()
        {
            ["ping"]                    = _ => "\"pong\"",
            ["unity.editor_info"]       = _ => HandleEditorInfo(),
            ["unity.list_scenes"]       = _ => HandleListScenes(),
            ["unity.create_gameobject"] = HandleCreateGameObject,
            ["unity.save_project"]      = _ => HandleSaveProject(),
            ["unity.refresh_assets"]    = _ => HandleRefreshAssets(),
            ["unity.create_prefab"]     = HandleCreatePrefab,
            ["unity.create_text"]       = HandleCreateText,
            ["unity.create_script"]     = HandleCreateScript
        };

        /// <summary>
        /// Execute a command synchronously on the main thread.
        /// </summary>
        public static string Dispatch(string toolName, string argsJson)
        {
            if (!_handlers.TryGetValue(toolName, out var handler))
                throw new NotSupportedException($"Tool '{toolName}' not supported. Available: {string.Join(", ", _handlers.Keys)}");

            string?   result   = null;
            Exception? error   = null;

            // Always marshal to main thread — Unity APIs cannot be called from
            // background threads (IPC receive thread, etc).
            var dispatched = false;
            EditorApplication.CallbackFunction cb = null!;
            cb = () =>
            {
                try
                {
                    result = handler(argsJson);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    EditorApplication.update -= cb;
                    dispatched = true;
                }
            };
            EditorApplication.update += cb;

            // Spin-wait for main thread execution
            var spinStart = DateTime.UtcNow;
            while (!dispatched)
            {
                if ((DateTime.UtcNow - spinStart).TotalSeconds > 30)
                    throw new TimeoutException($"Command '{toolName}' timed out on main thread.");
                System.Threading.Thread.Sleep(10);
            }

            if (error != null) throw new Exception(error.Message, error);
            return result ?? "null";
        }

        // ========== Command implementations ==========

        private static string HandleEditorInfo()
        {
            return $"{{\"unityVersion\":\"{Application.unityVersion}\",\"projectPath\":\"{Escape(Application.dataPath)}\",\"isPlaying\":{EditorApplication.isPlaying.ToString().ToLower()}}}";
        }

        private static string HandleListScenes()
        {
            var scenes = EditorBuildSettings.scenes;
            System.Text.StringBuilder sb = new("[");
            for (int i = 0; i < scenes.Length; i++)
            {
                var s = scenes[i];
                if (i > 0) sb.Append(',');
                sb.Append($"{{\"path\":\"{Escape(s.path)}\",\"enabled\":{s.enabled.ToString().ToLower()}}}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string HandleCreateGameObject(string argsJson)
        {
            // MVP simplified parsing: {"name":"NewObj","parent":null}
            string name = "NewGameObject";
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(argsJson, "\"name\"\\s*:\\s*\"(?<n>[^\"]+)\"");
                if (m.Success) name = m.Groups["n"].Value;
            }
            catch { /* use default name */ }

            var go = new GameObject(name);
            Selection.activeGameObject = go;
            return $"{{\"name\":\"{Escape(go.name)}\",\"instanceId\":{go.GetInstanceID()}}}";
        }

        private static string HandleSaveProject()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return "\"ok\"";
        }

        private static string HandleRefreshAssets()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            return "\"ok\"";
        }

        private static string HandleCreatePrefab(string argsJson)
        {
            // Args: {"path":"Assets/Prefabs/Test.prefab","children":[{"name":"UI","components":["RectTransform"]},{"name":"Text","components":["TextMeshPro","RectTransform"]}]}
            string path = "Assets/Prefabs/NewPrefab.prefab";
            var pathMatch = System.Text.RegularExpressions.Regex.Match(argsJson, "\"path\"\\s*:\\s*\"(?<p>[^\"]+)\"");
            if (pathMatch.Success) path = pathMatch.Groups["p"].Value;

            var root = new GameObject("Root");
            root.transform.localPosition = Vector3.zero;

            // Create children if specified
            var childrenMatch = System.Text.RegularExpressions.Regex.Match(argsJson, "\"children\"\\s*:\\s*\\[(?<c>.*?)\\]\\s*\\}", System.Text.RegularExpressions.RegexOptions.Singleline);
            if (childrenMatch.Success)
            {
                var childrenJson = childrenMatch.Groups["c"].Value;
                // Simple child parsing: find each {"name":"...","components":[...]} block
                var childMatches = System.Text.RegularExpressions.Regex.Matches(
                    childrenJson,
                    "\\{\\s*\"name\"\\s*:\\s*\"(?<name>[^\"]+)\"(?:\\s*,\\s*\"components\"\\s*:\\s*\\[(?<comps>.*?)\\])?\\s*\\}",
                    System.Text.RegularExpressions.RegexOptions.Singleline);

                foreach (System.Text.RegularExpressions.Match cm in childMatches)
                {
                    var childName = cm.Groups["name"].Value;
                    var child = new GameObject(childName);
                    child.transform.SetParent(root.transform, false);
                }
            }

            // Ensure directory exists
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir!);

            // Save as prefab
            var prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);

            if (prefab != null)
                return $"{{\"path\":\"{Escape(path)}\",\"instanceId\":{prefab.GetInstanceID()}}}";

            throw new Exception($"Failed to create prefab at {path}");
        }

        private static string HandleCreateText(string argsJson)
        {
            // Args: {"path":"Assets/Scripts/Test.cs","content":"public class Test {}"}
            string path = "Assets/Scripts/NewScript.cs";
            string content = "// Empty script";

            var pathMatch = System.Text.RegularExpressions.Regex.Match(argsJson, "\"path\"\\s*:\\s*\"(?<p>[^\"]+)\"");
            if (pathMatch.Success) path = pathMatch.Groups["p"].Value;

            var contentMatch = System.Text.RegularExpressions.Regex.Match(argsJson, "\"content\"\\s*:\\s*\"(?<c>.*?)\"\\s*\\}", System.Text.RegularExpressions.RegexOptions.Singleline);
            if (contentMatch.Success)
            {
                content = contentMatch.Groups["c"].Value;
                // Unescape
                content = content.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\");
            }

            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir!);

            System.IO.File.WriteAllText(path, content);
            AssetDatabase.Refresh();

            return $"{{\"path\":\"{Escape(path)}\"}}";
        }

        private static string HandleCreateScript(string argsJson) => HandleCreateText(argsJson);

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
#endif
