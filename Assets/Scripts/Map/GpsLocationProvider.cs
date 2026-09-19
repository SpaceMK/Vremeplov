using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TalesTensor.Map
{
    /// <summary>Where the location pipeline currently is, for driving on-screen feedback.</summary>
    public enum GpsStatus
    {
        /// <summary>Just started; nothing decided yet.</summary>
        Initializing,
        /// <summary>Showing (or waiting on) the OS location-permission dialog.</summary>
        RequestingPermission,
        /// <summary>The user has refused the location permission.</summary>
        PermissionDenied,
        /// <summary>Location services are switched off at the OS level.</summary>
        Disabled,
        /// <summary>Permission granted and services on; waiting for the first fix.</summary>
        WaitingForSignal,
        /// <summary>We have a live position.</summary>
        Running,
        /// <summary>Permission (or location services) was taken away after we had a fix.</summary>
        PermissionRevoked,
    }

    /// <summary>
    /// Wraps Unity's built-in <see cref="LocationService"/> and exposes a simple
    /// polled position. With no GPS hardware (desktop / editor) it falls back to a
    /// simulated location you can walk around with WASD / arrow keys, so the map is
    /// fully testable without deploying to a phone.
    /// </summary>
    [DisallowMultipleComponent]
    public class GpsLocationProvider : MonoBehaviour
    {
        public enum LocationMode
        {
            /// <summary>Real GPS on device, simulated in the editor.</summary>
            Auto,
            ForceSimulated,
            ForceDevice,
        }

        [Header("Mode")]
        public LocationMode mode = LocationMode.Auto;

        [Header("Simulation (editor / no GPS)")]
        [Tooltip("Where the simulated player starts.")]
        public double simulatedLatitude = 55.9486;   // Edinburgh Castle
        public double simulatedLongitude = -3.1999;
        [Tooltip("Simulated walking speed in metres/second while holding a key.")]
        public float simulatedSpeed = 12f;

        [Header("Device GPS")]
        [Tooltip("Desired accuracy in metres.")]
        public float desiredAccuracy = 5f;
        [Tooltip("Minimum movement (metres) before a new reading is reported.")]
        public float updateDistance = 1f;
        [Tooltip("Seconds to wait before retrying when permission is denied, location " +
                 "is off, or a fix can't be acquired. We keep retrying (and re-asking " +
                 "for permission) until we get a position.")]
        public float retryInterval = 3f;

        [Tooltip("When simulating, WASD moves relative to this transform's heading " +
                 "(usually the map camera) so 'forward' matches what you see. " +
                 "Set automatically by MapController.")]
        public Transform headingReference;

        public bool HasFix { get; private set; }
        public LatLon Current { get; private set; }

        /// <summary>Current state of the location pipeline. Drives the on-screen overlay.</summary>
        public GpsStatus Status { get; private set; } = GpsStatus.Initializing;

        /// <summary>Raised whenever <see cref="Status"/> changes.</summary>
        public event Action<GpsStatus> OnStatusChanged;

        LatLon _sim;

        void SetStatus(GpsStatus s)
        {
            if (Status == s) return;
            Status = s;
            OnStatusChanged?.Invoke(s);
        }

        bool UseSimulation =>
            mode == LocationMode.ForceSimulated ||
            (mode == LocationMode.Auto && Application.isEditor);

        void OnEnable()
        {
            if (UseSimulation)
            {
                _sim = new LatLon(simulatedLatitude, simulatedLongitude);
                Current = _sim;
                HasFix = true;
                SetStatus(GpsStatus.Running);
            }
            else
            {
                StartCoroutine(StartDeviceLocation());
            }
        }

        /// <summary>
        /// Drives the device location pipeline and keeps retrying until it gets a fix:
        /// requests the runtime permission (re-asking each loop if it's still missing),
        /// waits for location services to be enabled, then starts the service and waits
        /// for the first reading. <see cref="Status"/> is updated throughout so the UI
        /// can show the right message.
        /// </summary>
        IEnumerator StartDeviceLocation()
        {
            while (true)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
                {
                    SetStatus(GpsStatus.RequestingPermission);
                    yield return RequestAndroidLocationPermission();

                    if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
                    {
                        // Refused (possibly "don't ask again"). Show the message and ask
                        // again after a beat — the dialog re-appears unless permanently
                        // denied, in which case the overlay's "Open settings" helps.
                        SetStatus(GpsStatus.PermissionDenied);
                        yield return new WaitForSeconds(retryInterval);
                        continue;
                    }
                }
#endif
                if (!Input.location.isEnabledByUser)
                {
                    SetStatus(GpsStatus.Disabled);
                    yield return new WaitForSeconds(retryInterval);
                    continue;
                }

                SetStatus(GpsStatus.WaitingForSignal);
                Input.location.Start(desiredAccuracy, updateDistance);

                int guard = 20; // up to ~20s for a first fix
                while (Input.location.status == LocationServiceStatus.Initializing && guard-- > 0)
                    yield return new WaitForSeconds(1f);

                if (Input.location.status != LocationServiceStatus.Running)
                {
                    Debug.LogWarning($"[GPS] Location service failed to start (status: {Input.location.status}); retrying.");
                    Input.location.Stop();
                    yield return new WaitForSeconds(retryInterval);
                    continue; // back to the top — re-checks permission and the OS toggle too
                }

                var d = Input.location.lastData;
                Current = new LatLon(d.latitude, d.longitude);
                HasFix = true;
                SetStatus(GpsStatus.Running);
                yield break;
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Shows the Android location-permission dialog and waits for the user's answer.</summary>
        IEnumerator RequestAndroidLocationPermission()
        {
            bool answered = false;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => answered = true;
            callbacks.PermissionDenied += _ => answered = true;
            callbacks.PermissionDeniedAndDontAskAgain += _ => answered = true;
            Permission.RequestUserPermission(Permission.FineLocation, callbacks);

            // Guard in case no callback ever fires (e.g. user navigates away).
            float waited = 0f;
            while (!answered && waited < 30f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }
#endif

        // How often, once running, we re-check that we still have permission + a service.
        const float PermissionCheckInterval = 2f;
        float _permissionCheckTimer;

        void Update()
        {
            if (UseSimulation)
            {
                UpdateSimulated();
                return;
            }

            // Once we have a fix, keep an eye on the permission / service being pulled
            // out from under us (e.g. the user revokes it from system settings).
            if (Status == GpsStatus.Running)
            {
                _permissionCheckTimer += Time.unscaledDeltaTime;
                if (_permissionCheckTimer >= PermissionCheckInterval)
                {
                    _permissionCheckTimer = 0f;
                    if (LocationLost()) { HandleRevoked(); return; }
                }
            }

            if (Input.location.status == LocationServiceStatus.Running)
            {
                var d = Input.location.lastData;
                Current = new LatLon(d.latitude, d.longitude);
                HasFix = true;
            }
        }

        // Catch a revocation done while we were backgrounded, the moment we return.
        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && !UseSimulation && Status == GpsStatus.Running && LocationLost())
                HandleRevoked();
        }

        /// <summary>True if the permission or location service we were relying on is gone.</summary>
        bool LocationLost()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation)) return true;
#endif
            if (!Input.location.isEnabledByUser) return true;
            return Input.location.status == LocationServiceStatus.Failed ||
                   Input.location.status == LocationServiceStatus.Stopped;
        }

        /// <summary>Stop trusting our position and surface the "permission revoked" popup.</summary>
        void HandleRevoked()
        {
            HasFix = false;
            Input.location.Stop();
            StopAllCoroutines(); // cancel any in-flight (re)acquisition
            SetStatus(GpsStatus.PermissionRevoked);
        }

        /// <summary>
        /// Called by the revoked-permission popup's accept button: re-request the
        /// permission and, if granted, resume acquiring a fix. If the user refuses
        /// again we drop back to the popup so they can try once more.
        /// </summary>
        public void RequestPermissionAndResume()
        {
            StopAllCoroutines();
            StartCoroutine(ResumeAfterRevoke());
        }

        IEnumerator ResumeAfterRevoke()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                SetStatus(GpsStatus.RequestingPermission);
                yield return RequestAndroidLocationPermission();

                if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
                {
                    SetStatus(GpsStatus.PermissionRevoked); // still refused — show the popup again
                    yield break;
                }
            }
#endif
            // Permission is good again; run the normal acquisition loop (handles a
            // disabled service, waiting for signal, etc.) until we're Running.
            yield return StartDeviceLocation();
        }

        void UpdateSimulated()
        {
            float x = 0f, y = 0f;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
            }
#else
            x = Input.GetAxisRaw("Horizontal");
            y = Input.GetAxisRaw("Vertical");
#endif
            if (Mathf.Approximately(x, 0f) && Mathf.Approximately(y, 0f)) return;

            // Rotate the (right, forward) input by the camera heading so "forward"
            // follows the look direction. World +Z is north, +X is east.
            float yaw = headingReference != null ? headingReference.eulerAngles.y : 0f;
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * new Vector3(x, 0f, y);
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            // Step a metres/second velocity, converted to a lat/lon delta.
            double metresNorth = dir.z * simulatedSpeed * Time.deltaTime;
            double metresEast = dir.x * simulatedSpeed * Time.deltaTime;
            const double metresPerDegLat = 111320.0;
            double dLat = metresNorth / metresPerDegLat;
            double dLon = metresEast / (metresPerDegLat * System.Math.Cos(_sim.Latitude * System.Math.PI / 180.0));

            _sim = new LatLon(_sim.Latitude + dLat, _sim.Longitude + dLon);
            Current = _sim;
            HasFix = true;
        }
    }
}
