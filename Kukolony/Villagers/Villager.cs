using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Gui;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     A colony villager. Attached to the villager prefab at registration, so every
    ///     instance carries one.
    ///
    ///     This component does not drive itself. Valheim ticks creature AI through
    ///     MonoUpdaters at a fixed 0.05s step, and
    ///     <see cref="Patches.MonsterAiTickPatch" /> hands that tick here. Using the
    ///     game's own timestep is why the mod needs no coroutines and no async - the
    ///     predecessor's async AI loop is what used to crash the game.
    /// </summary>
    internal sealed class Villager : MonoBehaviour, Interactable
    {
        /// <summary>How close to home counts as home. Avoids jittering on the boundary.</summary>
        private const float HomeStopDistance = 2f;

        /// <summary>
        ///     Every loaded villager. Mirrors vanilla's BaseAI.Instances; iterating a list
        ///     beats scanning the scene, and claim
        ///     checks run inside the find step.
        /// </summary>
        internal static List<Villager> Instances { get; } = new List<Villager>();

        private MonsterAI _ai;
        private Character _character;
        private ZNetView _nview;
        private VisEquipment _visEquipment;
        private Navigation.VillagerWalk _walk;
        private VillagerAnimation _animation;
        private Container _bag;

        private bool _pathFailureReported;

        /// <summary>Short description of what this villager is doing, for hover text.</summary>
        internal string Activity { get; private set; } = "idle";

        /// <summary>
        ///     The activity a villager reports with no Kolony to work for. A constant because
        ///     three self-test assertions compare against it by value, and a wording tweak
        ///     that missed one would leave a control passing vacuously against a string
        ///     nothing produces.
        /// </summary>
        internal const string NoKolonyActivity = "no Kolony";


        private void Awake() => Instances.Add(this);

        /// <summary>
        ///     Must remove, or a destroyed villager keeps holding its claim forever.
        /// </summary>
        private void OnDestroy() => Instances.Remove(this);

        /// <summary>
        ///     Records what a villager is doing, logging only when it changes.
        ///
        ///     Transitions are rare and genuinely informative; the per-tick state is not.
        ///     Logging the change rather than the state is what keeps a colony of
        ///     villagers from flooding the log at 20 Hz each.
        ///
        ///     <paramref name="detail" /> is deliberately excluded from the comparison.
        ///     Anything that varies per tick - a distance, a position - would make every
        ///     tick look like a new activity and defeat the whole point.
        /// </summary>
        private void SetActivity(string activity, string detail = null)
        {
            if (Activity == activity)
            {
                return;
            }

            Activity = activity;
            Log.Info(string.IsNullOrEmpty(detail)
                ? $"Villager '{State.Name}' is now {activity}"
                : $"Villager '{State.Name}' is now {activity} ({detail})");
        }

        /// <summary>
        ///     Binds on demand. Callers outside the AI tick - hover text, diagnostics -
        ///     can read state before the villager has ever ticked.
        /// </summary>
        internal VillagerState State =>
            new VillagerState(Bind() && _nview.IsValid() ? _nview.GetZDO() : null);

        /// <summary>
        ///     Runs one AI step.
        /// </summary>
        /// <returns>
        ///     True if this villager handled its own behaviour and vanilla AI should be
        ///     skipped. False to let vanilla run - which it does correctly, since
        ///     BaseAI.UpdateAI no-ops for non-owners anyway.
        /// </returns>
        /// <summary>
        ///     Sends the villager somewhere, as its own behaviour rather than from outside.
        /// </summary>
        /// <remarks>
        ///     Driving a villager by calling into its walk from a test does not work and is
        ///     worth recording: the villager's own tick runs every frame and walks it home, so
        ///     two callers hand the same walker different destinations and it stands still
        ///     between them. An errand is consumed inside the tick, ahead of going home, which
        ///     is where a travel job will sit when there is one.
        /// </remarks>
        internal void SendOnErrand(Vector3 target)
        {
            _errand = target;
            _onErrand = true;
        }

        internal bool OnErrand => _onErrand;

        private Vector3 _errand;
        private bool _onErrand;

        /// <summary>
        ///     Narrates a journey to the log, for the half of travelling only a person can judge.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Three things are worth a line and nothing else is. <b>Mode changes</b>, because
        ///         the moment a villager comes into view and stops covering ground unseen is the
        ///         one most likely to look wrong. <b>Speed actually achieved</b>, measured between
        ///         reports rather than assumed from a setting - a villager that is technically
        ///         moving at three centimetres a second has been the answer more than once.
        ///         <b>Arrival</b>, so "it got there" and "it stopped" are different lines.
        ///     </para>
        ///     <para>
        ///         Silent unless a villager is on a journey longer than the settlement is wide,
        ///         which does not happen during ordinary work.
        ///     </para>
        /// </remarks>
        private void TraceTravel(Vector3 destination, MoveResult result)
        {
            bool travelling = _walk.Travelling;
            float remaining = Utils.DistanceXZ(transform.position, destination);

            if (!travelling)
            {
                if (!_tracing) return;

                _tracing = false;
                Log.Info($"[travel] {State.Name} finished travelling - {remaining:0}m out, " +
                         $"'{Activity}'. Walking from here.");
                return;
            }

            bool reckoning = _walk.Reckoning;

            if (!_tracing)
            {
                _tracing = true;
                _tracedReckoning = reckoning;
                _tracedAt = transform.position;
                _nextTrace = Time.time + TraceSeconds;
                Log.Info($"[travel] {State.Name} setting off - {remaining:0}m to go, " +
                         $"{(reckoning ? "unseen" : "walking")}.");
                return;
            }

            if (reckoning != _tracedReckoning)
            {
                _tracedReckoning = reckoning;
                string change = reckoning
                    ? "is out of sight and now covering ground unseen"
                    : "has come into view and is walking again";
                Log.Info($"[travel] {State.Name} {change} - {remaining:0}m to go.");
            }

            if (Time.time < _nextTrace) return;

            float covered = Utils.DistanceXZ(transform.position, _tracedAt);

            // Whether the settlement is actually holding the ground under this villager. A
            // traveller that is destroyed mid-journey leaves nothing behind to ask afterwards,
            // so the question has to be asked while it is still alive.
            Vector2s standingIn = ZoneSystem.GetZone(transform.position);
            bool zoneHeld = KeepAlive.KeepAliveZones.Contains(standingIn);
            bool appended = KeepAlive.Patches.ZDOManKeepAlivePatch.AppendedFrom(standingIn);
            bool allowed = _nview != null && _nview.IsValid() &&
                           KeepAlive.LoadAllowlist.Contains(_nview.GetZDO().GetPrefab());

            Log.Info($"[travel] {State.Name}: {remaining:0}m to go, " +
                     $"{covered / TraceSeconds:0.0}m/s {(reckoning ? "unseen" : "walking")}, " +
                     $"'{Activity}' ticks={_ticks / TraceSeconds:0.0}/s dt={_tickTime / Mathf.Max(1, _ticks):0.000} " +
                     $"zoneHeld={zoneHeld} appended={appended} allowed={allowed}" +
                     (result == MoveResult.PathFailed ? " - STUCK" : string.Empty));

            _tracedAt = transform.position;
            _nextTrace = Time.time + TraceSeconds;
            _ticks = 0;
            _tickTime = 0f;
        }

        private const float TraceSeconds = 3f;

        private bool _tracing;
        private bool _tracedReckoning;
        private Vector3 _tracedAt;
        private float _nextTrace;
        private int _ticks;
        private float _tickTime;

        /// <summary>Whether this rig can lie down, so a check can say which it is doing.</summary>
        internal bool CanSleep => _animation != null && _animation.CanSleep;

        /// <summary>This villager's durable identity, for anything that has to name it.</summary>
        /// <remarks>
        ///     Binds on demand, as <see cref="State" /> beside it does. Reading the field
        ///     directly instead meant this answered None for any villager that had not yet
        ///     ticked - the component resolves its view lazily, and nothing binds it at
        ///     Awake - so a caller that asked before the first AI tick was told the villager
        ///     had no identity. That is a silent None rather than an error, and it made a
        ///     cleanup path that guarded on it unreachable for the whole of two review rounds.
        /// </remarks>
        internal ZDOID Id => Bind() && _nview.IsValid() ? _nview.GetZDO().m_uid : ZDOID.None;

        /// <summary>Whether this villager is partway through a journey longer than one hop.</summary>
        internal bool IsTravelling => _walk != null && _walk.Travelling;

        /// <summary>Whether it is covering ground unseen rather than walking it.</summary>
        internal bool IsReckoning => _walk != null && _walk.Reckoning;

        /// <summary>Where it intends to be next, so the ground there can be loaded for it.</summary>
        internal Vector3 Waypoint => _walk != null ? _walk.Waypoint : transform.position;

        /// <summary>What the pathfinder thinks about a target, for a failure worth explaining.</summary>
        internal string Explain(Vector3 target) => VillagerMovement.Explain(_ai, target);

        /// <summary>
        ///     Puts away an axe this villager's chopping put in its hand, wherever it has
        ///     stopped chopping - including the paths that never reach the queue again.
        /// </summary>
        private void PutAxeAway()
        {
            Jobs.Chop.ChopJob.PutAxeAway(_visEquipment,
                _nview != null && _nview.IsValid() ? _nview.GetZDO() : null);

            // The rig's own hands, as well as the visible slot. Left set, the villager keeps
            // standing as though holding a tool it is no longer carrying.
            _animation?.Hold(null);
        }

        /// <summary>How long this villager's current trip has gone without getting closer.</summary>
        internal float TripStalledFor => _walk != null ? _walk.TripStalledFor : 0f;

        /// <summary>
        ///     Drives one walking tick at a destination, for checks about the trip clock.
        /// </summary>
        /// <remarks>
        ///     The clock is kept by the walk from the leg it is given, so the only honest way
        ///     to ask whether it starts, advances and resets is to walk somewhere.
        /// </remarks>
        internal void WalkForTest(Vector3 destination)
        {
            if (!Bind()) return;
            _walk.MoveTowards(destination, Navigation.Approach.ToStructure, deltaTime: .05f);
        }

        /// <summary>Stops walking on purpose, the way resting does, for checks.</summary>
        internal void StopForTest()
        {
            if (Bind()) _walk.Stop();
        }

        /// <summary>Announces a new leg the way a job does, for checks.</summary>
        internal void NewLegForTest()
        {
            if (Bind()) _walk.NewLeg();
        }

        /// <summary>Turns to face something, for work done standing still.</summary>
        internal void FaceTowards(Vector3 target, float deltaTime) =>
            VillagerMovement.FaceTowards(_ai, target, deltaTime);

        internal bool TryTakeOver(float deltaTime)
        {
            if (!Bind())
            {
                return false;
            }

            if (!_nview.IsValid())
            {
                return false;
            }

            // Every client needs the bag so it can read the contents; only the owner
            // may write to it.
            EnsureBag();

            // AI runs only on the ZDO owner (BaseAI.UpdateAI enforces the same rule).
            // Ownership can move between ticks, so this is never cached.
            if (!_nview.IsOwner())
            {
                return false;
            }

            // Identity first: everything below reports by name, and taming used to log
            // an empty one because it ran before the villager had been named.
            EnsureStaysPut();
            EnsureSoftEdges();
            EnsureIdentity();
            EnsureHumanSkin();
            EnsureAppearance();
            EnsureTamed();

            _ticks++;
            _tickTime += deltaTime;

            if (_onErrand)
            {
                MoveResult errand = _walk.MoveTowards(_errand, Navigation.Approach.ToStructure,
                    run: false, deltaTime: deltaTime);
                TraceTravel(_errand, errand);
                if (errand == MoveResult.Arrived) _onErrand = false;
                SetActivity(errand == MoveResult.PathFailed ? "cannot get there" : "travelling");
                return true;
            }

            if (TryWork(deltaTime)) return true;

            // A villager whose hearth was destroyed has nowhere to belong. Walking to
            // where the hearth used to be would look like ordinary behaviour, so it stops
            // and says what is wrong instead.
            if (!HasColony())
            {
                VillagerMovement.Stop(_ai);
                SetActivity(NoKolonyActivity);
                return true;
            }

            // Nothing to work at yet: jobs are rebuilt at roadmap milestone 6. Until then a
            // villager lives in the settlement and idles, which is the whole of milestone 1.
            StayNearHome(deltaTime);

            return true;
        }

        /// <summary>
        ///     Resolves components lazily rather than in Awake. A ZNetView is not
        ///     guaranteed to hold a valid ZDO by the time sibling components wake, so
        ///     binding on first use avoids depending on component order.
        /// </summary>
        private bool Bind()
        {
            if (_ai != null && _character != null && _nview != null)
            {
                return true;
            }

            // TryGetComponent, never GetComponent()?. - a destroyed Unity object passes
            // as non-null through the null-propagating operators. See docs/code-style.md.
            if (!TryGetComponent(out _ai))
            {
                return false;
            }

            if (!TryGetComponent(out _character))
            {
                return false;
            }

            if (!TryGetComponent(out _nview))
            {
                return false;
            }

            // CustomCreature registration may rebuild parts of the network prefab after
            // our template configuration. Persistence is a property of the live ZDO, so
            // enforce it at the boundary that actually matters for save/unload behavior.
            if (_nview.IsValid() && _nview.GetZDO() != null && !_nview.GetZDO().Persistent)
            {
                _nview.GetZDO().Persistent = true;
                Log.Warning("Corrected non-persistent villager ZDO created by the prefab pipeline");
            }

            TryGetComponent(out _visEquipment);

            // Both hold state for this villager alone, so they are built once here rather
            // than looked up per tick.
            _walk = new Navigation.VillagerWalk(_ai);
            _animation = new VillagerAnimation(gameObject);
            return true;
        }

        /// <summary>
        ///     A bag that survives reloads and ownership transfer. Attached on every
        ///     client, not just the owner, so non-owners can read its contents.
        /// </summary>
        private void EnsureBag()
        {
            if (_bag == null)
            {
                _bag = VillagerInventory.Attach(gameObject, _nview);
            }
        }

        /// <summary>
        ///     Rolls a face and outfit once. VisEquipment persists the result to the ZDO
        ///     by itself, so this never needs to run again.
        /// </summary>
        private void EnsureAppearance()
        {
            VillagerState state = State;
            if (state.HasAppearance || _visEquipment == null)
            {
                return;
            }

            VillagerAppearance.Randomise(_visEquipment);
            state.MarkAppearanceRolled();
            Log.Info($"Villager '{state.Name}' rolled its appearance");
        }

        /// <summary>
        ///     Draws this villager with the player's materials instead of the ghost rig's.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Measured, not guessed: a villager's body and hair render with
        ///         <c>Custom/Fallen Warrior</c> while its clothing already renders with
        ///         <c>Custom/Player</c>. That is why villagers glowed gold and the player did
        ///         not, and why removing the rig's lights and particles changed nothing — the
        ///         glow is in the skin material, not in an effect.
        ///     </para>
        ///     <para>
        ///         It repaints by shader rather than by knowing which renderer is which,
        ///         because hair and beards are built later and separately: repainting only the
        ///         body left villagers with normal skin and a glowing haircut. Running for a
        ///         few ticks catches whatever the game builds after the first one.
        ///     </para>
        ///     <para>
        ///         This is done here rather than when the prefab is built, because the prefab
        ///         is built before the scene has a player to copy from — the first attempt did
        ///         it there and silently kept the ghost skin. Material references are swapped,
        ///         never edited: editing a shared material would repaint every Fallen Warrior
        ///         in the world.
        ///     </para>
        /// </remarks>
        /// <summary>
        ///     Gives this villager the player's skin, once.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         One pass, on the first owned tick. Everything the game attaches later - hair,
        ///         beard, armour - is repainted by <see cref="Patches.VillagerAttachmentPatch" />
        ///         as it is attached, so there is nothing to wait for and nothing to poll.
        ///     </para>
        ///     <para>
        ///         This replaced a budget of up to 240 ticks per villager per load, each walking
        ///         every renderer on the rig and allocating a material array for each. That cost
        ///         scaled with population and with reloads, which is exactly the shape the
        ///         no-population-cap rule forbids; this scales with how often a villager changes
        ///         what it wears.
        ///     </para>
        /// </remarks>
        private void EnsureHumanSkin()
        {
            if (_skinRepainted || _visEquipment == null) return;
            if (!VillagerSkin.TryGet(out Material _, out Material _))
            {
                // The player rig is not built yet. Try again next tick rather than marking a
                // villager done when nothing was copied.
                return;
            }

            _skinRepainted = true;
            int repainted = VillagerSkin.Repaint(gameObject);
            Log.Info($"[villager] skin set in one pass, {repainted} renderer(s) repainted");
        }

        private bool _skinRepainted;

        /// <summary>
        ///     Villagers are tamed so the player and their other tame creatures never
        ///     treat them as enemies. BaseAI.IsEnemy short-circuits on tamed state before
        ///     it reaches the faction switch, which is cleaner than the predecessor's
        ///     trick of zeroing the creature's senses to fake friendliness.
        ///
        ///     Character.SetTamed persists to the ZDO, so this settles after one tick and
        ///     survives reloads.
        /// </summary>
        private void EnsureTamed()
        {
            if (_character.IsTamed())
            {
                return;
            }

            _ai.MakeTame();
            Log.Info($"Villager '{State.Name}' tamed");
        }

        /// <summary>
        ///     Gives a new villager its name and home. Runs once - afterwards both are on
        ///     the ZDO and survive save, reload and ownership transfer.
        /// </summary>
        /// <summary>
        ///     Stops a villager shoving the player around.
        /// </summary>
        /// <remarks>
        ///     A villager is built on a creature rig and keeps its colliders, so standing in a
        ///     doorway it pushes the player out of the way - which reads as the settlement
        ///     fighting you rather than living with you. Collisions against the world are left
        ///     alone, so villagers still walk on the ground and not through walls.
        ///
        ///     Re-checked rather than done once at spawn: the player can die, respawn and
        ///     arrive with new colliders, and a villager loaded long before them never met the
        ///     old ones.
        /// </remarks>
        /// <summary>
        ///     Stops the game deciding a villager should wander off and disappear.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <c>BaseAI.MoveAwayAndDespawn</c> walks a creature <em>away from the nearest
        ///         player</em> five metres at a time, and destroys its ZNetView once no player is
        ///         within forty. That is both of the things a settlement must never do: a villager
        ///         that appears to wander off in a meaningless direction, and one that fades away
        ///         in front of you.
        ///     </para>
        ///     <para>
        ///         It is reached from two flags - <c>despawnInDay</c> and <c>eventCreature</c> -
        ///         and <b>neither check asks whether the creature is tamed</b>, so being somebody's
        ///         villager is no protection. The flags live on the ZDO, re-read every four
        ///         seconds, so clearing the component's fields alone would not hold.
        ///     </para>
        ///     <para>
        ///         Cleared once per villager rather than every tick: the ZDO write is the point,
        ///         and rewriting it constantly is the shape a settlement with no population cap
        ///         cannot afford.
        ///     </para>
        /// </remarks>
        private void EnsureStaysPut()
        {
            if (_staysPut || _ai == null || _nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;

            _staysPut = true;
            _ai.SetDespawnInDay(false);
            _ai.SetEventCreature(false);
        }

        private bool _staysPut;

        private void EnsureSoftEdges()
        {
            Player player = Player.m_localPlayer;
            if (player == null || ReferenceEquals(player, _ignoringPlayer)) return;

            Collider[] mine = GetComponentsInChildren<Collider>(true);
            Collider[] theirs = player.GetComponentsInChildren<Collider>(true);
            foreach (Collider a in mine)
            {
                if (a == null || a.isTrigger) continue;
                foreach (Collider b in theirs)
                {
                    if (b == null || b.isTrigger) continue;
                    Physics.IgnoreCollision(a, b, true);
                }
            }

            _ignoringPlayer = player;
        }

        private Player _ignoringPlayer;

        private void EnsureIdentity()
        {
            VillagerState state = State;

            if (!state.HasName)
            {
                string chosen = VillagerNames.Pick(NamesInUse());
                state.SetName(chosen);
                Log.Info($"Villager named '{chosen}'");
            }

            if (!state.HasHome)
            {
                Vector3 home = transform.position;
                state.SetHome(home);
                Log.Info($"Villager '{state.Name}' made home at ({home.x:F1}, {home.y:F1}, {home.z:F1})");
            }
        }

        /// <summary>
        ///     Names already spoken for. Loaded villagers always count; if this one is
        ///     already in a colony, its fellow members count too - read from their ZDOs,
        ///     so members nowhere near a player are still included.
        /// </summary>
        private HashSet<string> NamesInUse()
        {
            HashSet<string> taken = new HashSet<string>();

            foreach (Villager other in Instances)
            {
                if (other != this && other.State.HasName)
                {
                    taken.Add(other.State.Name);
                }
            }

            ZDO zdo = _nview != null ? _nview.GetZDO() : null;
            if (zdo == null)
            {
                return taken;
            }

            ZDO colonyZdo = ZDOMan.instance?.GetZDO(Colonies.ColonyMembership.GetColony(zdo));
            if (colonyZdo == null)
            {
                return taken;
            }

            foreach (ZDOID member in new Colonies.ColonyState(colonyZdo)
                         .GetMembers(Colonies.ColonyMemberKind.Villager))
            {
                ZDO memberZdo = ZDOMan.instance?.GetZDO(member);
                if (memberZdo != null && memberZdo != zdo)
                {
                    taken.Add(new VillagerState(memberZdo).Name);
                }
            }

            return taken;
        }

        /// <summary>
        ///     Where this villager idles when no queued job can run.
        /// </summary>
        /// <summary>
        ///     Where this villager belongs: its bed if it has one, else where it was born.
        /// </summary>
        /// <remarks>
        ///     Read from the colony's record rather than from a loaded bed, so a villager can
        ///     walk home to a bed whose zone has not been instantiated. The spawn point remains
        ///     the answer for anyone unassigned, which is what it has always been.
        /// </remarks>
        /// <summary>
        ///     Runs the job at the front of this villager's queue, if it has one.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Returns false when there is no work, so the villager falls through to going
        ///         home — an empty queue is idle, not an error.
        ///     </para>
        ///     <para>
        ///         Deciding is throttled; walking is not. A job that yields is asked again after
        ///         a pause rather than on the next tick, so a settlement with nothing to do
        ///         costs almost nothing. Movement still runs at the full rate, because a
        ///         villager that thinks twenty times a second and walks five would move in
        ///         visible steps.
        ///     </para>
        /// </remarks>
        private bool TryWork(float deltaTime)
        {
            Colonies.Colony colony = Colonies.Colony.FindFor(_nview.GetZDO());
            if (colony == null)
            {
                // Before the early return, not after. A villager whose hearth was destroyed
                // never reaches the queue again, so an axe left in its hand here would stay
                // there for the rest of the session - which is the exact failure putting it
                // away was added to prevent.
                PutAxeAway();
                return false;
            }

            if (Time.time < _nextWorkTick) return _working;

            VillagerState state = State;

            // Rest comes before work, and before the queue is even consulted. A tired villager
            // has nothing useful to offer any job, and asking one for work it cannot do would
            // burn a repetition to discover that.
            if (Resting.Tick(this, colony, state, _walk, _animation, deltaTime, out string resting))
            {
                SetActivity(resting);
                _working = true;
                return true;
            }

            List<Jobs.JobDefinition> jobs = colony.State.GetJobs();
            Jobs.JobDefinition job = Jobs.QueueRunner.Current(state, jobs);

            // A tool is held while the work is being done. Only the chopping job writes the
            // visible right hand, so anything else being current - a haul entry, or a chop
            // job the player deleted - has to put the axe away, or the villager carries one
            // for the rest of the session with nothing left to write the slot again.
            if (job == null || job.Kind != Jobs.JobKind.Chop) PutAxeAway();

            if (job == null)
            {
                _working = false;
                return false;
            }

            Jobs.JobResult result = Run(colony, job, state, deltaTime, out string doing);
            Jobs.QueueRunner.Apply(state, jobs, result);

            // Work is paid for when something is actually decided - a finished trip, or a failed
            // one. Not for Running, which is a step still in progress, and not for Skipped, which
            // is a villager that found nothing to do and should not tire from looking.
            if (result == Jobs.JobResult.Completed || result == Jobs.JobResult.Failed)
            {
                Resting.Spend(state);
            }

            // Only a yield backs off. Everything else is progress, and progress should not be
            // made to wait.
            if (result == Jobs.JobResult.Skipped) _nextWorkTick = Time.time + IdlePauseSeconds;

            SetActivity(doing);
            _working = true;
            return true;
        }

        private Jobs.JobResult Run(Colonies.Colony colony, Jobs.JobDefinition job,
            VillagerState state, float deltaTime, out string doing)
        {
            switch (job.Kind)
            {
                case Jobs.JobKind.Haul:
                    return Jobs.Haul.HaulJob.Tick(new Jobs.Haul.HaulContext
                    {
                        Villager = this,
                        Colony = colony,
                        Bag = _bag,
                        Walk = _walk,
                        Animation = _animation,
                        Job = job,
                        State = state,
                        DeltaTime = deltaTime
                    }, out doing);

                case Jobs.JobKind.Chop:
                    return Jobs.Chop.ChopJob.Tick(new Jobs.Chop.ChopContext
                    {
                        Villager = this,
                        Colony = colony,
                        Bag = _bag,
                        Walk = _walk,
                        Animation = _animation,
                        Job = job,
                        State = state,

                        // The visible mirror, so the axe it works with is the axe a player can
                        // see it holding. Nothing is placed in the creature's own inventory:
                        // the routine that equips a creature's best weapon on load would strip
                        // it, so the bag is the truth and this is the reflection.
                        Equipment = _visEquipment,
                        DeltaTime = deltaTime
                    }, out doing);

                default:
                    // Work with no engine is work somebody has not finished adding. Failing
                    // loudly beats a villager standing still for a reason nothing reports.
                    doing = "I do not know how to do that";
                    return Jobs.JobResult.Failed;
            }
        }

        /// <summary>How long to wait before asking a job that had nothing to do.</summary>
        private const float IdlePauseSeconds = .5f;

        private float _nextWorkTick;
        private bool _working;

        private Vector3 ResolveHome(VillagerState state)
        {
            Colonies.Colony colony = Colonies.Colony.FindFor(_nview.GetZDO());
            if (colony != null)
            {
                Colonies.StructureRecord bed =
                    Colonies.SettlementIndex.BedOf(colony, _nview.GetZDO().m_uid);
                ZDO bedZdo = bed == null ? null : ZDOMan.instance?.GetZDO(bed.Id);
                if (bedZdo != null && bedZdo.IsValid()) return bedZdo.GetPosition();
            }

            return state.Home;
        }

        /// <summary>
        ///     Whether this villager currently has a colony it can see.
        /// </summary>
        /// <remarks>
        ///     This deliberately does not clear the stale back-pointer, and does not claim
        ///     the colony was destroyed. Absence is not proof: on the host a missing ZDO does
        ///     mean destroyed, but a multiplayer client is only told about part of the world,
        ///     so a colony that was never replicated to it looks exactly the same. Deleting
        ///     membership on that evidence would throw away a real settlement's roster.
        ///
        ///     So the response is behavioural and reversible - the villager stops and says it
        ///     has no colony - and it resumes by itself if the hearth turns out to be there
        ///     after all. Reclaiming genuinely orphaned villagers is the colony screen's job,
        ///     where a person can see what is being discarded.
        /// </remarks>
        private bool HasColony()
        {
            ZDOID colonyId = Colonies.ColonyMembership.GetColony(_nview.GetZDO());
            return !colonyId.IsNone() && ZDOMan.instance != null &&
                   ZDOMan.instance.GetZDO(colonyId) != null;
        }

        /// <summary>
        ///     Idle behaviour: stay within reach of home, otherwise walk back.
        ///
        ///     Deliberately concrete rather than hidden behind a behaviour interface.
        ///     One example is not enough to know the right abstraction, and the
        ///     predecessor's twelve near-identical AI classes are what happens when you
        ///     guess. The interface gets extracted once a second behaviour exists.
        /// </summary>
        private void StayNearHome(float deltaTime)
        {
            VillagerState state = State;
            Vector3 home = ResolveHome(state);
            float distance = Utils.DistanceXZ(home, transform.position);

            if (distance <= ModConfig.GoHomeRadius.Value)
            {
                VillagerMovement.Stop(_ai);
                SetActivity("idle");
                _pathFailureReported = false;
                return;
            }

            // Routed through VillagerWalk like every other journey, so walking home gets the
            // same navmesh-snapped destination, the same path grace and the same progress
            // tracking that work does. Calling the bare move wrapper here meant the one piece
            // of movement every villager performs constantly was also the only one that could
            // not benefit from any of it.
            switch (_walk.MoveTowards(home, HomeStopDistance, deltaTime: deltaTime))
            {
                case MoveResult.Moving:
                    SetActivity("walking home", $"{distance:F0}m away");
                    _pathFailureReported = false;
                    break;

                case MoveResult.Arrived:
                    SetActivity("idle");
                    _pathFailureReported = false;
                    break;

                case MoveResult.PathFailed:
                    SetActivity("stuck");
                    // Latched: the villager retries every tick, but one log line is
                    // enough to diagnose it. Reset as soon as it moves again.
                    if (!_pathFailureReported)
                    {
                        _pathFailureReported = true;
                        Log.Warning(
                            $"Villager '{state.Name}' cannot path home, {distance:F0}m away. Retrying.");
                    }

                    break;
            }
        }

        /// <summary>
        ///     Where this villager would walk to when idle. Exposed for the acceptance
        ///     test, which verifies the persistent fallback home.
        /// </summary>
        internal Vector3 ResolveHomeForTest() => ResolveHome(State);

        /// <summary>
        ///     Explains why this villager is not operating. Diagnostics only - a villager
        ///     that fails to bind is silent by design, which makes it invisible when
        ///     something is wrong.
        /// </summary>
        internal string Diagnose()
        {
            if (!TryGetComponent(out MonsterAI _))
            {
                return "no MonsterAI component";
            }

            if (!TryGetComponent(out Character _))
            {
                return "no Character component";
            }

            if (!TryGetComponent(out ZNetView nview))
            {
                return "no ZNetView component";
            }

            if (nview.m_zdo == null)
            {
                // A prefab registered under Jotunn's container is a scene object too, so
                // it can be mistaken for a spawned instance. The "(Clone)" suffix and the
                // active flags tell the two apart.
                return $"ZNetView never created a ZDO - name='{gameObject.name}' "
                       + $"activeSelf={gameObject.activeSelf} "
                       + $"activeInHierarchy={gameObject.activeInHierarchy} "
                       + $"parent='{(transform.parent != null ? transform.parent.name : "<none>")}' "
                       + $"scene='{gameObject.scene.name}'";
            }

            if (!nview.m_zdo.IsValid())
            {
                return $"ZDO exists but was invalidated (uid={nview.m_zdo.m_uid}, "
                       + $"persistent={nview.m_zdo.Persistent}, owner={nview.m_zdo.HasOwner()})";
            }

            return "bound ok";
        }

        /// <summary>Hover line: who this is and what they are doing.</summary>
        internal string DescribeForHover()
        {
            VillagerState state = State;
            if (!state.IsValid)
            {
                return string.Empty;
            }

            // The prompt belongs here rather than on a Hoverable of our own. Character
            // already implements Hoverable and the game resolves it with
            // GetComponentInParent<Hoverable>(), first match wins, and component order is not
            // guaranteed - so a second one is a coin flip. Adding the interface to this class
            // lost that flip every time, because the component is added after Humanoid: the
            // method was never called, the prompt never appeared, and the check that asserted
            // it passed by calling the method itself.
            string prompt = "[<color=yellow><b>$KEY_Use</b></color>] manage";
            return $"{state.Name}\n<color=grey>{Activity}</color>\n" +
                   (Localization.instance != null ? Localization.instance.Localize(prompt) : prompt);
        }

        /// <summary>
        ///     Opens the colony screen on this villager.
        /// </summary>
        /// <remarks>
        ///     <paramref name="hold" /> is refused rather than obeyed: the key repeats while it
        ///     is held, and a screen that reopens twenty times a second is a screen that cannot
        ///     be navigated. The same two calls the map pin makes, so walking up to somebody and
        ///     clicking their pin land on exactly the same screen.
        /// </remarks>
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || _nview == null || !_nview.IsValid()) return false;

            ZDO zdo = _nview.GetZDO();
            Colony colony = Colony.FindFor(zdo);
            if (colony == null)
            {
                Report.Say($"{DisplayName()} has no Kolony to manage them from.");
                return true;
            }

            ColonyScreen screen = ColonyScreen.Instance;
            if (screen == null) return false;

            screen.Open(colony, null);
            screen.Push(new VillagerDetailScreen(zdo.m_uid));
            return true;
        }

        /// <summary>
        ///     Handing an item over is done from the villager's screen, not by shoving it at them.
        /// </summary>
        /// <remarks>
        ///     Refused explicitly rather than left unimplemented: returning true here consumes
        ///     the player's item, and a villager that silently eats whatever you are holding
        ///     when you press use is worse than one that does nothing.
        /// </remarks>
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        internal string DisplayName()
        {
            VillagerState state = State;
            return state.IsValid && state.HasName ? state.Name : "Villager";
        }
    }
}
