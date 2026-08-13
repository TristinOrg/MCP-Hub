// Unity Editor command dispatcher: MVP with a few sample commands, extensible later.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
            ["unity.refresh_assets"]    = _ => HandleRefreshAssets()
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

            // Marshal to main thread
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                result = handler(argsJson);
            }
            else
            {
                var dispatched = false;
                EditorApplication.CallbackFunction cb = null!;
                cb = () =>
                {
                    try
                    {
                        Volatile.Write(ref result, handler(argsJson));
                    }
                    catch (Exception ex)
                    {
                        Volatile.Write(ref error, ex);
                    }
                    finally
                    {
                        EditorApplication.update -= cb;
                        Volatile.Write(ref dispatched, true);
                    }
                };
                EditorApplication.update += cb;

                // Spin-wait for main thread execution
                var spinStart = DateTime.UtcNow;
                while (!Volatile.Read(ref dispatched))
                {
                    if ((DateTime.UtcNow - spinStart).TotalSeconds > 30)
                        throw new TimeoutException($"Command '{toolName}' timed out on main thread.");
                    System.Threading.Thread.Sleep(10);
                }
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
            EditorApplication.SaveAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return "\"ok\"";
        }

        private static string HandleRefreshAssets()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            return "\"ok\"";
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
#endif
