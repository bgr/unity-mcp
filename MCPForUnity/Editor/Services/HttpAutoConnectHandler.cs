using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Windows;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Starts the HTTP Local session whenever the local server is reachable and no session is
    /// running, with or without the MCP for Unity window open. It never launches a server.
    /// This covers editor launch, a reload resume that ran out of retries, and a session the
    /// window ended because the server went away. End Session pauses it until the user starts
    /// a session again.
    /// </summary>
    [InitializeOnLoad]
    internal static class HttpAutoConnectHandler
    {
        internal const string UserEndedSessionKey = "HttpAutoConnectHandler.UserEndedSession";

        private const double PollIntervalSeconds = 2.0;  // the probe runs on a thread-pool thread
        private const double FailureBackoffSeconds = 30.0;  // a reachable server that refuses the session

        private static double _nextPollTime;
        private static bool _attemptInFlight;

        static HttpAutoConnectHandler()
        {
            if (Application.isBatchMode &&
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UNITY_MCP_ALLOW_BATCH")))
            {
                return;
            }

            EditorApplication.update += Tick;
        }

        internal static void PauseUntilManualStart() => SessionState.SetBool(UserEndedSessionKey, true);

        internal static void Unpause() => SessionState.EraseBool(UserEndedSessionKey);

        private static void Tick()
        {
            if (_attemptInFlight) return;

            double now = EditorApplication.timeSinceStartup;
            if (now < _nextPollTime) return;
            _nextPollTime = now + PollIntervalSeconds;

            bool shouldProbe;
            try
            {
                shouldProbe = ShouldProbe(HttpBridgeReloadHandler.IsEditorBusy());
            }
            catch (Exception ex)
            {
                // Services may not be initialized on the first frames of a fresh launch.
                McpLog.Debug($"[HTTP Auto-Connect] Services not ready: {ex.Message}");
                return;
            }

            if (shouldProbe)
            {
                _ = ConnectIfServerReachableAsync();
            }
        }

        /// <summary>
        /// Decision core, separated so EditMode tests can drive it.
        /// </summary>
        internal static bool ShouldProbe(bool editorBusy)
        {
            if (editorBusy) return false;
            if (SessionState.GetBool(UserEndedSessionKey, false)) return false;

            // The reload resume and the auto-start connect own the bridge while they are pending.
            if (HttpBridgeReloadHandler.IsResumePending) return false;
            if (SessionState.GetBool(HttpAutoStartHandler.ConnectPendingKey, false)) return false;

            if (!EditorConfigurationCache.Instance.UseHttpTransport) return false;
            if (HttpEndpointUtility.IsRemoteScope()) return false;

            // The HTTP state stays connected while WebSocketTransportClient reconnects on its own.
            return !MCPServiceLocator.TransportManager.IsRunning(TransportMode.Http);
        }

        private static async Task ConnectIfServerReachableAsync()
        {
            _attemptInFlight = true;
            try
            {
                if (!await MCPServiceLocator.Server.IsLocalHttpServerReachableAsync()) return;

                // Settings or the session may have changed while the probe ran.
                if (!ShouldProbe(HttpBridgeReloadHandler.IsEditorBusy())) return;

                if (await MCPServiceLocator.Bridge.StartAsync())
                {
                    McpLog.Info("Session connected");
                    MCPForUnityEditorWindow.RequestHealthVerification();
                }
                else
                {
                    _nextPollTime = EditorApplication.timeSinceStartup + FailureBackoffSeconds;
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[HTTP Auto-Connect] Failed: {ex.Message}");
                _nextPollTime = EditorApplication.timeSinceStartup + FailureBackoffSeconds;
            }
            finally
            {
                _attemptInFlight = false;
            }
        }
    }
}
