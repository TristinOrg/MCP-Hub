#if UNITY_EDITOR
using MCPForUnity.Editor.Services;
using UnityEditor;

namespace Tristin.CoplayBridge
{
    /// <summary>
    /// Connects the official Coplay Unity bridge to the Hub-managed server after package reload.
    /// </summary>
    [InitializeOnLoad]
    internal static class CoplayBridgeBootstrap
    {
        static CoplayBridgeBootstrap()
        {
            EditorPrefs.SetBool("MCPForUnity.UseHttpTransport", true);
            EditorPrefs.SetString("MCPForUnity.HttpTransportScope", "local");
            EditorPrefs.SetString("MCPForUnity.HttpUrl", "http://127.0.0.1:8080");
            EditorApplication.delayCall += Connect;
        }

        private static async void Connect()
        {
            var bridge = new BridgeControlService();
            if (!bridge.IsRunning)
                await bridge.StartAsync();
        }
    }
}
#endif
