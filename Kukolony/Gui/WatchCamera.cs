using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Gui
{
    /// <summary>
    ///     Watches a villager: the camera orbits them, and nothing else moves.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The player does not go anywhere.</b> A settlement worth watching is usually
    ///         somewhere the player is not, and teleporting to look would cost the walk back -
    ///         so this moves the camera alone and leaves the body standing where it was.
    ///     </para>
    ///     <para>
    ///         <b>Its own orbit rather than the game's.</b> The player's camera reads its angles
    ///         off the player - the yaw and pitch are the player's own look, and the camera is
    ///         downstream of them - so there is no target to point somewhere else. Free fly is
    ///         the other candidate and is a flying camera driven by the movement keys, which is
    ///         not what "look at this one from a few sides" means.
    ///     </para>
    ///     <para>
    ///         Input is blocked while watching, deliberately. Mouse movement has to mean orbit
    ///         rather than turning a body that is standing somewhere else entirely, and a player
    ///         holding a direction key while looking at a villager three hundred metres away
    ///         would walk off whatever they were standing on.
    ///     </para>
    ///     <para>
    ///         <b>Moving the camera moves more than the view.</b> Valheim decides what biome you
    ///         are in by asking the camera where it is - reasonable when the camera is always
    ///         behind the player, and false here - so watching somebody on a mountain put the
    ///         weather, the music and the freezing on a player standing in the meadows. The
    ///         environment is pinned to the body instead; see EnvManBiomePatch, which is where
    ///         any other "the camera is the player" assumption should be undone as it is found.
    ///     </para>
    /// </remarks>
    internal sealed class WatchCamera : MonoBehaviour
    {
        private const float TurnSpeed = 4f;

        private const float ZoomSpeed = 3f;

        private const float NearestDistance = 1.5f;

        private const float FarthestDistance = 30f;

        /// <summary>How high above their feet to look, so the camera frames a person.</summary>
        private const float EyeHeight = 1.3f;

        private static WatchCamera _instance;

        private Transform _subject;
        private string _name = string.Empty;
        private GameCamera _camera;
        private bool _cameraWasEnabled;
        private Vector3 _restorePosition;
        private Quaternion _restoreRotation;
        private float _yaw;
        private float _pitch = 15f;
        private float _distance = 6f;
        private bool _blocked;

        internal static bool Watching => _instance != null && _instance._subject != null;

        /// <summary>
        ///     Starts watching, if there is anything to watch.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         A villager with no body has nothing to look at and no world around it to
        ///         draw, so this refuses and says so rather than showing an empty void.
        ///     </para>
        ///     <para>
        ///         <b>It should almost never refuse.</b> A villager is the keep-alive's
        ///         first-priority anchor and holds its own zone plus a ring of neighbours, which
        ///         is the whole reason that system exists - so on a host every villager is
        ///         loaded, wherever it has got to. What is left is a client, where the mod holds
        ///         nothing open because the server decides what a client is sent, and a zone
        ///         budget that has bound: villagers are served before the circles, so reaching
        ///         this means the cap is far too low for the settlement's size.
        ///     </para>
        /// </remarks>
        internal static bool Watch(ZDOID villager, string name)
        {
            GameObject body = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(villager) : null;
            if (body == null)
            {
                // Not "too far away", which is what this used to say and is not the reason:
                // distance is exactly what the keep-alive exists to stop mattering. On a host
                // this means the zone budget has bound; on a client it means the server has not
                // sent this villager, which no setting here can change.
                Report.Say(ZNet.instance != null && !ZNet.instance.IsServer()
                    ? $"{name} is not loaded here - only the host holds villagers open."
                    : $"{name} is not loaded. Raise KeepAliveMaxZones if the settlement has grown.");
                return false;
            }

            if (GameCamera.instance == null) return false;

            if (_instance == null)
            {
                GameObject holder = new GameObject("KukolonyWatchCamera");
                DontDestroyOnLoad(holder);
                _instance = holder.AddComponent<WatchCamera>();
            }

            _instance.Begin(body.transform, name);
            return true;
        }

        internal static void StopWatching()
        {
            if (_instance != null) _instance.End();
        }

        private void Begin(Transform subject, string name)
        {
            if (_subject == null) Take();

            _subject = subject;
            _name = name;

            // Behind them to start, so the first frame shows a person rather than a face.
            _yaw = subject.eulerAngles.y + 180f;

            Report.Say($"Watching {name}. Escape to come back.");
        }

        private void Take()
        {
            _camera = GameCamera.instance;
            if (_camera == null) return;

            _cameraWasEnabled = _camera.enabled;
            _restorePosition = _camera.transform.position;
            _restoreRotation = _camera.transform.rotation;

            // The game's camera is switched off rather than fought with. It runs in LateUpdate
            // and writes the same transform, so both of us driving it is a camera that shakes.
            _camera.enabled = false;

            Jotunn.Managers.GUIManager.BlockInput(true);
            _blocked = true;
        }

        private void End()
        {
            if (_subject == null) return;

            _subject = null;

            if (_camera != null)
            {
                _camera.enabled = _cameraWasEnabled;
                _camera.transform.position = _restorePosition;
                _camera.transform.rotation = _restoreRotation;
            }

            if (_blocked)
            {
                Jotunn.Managers.GUIManager.BlockInput(false);
                _blocked = false;
            }
        }

        private void Update()
        {
            if (_subject == null) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                End();
                return;
            }

            // Destroyed, or its zone unloaded while being watched. Either way there is nothing
            // left to look at, and a camera orbiting a hole in the air is worse than saying so.
            if (!_subject.gameObject.activeInHierarchy)
            {
                Report.Say($"{_name} is no longer there.");
                End();
            }
        }

        /// <summary>
        ///     Places the camera, after everything else has moved.
        /// </summary>
        /// <remarks>
        ///     LateUpdate, because a villager walks in its own update and a camera positioned
        ///     before it moves trails one frame behind - which reads as a stutter at exactly the
        ///     moment somebody is watching to see whether the walking looks right.
        /// </remarks>
        private void LateUpdate()
        {
            if (_subject == null || _camera == null) return;

            _yaw += Input.GetAxis("Mouse X") * TurnSpeed;

            // Clamped short of straight up and straight down, where an orbit turns itself
            // inside out and the horizon rolls over.
            _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * TurnSpeed, -35f, 70f);

            _distance = Mathf.Clamp(_distance - Input.mouseScrollDelta.y * ZoomSpeed,
                NearestDistance, FarthestDistance);

            Vector3 focus = _subject.position + Vector3.up * EyeHeight;
            Quaternion facing = Quaternion.Euler(_pitch, _yaw, 0f);

            _camera.transform.position = focus - facing * Vector3.forward * _distance;
            _camera.transform.rotation = facing;
        }

        private void OnDestroy()
        {
            End();
            if (_instance == this) _instance = null;
        }
    }
}
