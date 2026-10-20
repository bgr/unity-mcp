using NUnit.Framework;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using UnityEditor;

namespace MCPForUnityTests.Editor.Services
{
    /// <summary>
    /// Tests for the auto-connect decision. ShouldProbe is pure: it never probes the port and
    /// never starts the bridge.
    /// </summary>
    public class HttpAutoConnectHandlerTests : TransportPreferenceTestBase
    {
        private FakeTransportClient _fakeClient;
        private TransportManager _savedManager;
        private bool _savedUserEnded;
        private bool _savedConnectPending;
        private bool _savedResumeFlag;
        private bool _savedUseHttpTransport;
        private bool _hadScope;
        private string _savedScope;

        [SetUp]
        public void SetUp()
        {
            _savedUserEnded = SessionState.GetBool(HttpAutoConnectHandler.UserEndedSessionKey, false);
            _savedConnectPending = SessionState.GetBool(HttpAutoStartHandler.ConnectPendingKey, false);
            _savedResumeFlag = SessionState.GetBool(HttpBridgeReloadHandler.ResumeSessionKey, false);
            _savedUseHttpTransport = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);
            _hadScope = EditorPrefs.HasKey(EditorPrefKeys.HttpTransportScope);
            _savedScope = EditorPrefs.GetString(EditorPrefKeys.HttpTransportScope, string.Empty);
            _savedManager = MCPServiceLocator.TransportManager;

            SessionState.EraseBool(HttpAutoConnectHandler.UserEndedSessionKey);
            SessionState.EraseBool(HttpAutoStartHandler.ConnectPendingKey);
            SessionState.EraseBool(HttpBridgeReloadHandler.ResumeSessionKey);
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, true);
            EditorPrefs.SetString(EditorPrefKeys.HttpTransportScope, "local");
            EditorConfigurationCache.Instance.Refresh();

            _fakeClient = new FakeTransportClient();
            var manager = new TransportManager();
            manager.Configure(() => _fakeClient, () => _fakeClient);
            MCPServiceLocator.Register(manager);
        }

        [TearDown]
        public void TearDown()
        {
            // Stored-false and absent are indistinguishable: every read uses GetBool(key, false).
            SessionState.SetBool(HttpAutoConnectHandler.UserEndedSessionKey, _savedUserEnded);
            SessionState.SetBool(HttpAutoStartHandler.ConnectPendingKey, _savedConnectPending);
            SessionState.SetBool(HttpBridgeReloadHandler.ResumeSessionKey, _savedResumeFlag);
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, _savedUseHttpTransport);
            if (_hadScope)
                EditorPrefs.SetString(EditorPrefKeys.HttpTransportScope, _savedScope);
            else
                EditorPrefs.DeleteKey(EditorPrefKeys.HttpTransportScope);
            EditorConfigurationCache.Instance.Refresh();
            MCPServiceLocator.Register(_savedManager);
        }

        [Test]
        public void ShouldProbe_HttpLocalWithoutSession_Probes()
        {
            Assert.IsTrue(HttpAutoConnectHandler.ShouldProbe(editorBusy: false));
        }

        [Test]
        public void ShouldProbe_EditorBusy_Waits()
        {
            Assert.IsFalse(HttpAutoConnectHandler.ShouldProbe(editorBusy: true));
        }

        [Test]
        public void ShouldProbe_UserEndedSession_StaysPausedUntilUnpause()
        {
            HttpAutoConnectHandler.PauseUntilManualStart();
            Assert.IsFalse(HttpAutoConnectHandler.ShouldProbe(editorBusy: false));

            HttpAutoConnectHandler.Unpause();
            Assert.IsTrue(HttpAutoConnectHandler.ShouldProbe(editorBusy: false));
        }

        [Test]
        public void ShouldProbe_ResumePending_YieldsToReloadHandler()
        {
            SessionState.SetBool(HttpBridgeReloadHandler.ResumeSessionKey, true);

            Assert.IsFalse(HttpAutoConnectHandler.ShouldProbe(editorBusy: false));
        }

        [Test]
        public void ShouldProbe_AutoStartConnectPending_YieldsToAutoStart()
        {
            SessionState.SetBool(HttpAutoStartHandler.ConnectPendingKey, true);

            Assert.IsFalse(HttpAutoConnectHandler.ShouldProbe(editorBusy: false));
        }

        [Test]
        public void ShouldProbe_StdioSelected_Skips()
        {
            EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, false);
            EditorConfigurationCache.Instance.Refresh();

            Assert.IsFalse(HttpAutoConnectHandler.ShouldProbe(editorBusy: false));
        }

        [Test]
        public void ShouldProbe_HttpRemoteSelected_Skips()
        {
            EditorPrefs.SetString(EditorPrefKeys.HttpTransportScope, "remote");
            EditorConfigurationCache.Instance.Refresh();

            Assert.IsFalse(HttpAutoConnectHandler.ShouldProbe(editorBusy: false));
        }

        [Test]
        public void ShouldProbe_BridgeRunning_Skips()
        {
            var start = MCPServiceLocator.TransportManager.StartAsync(TransportMode.Http);
            Assert.IsTrue(start.IsCompleted && start.Result, "fake bridge should start synchronously");

            Assert.IsFalse(HttpAutoConnectHandler.ShouldProbe(editorBusy: false));
        }
    }
}
