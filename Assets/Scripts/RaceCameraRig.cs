using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MarbleArena
{
    public sealed class RaceCameraRig : MonoBehaviour
    {
        const int TrackMask = 1 << 8;
        const float CameraRadius = 0.24f;
        const float MinimumTargetDistance = 1.6f;
        const float MinimumOtherMarbleDistance = 1.6f;
        const float ShotDuration = 5.5f;
        static readonly Vector3 ArenaFocus = new Vector3(0f, 17.5f, 0f);
        static readonly Vector3 BowlCenter = new Vector3(-5f, 0f, 10f);
        static readonly Color Midnight = new Color(0.006f, 0.009f, 0.022f);
        static readonly Vector3[] EscapeOffsets =
        {
            new Vector3(0f, 2.3f, -2.6f), new Vector3(0f, 3.1f, -1.1f),
            new Vector3(1.7f, 2f, -2f), new Vector3(-1.7f, 2f, -2f),
            new Vector3(0f, 3.4f, 0f), new Vector3(0f, 1.8f, 2.8f),
            new Vector3(2.6f, 1.2f, 0f), new Vector3(-2.6f, 1.2f, 0f),
            new Vector3(0f, 0.55f, -3.6f), new Vector3(0f, 0.55f, 3.6f),
            new Vector3(2.4f, 0.6f, -2.4f), new Vector3(-2.4f, 0.6f, -2.4f),
            new Vector3(0f, 4.8f, -0.8f)
        };

        public Camera Camera { get; private set; }
        public int Mode { get; private set; }

        ArenaBuilder _arena;
        MarbleRacer _lastTarget;
        MarbleRacer _lightingTarget;
        Light _keyLight;
        Light _coralLight;
        Light _blueLight;
        Light _parkLight;
        Bloom _bloom;
        VolumeProfile _profile;
        ReflectionProbe _reflectionProbe;
        Material _previousSkybox;
        Light _previousSun;
        Vector3 _heading = Vector3.forward;
        Vector3 _lookPoint;
        Vector3 _lastTargetPosition;
        Vector3 _lightFocus;
        Vector3 _coralFocus;
        Vector3 _blueFocus;
        Vector3 _parkFocus;
        Vector3 _bowlViewSide;
        Vector3 _lastSafePosition;
        float _headingSlope;
        float _orbitYaw = 0.78f;
        float _orbitPitch = 0.53f;
        float _orbitDistance = 70f;
        float _manualOrbitHold;
        float _shotClock;
        float _battleHold;
        float _captureDelay;
        int _shotIndex;
        bool _highQuality = true;
        bool _headingReady;
        bool _poseReady;
        bool _reflectionRequested;
        bool _bowlViewReady;
        bool _safePositionReady;
        string _lastLivePhase = "menu";

        public void Setup(ArenaBuilder arena)
        {
            _arena = arena;
            if (Camera != null)
                return;

            var lens = CreateChild("Race Lens");
            lens.tag = "MainCamera";
            Camera = lens.AddComponent<Camera>();
            Camera.nearClipPlane = 0.09f;
            Camera.farClipPlane = 240f;
            Camera.fieldOfView = 58f;
            Camera.allowHDR = true;
            Camera.allowMSAA = true;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Midnight;
            var cameraData = Camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.renderShadows = true;
            cameraData.volumeLayerMask = ~0;
            if (FindAnyObjectByType<AudioListener>() == null)
                lens.AddComponent<AudioListener>();

            CreateLighting();
            CreatePostProcessing();
            CreateReflectionProbe();
            SetQuality(_highQuality);

            _lookPoint = GetArenaFocus();
            Camera.transform.SetPositionAndRotation(
                OrbitPosition(_lookPoint),
                Quaternion.LookRotation(_lookPoint - OrbitPosition(_lookPoint), Vector3.up));
            _poseReady = true;
        }

        public void SetMode(int mode)
        {
            Mode = Mathf.Clamp(mode, 0, 2);
            _shotClock = 0f;
        }

        public void SetQuality(bool high)
        {
            _highQuality = high;
            if (_keyLight != null)
                _keyLight.shadows = high ? LightShadows.Soft : LightShadows.Hard;
            if (_bloom != null)
                _bloom.active = high;
        }

        public void Orbit(float dx, float dy)
        {
            if (!IsFinite(dx) || !IsFinite(dy))
                return;
            _orbitYaw -= dx * 0.006f;
            _orbitPitch = Mathf.Clamp(_orbitPitch + dy * 0.0045f, 0.28f, 1.23f);
            _manualOrbitHold = 5f;
        }

        public void Zoom(float amount)
        {
            if (!IsFinite(amount))
                return;
            _orbitDistance = Mathf.Clamp(_orbitDistance + amount * 3f, 40f, 115f);
            _manualOrbitHold = 3f;
        }

        public void UpdateCamera(float delta, string phase, MarbleRacer target, List<MarbleRacer> racers)
        {
            if (Camera == null || !IsFinite(delta))
                return;
            float step = Mathf.Clamp(delta > 0f ? delta : phase == "paused" ? Time.unscaledDeltaTime : 0f, 0f, 0.1f);
            _manualOrbitHold = Mathf.Max(0f, _manualOrbitHold - step);
            CaptureReflectionsOnce(step);
            if (phase != "paused")
                _lastLivePhase = phase;
            string viewPhase = phase == "paused" ? _lastLivePhase : phase;
            _lightingTarget = target;
            bool follow = target != null && Mode != 2 && (viewPhase == "racing" || viewPhase == "countdown");

            Vector3 focus = GetArenaFocus();
            Vector3 desiredPosition = OrbitPosition(focus);
            Vector3 desiredLook = focus;
            Vector3 anchor = Vector3.zero;
            Vector3 trackingMotion = Vector3.zero;
            bool onBowlSurface = false;
            float desiredFov = 58f;
            float response = 3f;

            if (follow)
            {
                Vector3 targetPosition = target.transform.position;
                if (_lastTarget != target)
                {
                    if (_lastTarget != null && (targetPosition - _lastTargetPosition).sqrMagnitude > 64f)
                        _poseReady = false;
                    _lastTarget = target;
                    _headingReady = false;
                    _bowlViewReady = false;
                    _safePositionReady = false;
                    _lastTargetPosition = targetPosition;
                }
                else if ((targetPosition - _lastTargetPosition).sqrMagnitude > 676f)
                {
                    _poseReady = false;
                }
                else
                {
                    trackingMotion = targetPosition - _lastTargetPosition;
                    trackingMotion.y *= 0.35f;
                }
                _lastTargetPosition = targetPosition;

                Vector3 velocity = target.Body != null ? target.Body.linearVelocity : Vector3.zero;
                float speed = velocity.magnitude;
                UpdateHeading(velocity, target.TrackTangent, step);
                Vector3 lookDirection = new Vector3(_heading.x, _headingSlope, _heading.z).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, _heading);
                anchor = targetPosition + Vector3.up * 0.28f;
                onBowlSurface = target.InBowl && target.Sector != "funnel_drop" && target.Sector != "catch_cup"
                    && Vector3.ProjectOnPlane(targetPosition - GetBowlCenter(), Vector3.up).sqrMagnitude < 36f;
                bool inThroat = target.InBowl && target.Sector == "funnel_drop";
                bool inCatchCup = target.InBowl && target.Sector == "catch_cup";
                if (!onBowlSurface)
                    _bowlViewReady = false;

                if (inThroat)
                {
                    desiredPosition = targetPosition - _heading * 0.55f + Vector3.up * 2.85f;
                    Vector3 center = GetBowlCenter();
                    Vector3 radial = Vector3.ProjectOnPlane(desiredPosition - center, Vector3.up);
                    if (radial.magnitude > 0.9f)
                        desiredPosition += radial.normalized * (0.9f - radial.magnitude);
                    desiredLook = targetPosition + _heading * 0.25f + Vector3.down * 0.2f;
                    desiredFov = Mode == 1 ? 90f : 80f;
                    response = 9f;
                }
                else if (Mode == 1)
                {
                    float distance = Mathf.Clamp(3.05f + speed * 0.008f, 3.05f, 3.2f);
                    desiredPosition = targetPosition - _heading * distance + Vector3.up * (1.8f + Mathf.Min(speed * 0.007f, 0.2f));
                    desiredLook = targetPosition + lookDirection * Mathf.Clamp(1.2f + speed * 0.045f, 1.2f, 2.4f) + Vector3.up * 0.4f;
                    desiredFov = 86f + Mathf.Clamp(speed * 0.22f, 0f, 6f);
                    response = 10f;
                    if (onBowlSurface)
                    {
                        Vector3 inward = Vector3.ProjectOnPlane(GetBowlCenter() - targetPosition, Vector3.up).normalized;
                        desiredPosition += inward * 0.75f;
                        desiredLook = targetPosition + _heading * 1.1f + inward * 0.25f + Vector3.up * 0.35f;
                    }
                    else if (inCatchCup)
                    {
                        desiredPosition = targetPosition - _heading * 2.8f + Vector3.up * 0.9f;
                    }
                }
                else if (viewPhase == "countdown")
                {
                    desiredPosition = targetPosition - _heading * 5.6f + right * 3.2f + Vector3.up * 3.5f;
                    desiredLook = PackFocus(target, racers, out _) + _heading * 0.7f;
                    desiredFov = 62f;
                    response = 2.6f;
                }
                else if (onBowlSurface)
                {
                    Vector3 center = GetBowlCenter();
                    if (!_bowlViewReady)
                    {
                        Vector3 radial = Vector3.ProjectOnPlane(targetPosition - center, Vector3.up).normalized;
                        _bowlViewSide = radial.sqrMagnitude > 0.01f
                            ? (-radial + Vector3.Cross(Vector3.up, radial) * 0.35f).normalized
                            : new Vector3(0.94f, 0f, -0.34f);
                        _bowlViewReady = true;
                    }
                    desiredLook = BowlFocus(target, racers, out float groupRadius);
                    float framingDistance = Mathf.Clamp(groupRadius * 2.05f, 4.8f, 7.4f);
                    desiredPosition = desiredLook + _bowlViewSide * (framingDistance * 0.65f) + Vector3.up * (framingDistance * 0.76f);
                    desiredFov = 60f;
                    trackingMotion = Vector3.zero;
                    response = 6.5f;
                }
                else if (inCatchCup)
                {
                    desiredPosition = targetPosition - _heading * 2.8f + right * 0.55f + Vector3.up * 1f;
                    desiredLook = targetPosition + _heading * 0.8f + Vector3.up * 0.25f;
                    desiredFov = 72f;
                    response = 7f;
                }
                else
                {
                    if (phase != "paused")
                    {
                        _shotClock += step;
                        if (_shotClock >= ShotDuration)
                        {
                            _shotClock %= ShotDuration;
                            _shotIndex = (_shotIndex + 1) % 4;
                        }
                    }
                    desiredLook = PackFocus(target, racers, out int nearbyRacers) + lookDirection * 0.65f;
                    _battleHold = nearbyRacers >= 2 ? 2.5f : Mathf.Max(0f, _battleHold - step);
                    int shot = _battleHold > 0f ? _shotIndex % 2 : _shotIndex;
                    switch (shot)
                    {
                        case 0:
                            desiredPosition = targetPosition - _heading * 4.3f + right * 2.8f + Vector3.up * 2.7f;
                            break;
                        case 1:
                            desiredPosition = targetPosition - _heading * 2.6f - right * 4.4f + Vector3.up * 4.1f;
                            break;
                        case 2:
                            desiredPosition = targetPosition - _heading * 4.5f + right * 0.8f + Vector3.up * 1.65f;
                            break;
                        default:
                            desiredPosition = targetPosition - _heading * 0.8f + right * 5.2f + Vector3.up * 3.3f;
                            break;
                    }
                    desiredFov = shot == 1 ? 62f + Mathf.Min(speed * 0.08f, 3f) : 60f + Mathf.Min(speed * 0.1f, 4f);
                    response = 3.7f;
                }
                if (onBowlSurface)
                    desiredPosition = ClearBowlSurface(desiredPosition);
                desiredPosition = ResolveObstruction(anchor, desiredPosition, right, onBowlSurface, target, racers, trackingMotion);
                if (Mode == 1 && (desiredPosition - targetPosition).sqrMagnitude < 6.25f)
                {
                    float closeLook = Mathf.Clamp((desiredPosition - targetPosition).magnitude * 0.42f, 0.65f, 1.05f);
                    desiredLook = targetPosition + lookDirection * closeLook + Vector3.up * 0.28f;
                    desiredFov = Mathf.Max(desiredFov, 94f);
                }
            }
            else
            {
                if ((viewPhase == "menu" || viewPhase == "results") && _manualOrbitHold <= 0f)
                    _orbitYaw += step * (viewPhase == "results" ? 0.027f : 0.036f);
                if (viewPhase == "results")
                {
                    desiredLook += Vector3.up * 0.6f;
                    desiredFov = 55f;
                }
                desiredPosition = OrbitPosition(desiredLook);
            }

            float positionBlend = 1f - Mathf.Exp(-response * step);
            float lookResponse = onBowlSurface && Mode == 0 ? 12f : Mathf.Max(response, 4f);
            float lookBlend = 1f - Mathf.Exp(-lookResponse * step);
            Vector3 position;
            if (!_poseReady)
            {
                position = desiredPosition;
                _lookPoint = desiredLook;
                _poseReady = true;
            }
            else
            {
                position = Vector3.Lerp(Camera.transform.position + trackingMotion, desiredPosition, positionBlend);
                _lookPoint = Vector3.Lerp(_lookPoint + trackingMotion, desiredLook, lookBlend);
            }
            if (follow)
            {
                bool interpolatedSafe = TrySafePosition(anchor, position, onBowlSurface, target, racers, out Vector3 clearPosition);
                if (!interpolatedSafe || (clearPosition - target.transform.position).sqrMagnitude < 4.84f
                    && (desiredPosition - target.transform.position).sqrMagnitude >= 4.84f)
                {
                    position = desiredPosition;
                    _lookPoint = desiredLook;
                }
                else
                    position = clearPosition;
                if (IsSafePosition(position, target, racers))
                {
                    _lastSafePosition = position;
                    _safePositionReady = true;
                }
            }

            Vector3 viewDirection = _lookPoint - position;
            Quaternion rotation = Camera.transform.rotation;
            if (viewDirection.sqrMagnitude > 0.01f)
            {
                Vector3 up = Mathf.Abs(Vector3.Dot(viewDirection.normalized, Vector3.up)) > 0.985f ? Vector3.forward : Vector3.up;
                rotation = Quaternion.LookRotation(viewDirection, up);
            }
            float fieldOfView = Mathf.Lerp(Camera.fieldOfView, desiredFov, 1f - Mathf.Exp(-3.5f * step));
            if (follow)
            {
                Vector3 localTarget = Quaternion.Inverse(rotation) * (target.transform.position - position);
                float halfHeight = Mathf.Max(0.01f, localTarget.z * Mathf.Tan(fieldOfView * Mathf.Deg2Rad * 0.5f));
                if (localTarget.z <= 0f || Mathf.Abs(localTarget.x) > halfHeight * Camera.aspect * 0.72f
                    || Mathf.Abs(localTarget.y) > halfHeight * 0.6f)
                {
                    _lookPoint = target.transform.position + Vector3.up * 0.1f;
                    Vector3 direction = (_lookPoint - position).normalized;
                    Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.985f ? Vector3.forward : Vector3.up;
                    rotation = Quaternion.LookRotation(direction, up);
                }
            }
            Camera.transform.SetPositionAndRotation(position, rotation);
            Camera.fieldOfView = fieldOfView;
        }

        public void UpdateShowLighting(float raceTime, float progress, string phase)
        {
            if (_keyLight == null || phase == "paused")
                return;
            raceTime = IsFinite(raceTime) ? Mathf.Max(0f, raceTime) : 0f;
            progress = IsFinite(progress) ? Mathf.Clamp01(progress) : 0f;
            bool racing = phase == "racing";
            bool celebrating = phase == "results";
            float blend = 1f - Mathf.Exp(-1.6f * Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f));
            Vector3 focus = GetArenaFocus();
            if ((racing || phase == "countdown") && _lightingTarget != null)
                focus = _lightingTarget.transform.position + Vector3.up * 0.3f;
            else if (celebrating)
                focus = TrackFocus(0.985f) + Vector3.up;
            _lightFocus = Vector3.Lerp(_lightFocus, focus, blend);
            AimLight(_keyLight, _lightFocus + new Vector3(-9f, 17f, -8f), _lightFocus);

            float middle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.52f, progress));
            float finale = celebrating ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.76f, 1f, progress));
            Color coral = Color.Lerp(new Color(1f, 0.38f, 0.28f), new Color(0.76f, 0.38f, 1f), middle);
            Color blue = Color.Lerp(new Color(0.22f, 0.64f, 1f), new Color(0.3f, 0.83f, 0.94f), middle);
            coral = Color.Lerp(coral, new Color(1f, 0.57f, 0.31f), finale);
            blue = Color.Lerp(blue, new Color(0.42f, 0.7f, 1f), finale);
            _coralLight.color = Color.Lerp(_coralLight.color, coral, blend);
            _blueLight.color = Color.Lerp(_blueLight.color, blue, blend);
            _parkLight.color = Color.Lerp(_parkLight.color, Color.Lerp(new Color(1f, 0.77f, 0.48f), new Color(1f, 0.46f, 0.37f), finale), blend);
            _keyLight.color = Color.Lerp(_keyLight.color, celebrating ? new Color(1f, 0.94f, 0.84f) : new Color(1f, 0.97f, 0.91f), blend);

            float breathing = racing ? 0.96f + 0.04f * Mathf.Sin(raceTime * 0.7f) : 1f;
            _keyLight.intensity = Mathf.Lerp(_keyLight.intensity, celebrating ? 480f : 400f, blend);
            _coralLight.intensity = Mathf.Lerp(_coralLight.intensity, Mathf.Lerp(300f, 360f, finale) * breathing, blend);
            _blueLight.intensity = Mathf.Lerp(_blueLight.intensity, Mathf.Lerp(280f, 235f, finale) * breathing, blend);
            _parkLight.intensity = Mathf.Lerp(_parkLight.intensity, celebrating ? 365f : 300f, blend);

            float sweep = racing ? Mathf.Sin(raceTime * 0.18f) * 3f : 0f;
            AimLight(_coralLight, _coralFocus + new Vector3(-16f, 17f, 8f), _coralFocus + Vector3.right * sweep);
            AimLight(_blueLight, _blueFocus + new Vector3(16f, 18f, 9f), _blueFocus - Vector3.forward * sweep);
        }

        void CreateLighting()
        {
            _previousSkybox = RenderSettings.skybox;
            _previousSun = RenderSettings.sun;
            RenderSettings.skybox = null;
            RenderSettings.sun = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.15f, 0.18f, 0.26f);
            RenderSettings.ambientEquatorColor = new Color(0.11f, 0.12f, 0.17f);
            RenderSettings.ambientGroundColor = new Color(0.045f, 0.045f, 0.065f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 128;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = Midnight;
            RenderSettings.fogDensity = 0.0016f;

            _lightFocus = GetArenaFocus();
            _coralFocus = TrackFocus(0.14f);
            _blueFocus = TrackFocus(0.58f);
            _parkFocus = (_arena != null ? _arena.transform.position : Vector3.zero) + Vector3.up * 2.5f;
            _keyLight = CreateShowSpot("Warm White Follow Spot", new Color(1f, 0.97f, 0.91f), 400f, 64f, 90f);
            AimLight(_keyLight, _lightFocus + new Vector3(-9f, 17f, -8f), _lightFocus);
            _keyLight.shadows = LightShadows.Soft;
            _keyLight.shadowStrength = 0.85f;
            _keyLight.shadowBias = 0.02f;
            _keyLight.shadowNormalBias = 0.08f;
            _keyLight.shadowNearPlane = 0.3f;

            _coralLight = CreateShowSpot("Coral Pavilion Spot", new Color(1f, 0.38f, 0.28f), 300f, 62f, 112f);
            _blueLight = CreateShowSpot("Blue Boardwalk Spot", new Color(0.22f, 0.64f, 1f), 280f, 62f, 112f);
            _parkLight = CreateShowSpot("Carousel Footlights", new Color(1f, 0.77f, 0.48f), 300f, 72f, 115f);
            AimLight(_coralLight, _coralFocus + new Vector3(-16f, 17f, 8f), _coralFocus);
            AimLight(_blueLight, _blueFocus + new Vector3(16f, 18f, 9f), _blueFocus);
            AimLight(_parkLight, _parkFocus + new Vector3(12f, 24f, -14f), _parkFocus);

            Light fill = CreateChild("Neutral Park Fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.transform.rotation = Quaternion.Euler(42f, -38f, 0f);
            fill.color = new Color(0.94f, 0.96f, 1f);
            fill.intensity = 0.34f;
            fill.shadows = LightShadows.None;
        }

        Light CreateShowSpot(string objectName, Color color, float intensity, float range, float angle)
        {
            Light light = CreateChild(objectName).AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = angle;
            light.innerSpotAngle = angle * 0.72f;
            light.shadows = LightShadows.None;
            light.cullingMask = ~(1 << 5);
            return light;
        }

        static void AimLight(Light light, Vector3 position, Vector3 focus)
        {
            light.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
        }

        Vector3 TrackFocus(float progress)
        {
            return _arena != null && _arena.TotalLength > 0f
                ? _arena.transform.TransformPoint(_arena.SampleDistance(_arena.TotalLength * progress).Position)
                : GetArenaFocus();
        }

        void CreatePostProcessing()
        {
            var volume = CreateChild("Arena Color").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "Midnight Carnival Grade";
            volume.sharedProfile = _profile;

            _bloom = _profile.Add<Bloom>(true);
            _bloom.threshold.Override(1.3f);
            _bloom.intensity.Override(0.08f);
            _bloom.scatter.Override(0.45f);
            _bloom.highQualityFiltering.Override(false);
            var color = _profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0f);
            color.contrast.Override(9f);
            color.saturation.Override(8f);
            var vignette = _profile.Add<Vignette>(true);
            vignette.color.Override(Midnight);
            vignette.intensity.Override(0.12f);
            vignette.smoothness.Override(0.5f);
            var tone = _profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
        }

        void CreateReflectionProbe()
        {
            var probeObject = CreateChild("Static Arena Reflection");
            probeObject.SetActive(false);
            _reflectionProbe = probeObject.AddComponent<ReflectionProbe>();
            _reflectionProbe.transform.position = GetArenaFocus();
            _reflectionProbe.mode = ReflectionProbeMode.Realtime;
            _reflectionProbe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            _reflectionProbe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            _reflectionProbe.resolution = 256;
            _reflectionProbe.hdr = true;
            _reflectionProbe.boxProjection = true;
            _reflectionProbe.size = new Vector3(110f, 70f, 110f);
            _reflectionProbe.center = Vector3.zero;
            _reflectionProbe.blendDistance = 5f;
            _reflectionProbe.nearClipPlane = 0.25f;
            _reflectionProbe.farClipPlane = 160f;
            _reflectionProbe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            _reflectionProbe.backgroundColor = Midnight;
            _reflectionProbe.cullingMask = ~((1 << 9) | (1 << 5));
            _reflectionProbe.intensity = 1f;
            probeObject.SetActive(true);
        }

        void CaptureReflectionsOnce(float delta)
        {
            if (_reflectionRequested || _reflectionProbe == null)
                return;
            _captureDelay += delta;
            if (_captureDelay < 0.15f)
                return;
            _reflectionProbe.RenderProbe();
            _reflectionRequested = true;
        }

        void UpdateHeading(Vector3 velocity, Vector3 tangent, float delta)
        {
            Vector3 direction = velocity.sqrMagnitude >= 1.4f ? velocity : tangent;
            if (direction.sqrMagnitude < 0.001f)
                return;
            direction.Normalize();
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude > 0.025f)
            {
                flat.Normalize();
                _heading = _headingReady ? Vector3.RotateTowards(_heading, flat, 4.8f * delta, 0f).normalized : flat;
                _headingReady = true;
            }
            _headingSlope = Mathf.Lerp(_headingSlope, Mathf.Clamp(direction.y, -0.7f, 0.6f), 1f - Mathf.Exp(-3.5f * delta));
        }

        static Vector3 PackFocus(MarbleRacer target, List<MarbleRacer> racers, out int nearbyRacers)
        {
            Vector3 position = target.transform.position;
            Vector3 sum = position * 2f;
            float totalWeight = 2f;
            nearbyRacers = 0;
            if (racers != null)
            {
                for (int i = 0; i < racers.Count; i++)
                {
                    MarbleRacer racer = racers[i];
                    if (racer == null || racer == target)
                        continue;
                    Vector3 other = racer.transform.position;
                    float distance = (other - position).magnitude;
                    if (distance < 6f && Mathf.Abs(other.y - position.y) < 1.8f)
                    {
                        float weight = 1f - distance / 6f;
                        sum += other * weight;
                        totalWeight += weight;
                        nearbyRacers++;
                    }
                }
            }
            return Vector3.Lerp(position, sum / totalWeight, 0.65f) + Vector3.up * 0.3f;
        }

        static Vector3 BowlFocus(MarbleRacer target, List<MarbleRacer> racers, out float groupRadius)
        {
            Vector3 position = target.transform.position;
            Vector3 padding = Vector3.one * MarbleRacer.Radius;
            Bounds group = new Bounds(position, padding * 2f);
            if (racers != null)
            {
                for (int i = 0; i < racers.Count; i++)
                {
                    MarbleRacer racer = racers[i];
                    if (racer == null || racer == target || !racer.InBowl
                        || racer.Sector == "funnel_drop" || racer.Sector == "catch_cup")
                        continue;
                    Vector3 other = racer.transform.position;
                    if ((other - position).sqrMagnitude < 23.04f && Mathf.Abs(other.y - position.y) < 2.2f)
                    {
                        group.Encapsulate(other - padding);
                        group.Encapsulate(other + padding);
                    }
                }
            }
            groupRadius = group.extents.magnitude;
            return group.center + Vector3.up * 0.1f;
        }

        Vector3 ClearBowlSurface(Vector3 position)
        {
            Vector3 center = GetBowlCenter();
            Vector3 radial = Vector3.ProjectOnPlane(position - center, Vector3.up);
            float radius = radial.magnitude;
            bool inMouth = radial.x > -8.2f && radial.x < -4.7f
                && Mathf.Abs(radial.z) < Mathf.Min(2.1f, -radial.x * 0.36f);
            if (radius > 5.42f && !inMouth)
            {
                position += radial.normalized * (5.42f - radius);
                radius = 5.42f;
            }
            float surface = center.y + 12f + 5f * Mathf.Clamp01((radius - 1.2f) / 4.6f);
            if (inMouth)
                surface = Mathf.Max(surface, center.y + 17.5f);
            position.y = Mathf.Max(position.y, surface + CameraRadius + 0.18f);
            return position;
        }

        Vector3 ResolveObstruction(Vector3 anchor, Vector3 desired, Vector3 right, bool bowlSurface,
            MarbleRacer target, List<MarbleRacer> racers, Vector3 trackingMotion)
        {
            Vector3 targetPosition = target.transform.position;
            bool preferredSafe = TrySafePosition(anchor, desired, bowlSurface, target, racers, out Vector3 preferred);
            if (preferredSafe && (preferred - targetPosition).sqrMagnitude >= 4.84f)
                return preferred;
            if (_safePositionReady && TrySafePosition(anchor, _lastSafePosition + trackingMotion, bowlSurface, target, racers, out Vector3 previous)
                && (previous - targetPosition).sqrMagnitude >= 4.84f)
                return previous;
            for (int i = 0; i < EscapeOffsets.Length; i++)
            {
                Vector3 offset = EscapeOffsets[i];
                Vector3 candidate = targetPosition + right * offset.x + Vector3.up * offset.y + _heading * offset.z;
                if (TrySafePosition(anchor, candidate, bowlSurface, target, racers, out Vector3 safe)
                    && (safe - targetPosition).sqrMagnitude >= 4.84f)
                    return safe;
            }
            if (preferredSafe)
                return preferred;
            for (int elevation = 0; elevation < 3; elevation++)
            for (int direction = 0; direction < 12; direction++)
            {
                float angle = direction * Mathf.PI / 6f;
                Vector3 candidate = targetPosition + (right * Mathf.Sin(angle) + _heading * Mathf.Cos(angle)) * 3.8f
                    + Vector3.up * (0.6f + elevation * 1.2f);
                if (TrySafePosition(anchor, candidate, bowlSurface, target, racers, out Vector3 safe))
                    return safe;
            }
            Vector3 overview = OrbitPosition(GetArenaFocus());
            return (overview - targetPosition).sqrMagnitude >= MinimumTargetDistance * MinimumTargetDistance
                ? overview : targetPosition + Vector3.up * 8f;
        }

        bool TrySafePosition(Vector3 anchor, Vector3 candidate, bool bowlSurface, MarbleRacer target,
            List<MarbleRacer> racers, out Vector3 clear)
        {
            if (bowlSurface)
                candidate = ClearBowlSurface(candidate);
            clear = CastPosition(anchor, candidate);
            return IsSafePosition(clear, target, racers);
        }

        static bool IsSafePosition(Vector3 position, MarbleRacer target, List<MarbleRacer> racers)
        {
            if ((position - target.transform.position).sqrMagnitude < MinimumTargetDistance * MinimumTargetDistance)
                return false;
            if (racers != null)
            {
                for (int i = 0; i < racers.Count; i++)
                {
                    MarbleRacer racer = racers[i];
                    if (racer != null && racer != target
                        && (position - racer.transform.position).sqrMagnitude < MinimumOtherMarbleDistance * MinimumOtherMarbleDistance)
                        return false;
                }
            }
            return !Physics.CheckSphere(position, CameraRadius, TrackMask, QueryTriggerInteraction.Ignore)
                && !Physics.Linecast(target.transform.position, position, TrackMask, QueryTriggerInteraction.Ignore)
                && !Physics.Linecast(position, target.transform.position, TrackMask, QueryTriggerInteraction.Ignore);
        }

        static Vector3 CastPosition(Vector3 anchor, Vector3 desired)
        {
            Vector3 offset = desired - anchor;
            float distance = offset.magnitude;
            if (distance < 0.001f)
                return desired;
            Vector3 direction = offset / distance;
            float allowed = distance;
            if (Physics.SphereCast(anchor, CameraRadius, direction, out RaycastHit hit, distance, TrackMask, QueryTriggerInteraction.Ignore))
                allowed = Mathf.Min(allowed, hit.distance - 0.06f);
            if (Physics.Raycast(desired, -direction, out hit, distance, TrackMask, QueryTriggerInteraction.Ignore))
            {
                float clearance = CameraRadius / Mathf.Max(0.15f, Mathf.Abs(Vector3.Dot(direction, hit.normal)));
                allowed = Mathf.Min(allowed, distance - hit.distance - clearance - 0.06f);
            }
            return anchor + direction * Mathf.Max(0f, allowed);
        }

        Vector3 GetArenaFocus()
        {
            return (_arena != null ? _arena.transform.position : Vector3.zero) + ArenaFocus;
        }

        Vector3 GetBowlCenter()
        {
            return _arena != null ? _arena.transform.TransformPoint(BowlCenter) : BowlCenter;
        }

        Vector3 OrbitPosition(Vector3 focus)
        {
            return focus + new Vector3(Mathf.Sin(_orbitYaw) * Mathf.Cos(_orbitPitch), Mathf.Sin(_orbitPitch), Mathf.Cos(_orbitYaw) * Mathf.Cos(_orbitPitch)) * _orbitDistance;
        }

        GameObject CreateChild(string objectName)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            return child;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        void OnDestroy()
        {
            if (RenderSettings.skybox == null)
                RenderSettings.skybox = _previousSkybox;
            if (_profile != null)
            {
                for (int i = 0; i < _profile.components.Count; i++)
                    Destroy(_profile.components[i]);
                Destroy(_profile);
            }
            if (RenderSettings.sun == null)
                RenderSettings.sun = _previousSun;
        }
    }
}
