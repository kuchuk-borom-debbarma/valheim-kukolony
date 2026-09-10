using System;
using System.Collections.Generic;
using Jotunn.Managers;
using Kukolony.Colonies;
using Kukolony.Jobs;
using Kukolony.Jobs.Work;
using Kukolony.Villagers;
using UnityEngine;
using UnityEngine.UI;

namespace Kukolony.Gui
{
    internal sealed class ColonyPanel : MonoBehaviour
    {
        private enum Tab { Structures, Members, Jobs }
        private const int Rows = 4;
        internal static ColonyPanel Instance { get; private set; }

        private GameObject _root;
        private GameObject _content;
        private InputField _name;
        private Colony _colony;
        private Tab _tab;
        private int _page;
        private string _search = string.Empty;
        private StructureSort _sort;
        private StructureCapability _capabilityFilter;
        private ZDOID _selectedMember = ZDOID.None;
        /// <summary>Villager awaiting a second click to confirm removal; never survives navigation.</summary>
        private ZDOID _pendingRemoval = ZDOID.None;
        private readonly HashSet<ZDOID> _selectedMembers = new HashSet<ZDOID>();
        private int _memberJobIndex;
        private ColonyJobConfig _selectedJob;
        private bool _showPresets;
        private bool _showTargetPicker;
        /// <summary>Outfit being edited, or -1 for the outfit list. Cleared on every navigation.</summary>
        private int _selectedOutfit = -1;
        private bool _showOutfits;
        /// <summary>True while the Structures tab is choosing something new to register.</summary>
        private bool _showRegisterPicker;
        private bool _blocked;

        internal bool IsOpen => _root != null && _root.activeSelf;
        internal static void Register() => GUIManager.OnCustomGUIAvailable += Rebuild;

        private static void Rebuild()
        {
            if (Instance != null) Destroy(Instance.gameObject);
            GameObject holder = new GameObject("KukolonyColonyPanel");
            holder.transform.SetParent(GUIManager.CustomGUIFront.transform, false);
            Instance = holder.AddComponent<ColonyPanel>();
            Instance.Build();
        }

        private void Build()
        {
            _root = GUIManager.Instance.CreateWoodpanel(transform, new Vector2(.5f,.5f),
                new Vector2(.5f,.5f), Vector2.zero, 860f, 680f, true);
            _root.SetActive(false);
            TextAt(_root.transform, "Colony management", 0, -28, 24, 600);
            _name = GUIManager.Instance.CreateInputField(_root.transform, new Vector2(.5f,1),
                new Vector2(.5f,1), new Vector2(0,-68), InputField.ContentType.Standard,
                "colony name", 24, 420, 30).GetComponent<InputField>();
            _name.onEndEdit.AddListener(value => { if (_colony != null && !string.IsNullOrWhiteSpace(value)) { _colony.State.SetName(value.Trim()); Refresh(); } });
            AddButton(_root.transform, "Structures", -230, -110, 190, () => SetTab(Tab.Structures));
            AddButton(_root.transform, "Members", 0, -110, 190, () => SetTab(Tab.Members));
            AddButton(_root.transform, "Jobs", 230, -110, 190, () => SetTab(Tab.Jobs));
            _content = new GameObject("Content", typeof(RectTransform));
            _content.transform.SetParent(_root.transform, false);
            RectTransform contentRect = (RectTransform)_content.transform;
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            AddButton(_root.transform, "Close", 0, -640, 140, Close);
        }

        internal void Open(Colony colony)
        {
            if (_root == null || colony == null) return;
            _colony = colony;
            if (colony.TryGetComponent(out ZNetView view) && view.IsValid()) view.ClaimOwnership();
            colony.EnsureNamed();
            _name.text = colony.State.Name;
            _tab = Tab.Structures; _page = 0; _search = string.Empty;
            _selectedMember = ZDOID.None; _pendingRemoval = ZDOID.None;
            _root.SetActive(true);
            Block(true);
            Refresh();
        }

        internal void Close()
        {
            if (_root != null) _root.SetActive(false);
            _colony = null; _pendingRemoval = ZDOID.None; Block(false);
        }

        internal void ShowTabForTest(string tab)
        {
            if (Enum.TryParse(tab, true, out Tab parsed)) SetTab(parsed);
        }

        internal void ShowMemberDetailForTest(int index)
        {
            List<ZDOID> members = _colony?.State.GetMembers(ColonyMemberKind.Villager);
            if (members != null && index >= 0 && index < members.Count)
            { _selectedMember = members[index]; _pendingRemoval = ZDOID.None; _tab = Tab.Members; Refresh(); }
        }

        /// <summary>
        ///     Selects an exact member so UI evidence shows a chosen fixture rather than
        ///     whichever member happens to sort first.
        /// </summary>
        internal void ShowMemberDetailForTest(ZDOID member)
        {
            List<ZDOID> members = _colony?.State.GetMembers(ColonyMemberKind.Villager);
            if (members != null && members.Contains(member))
            { _selectedMember = member; _pendingRemoval = ZDOID.None; _tab = Tab.Members; Refresh(); }
        }

        /// <summary>
        ///     Arms the inline remove confirmation so it can be photographed. Deliberately
        ///     does not remove anything: the UI scenario registers chest fixtures as members,
        ///     and executing here would destroy them mid-run and corrupt later captures.
        /// </summary>
        internal void ShowRemoveConfirmForTest()
        {
            if (_selectedMember.IsNone()) return;
            _pendingRemoval = _selectedMember;
            Refresh();
        }

        /// <summary>Opens the add-a-structure list. Registers nothing; the capture is the subject.</summary>
        internal void ShowRegisterPickerForTest()
        {
            _tab = Tab.Structures;
            _showRegisterPicker = true;
            _search = string.Empty;
            _page = 0;
            Refresh();
        }

        /// <summary>Opens the outfit list for UI evidence.</summary>
        internal void ShowOutfitsForTest()
        {
            _tab = Tab.Members;
            _selectedMember = ZDOID.None;
            _showOutfits = true;
            _selectedOutfit = -1;
            _page = 0;
            Refresh();
        }

        /// <summary>Opens one outfit's slots. Changes nothing; the capture is the subject.</summary>
        internal void ShowOutfitCardForTest()
        {
            ShowOutfitsForTest();
            _selectedOutfit = 0;
            Refresh();
        }

        internal void ShowJobForTest(int index)
        {
            List<ColonyJobConfig> jobs = _colony?.State.GetEffectiveJobs();
            if (jobs != null && index >= 0 && index < jobs.Count)
            { _selectedJob = jobs[index]; _tab = Tab.Jobs; Refresh(); }
        }

        internal void ShowPresetsForTest() { _tab = Tab.Jobs; _selectedJob = null; _showPresets = true; Refresh(); }
        internal void ShowPageForTest(int page) { _page = Mathf.Max(0, page); Refresh(); }
        internal void ShowTargetPickerForTest() { if (_selectedJob != null) { _showTargetPicker = true; Refresh(); } }

        private void Update()
        {
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private void OnDestroy() { Block(false); if (Instance == this) Instance = null; }
        private void Block(bool value) { if (_blocked == value) return; _blocked = value; GUIManager.BlockInput(value); }
        private void SetTab(Tab tab)
        {
            _tab = tab; _page = 0; _pendingRemoval = ZDOID.None; _showTargetPicker = false;
            _showOutfits = false; _selectedOutfit = -1; _showRegisterPicker = false;
            if (tab != Tab.Members) _selectedMember = ZDOID.None;
            if (tab != Tab.Jobs) { _selectedJob = null; _showPresets = false; _showTargetPicker = false; }
            Refresh();
        }

        private void Refresh()
        {
            if (_colony == null || _content == null) return;
            foreach (Transform child in new List<Transform>(Children(_content.transform))) Destroy(child.gameObject);
            switch (_tab)
            {
                case Tab.Structures: BuildStructures(); break;
                case Tab.Members: BuildMembers(); break;
                default: BuildJobs(); break;
            }
        }

        private void BuildStructures()
        {
            if (_showRegisterPicker) { BuildRegisterPicker(); return; }
            InputField search = GUIManager.Instance.CreateInputField(_content.transform, new Vector2(.5f,1),
                new Vector2(.5f,1), new Vector2(-150,-155), InputField.ContentType.Standard,
                "search structures", 24, 360, 30).GetComponent<InputField>();
            search.text = _search;
            search.onEndEdit.AddListener(value => { _search = value; _page = 0; Refresh(); });
            // Registering lives on the Add screen, where both "this one" and "all of them"
            // belong together; this row is for looking at what is already registered.
            AddButton(_content.transform, "Add…", 90, -155, 100,
                () => { _showRegisterPicker = true; _search = string.Empty; _page = 0; Refresh(); });
            AddButton(_content.transform, "Sort: " + _sort, 230, -155, 140, () =>
            { _sort = (StructureSort)(((int)_sort + 1) % 4); Refresh(); });
            AddButton(_content.transform, "Filter: " + CapabilityName(_capabilityFilter), 355, -155, 90, () =>
            { _capabilityFilter = NextCapability(_capabilityFilter); _page = 0; Refresh(); });

            List<StructureRecord> records = ColonyOperations.FilterStructures(_colony, _search,
                _capabilityFilter, _sort);
            int start = _page * Rows;
            for (int row = 0; row < Rows && start + row < records.Count; row++)
            {
                StructureRecord record = records[start + row];
                string status = record.IsLiveIn(_colony) ? "ready" : "invalid/out of radius";
                InputField rename = InputAt(_content.transform, record.Name, -285, -205-row*48, 190);
                rename.onEndEdit.AddListener(value => { ColonyOperations.RenameStructure(_colony, record.Id, value); Refresh(); });
                TextAt(_content.transform, record.Prefab, -45, -205-row*48, 14, 190, TextAnchor.MiddleLeft);
                TextAt(_content.transform, record.Capabilities.ToString(), 155, -205-row*48, 14, 180, TextAnchor.MiddleLeft);
                TextAt(_content.transform, status, 315, -205-row*48, 13, 130, TextAnchor.MiddleLeft,
                    record.IsLiveIn(_colony) ? Color.green : Color.gray);
                AddButton(_content.transform, "Remove", 370, -205-row*48, 75, () =>
                { _colony.RemoveStructure(record.Id); Refresh(); });
            }
            Pager(records.Count);
            LeftTextAt(_content.transform, $"{records.Count} registered • radius {_colony.EffectiveRadius:F0}m",
                -570, 14, 320, Color.gray);
        }

        private void BuildMembers()
        {
            List<ZDOID> members = _colony.State.GetMembers(ColonyMemberKind.Villager);
            if (_showOutfits) { BuildOutfits(); return; }
            if (!_selectedMember.IsNone())
            {
                BuildMemberDetail(members);
                return;
            }
            TextAt(_content.transform, "Villagers", -310, -160, 18, 180, TextAnchor.MiddleLeft);
            AddButton(_content.transform, "Outfits", 300, -160, 130,
                () => { _showOutfits = true; _selectedOutfit = -1; _page = 0; Refresh(); });
            for (int row = 0; row < Rows && _page*Rows+row < members.Count; row++)
            {
                ZDOID id = members[_page*Rows+row];
                bool selected = _selectedMembers.Contains(id);
                AddButton(_content.transform, selected ? "✓" : "○", -330, -210-row*48, 45,
                    () => { if (!_selectedMembers.Add(id)) _selectedMembers.Remove(id); Refresh(); });
                TextAt(_content.transform, ColonyAssignments.NameOf(id), -210, -210-row*48, 16, 190, TextAnchor.MiddleLeft);
                TextAt(_content.transform, ColonyAssignments.DescribeActivity(id), 40, -210-row*48, 14, 230, TextAnchor.MiddleLeft, Color.gray);
                AddButton(_content.transform, "Details", 300, -210-row*48, 120,
                    () => { _selectedMember=id; _pendingRemoval=ZDOID.None; Refresh(); });
            }
            List<ColonyJobConfig> jobs = _colony.State.GetEffectiveJobs();
            if (jobs.Count > 0)
            {
                _memberJobIndex = Mathf.Clamp(_memberJobIndex, 0, jobs.Count - 1);
                AddButton(_content.transform, "Job: " + Short(jobs[_memberJobIndex].Name, 18), -120, -520, 230,
                    () => { _memberJobIndex = (_memberJobIndex + 1) % jobs.Count; Refresh(); });
                AddButton(_content.transform, "Assign selected (" + _selectedMembers.Count + ")", 130, -520, 220,
                    () => { foreach (ZDOID member in _selectedMembers) ColonyAssignments.AppendJob(member, jobs[_memberJobIndex].Id); Refresh(); });
            }
            AddButton(_content.transform, "+ New villager", 275, -570, 180, () =>
            { VillagerLifecycle.Spawn(_colony); Refresh(); });
            Pager(members.Count);
        }

        private void BuildMemberDetail(List<ZDOID> members)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(_selectedMember) : null;
            VillagerState state = new VillagerState(zdo);
            AddButton(_content.transform, "← Members", -315, -160, 140,
                () => { _selectedMember=ZDOID.None; _pendingRemoval=ZDOID.None; Refresh(); });
            bool confirming = _pendingRemoval == _selectedMember;
            ZDOID removing = _selectedMember;
            AddButton(_content.transform, confirming ? "Confirm remove?" : "Remove villager", 300, -160, 160, () =>
            {
                if (!confirming) { _pendingRemoval = removing; Refresh(); return; }
                VillagerLifecycle.Remove(_colony, removing);
                _pendingRemoval = ZDOID.None; _selectedMember = ZDOID.None; Refresh();
            });
            TextAt(_content.transform, ColonyAssignments.NameOf(_selectedMember), -20, -160, 22, 400, TextAnchor.MiddleLeft);
            LeftTextAt(_content.transform, "Current activity: " + ColonyAssignments.DescribeActivity(_selectedMember),
                -210, 15, 440, Color.gray);
            List<Outfit> outfits = _colony.State.GetEffectiveOutfits();
            AddButton(_content.transform, "Outfit: " + Short(state.OutfitName.Length == 0 ? outfits[0].Name : state.OutfitName, 12),
                290, -210, 200, () =>
                {
                    int at = outfits.FindIndex(o => o.Name == state.OutfitName);
                    state.SetOutfitName(outfits[(at + 1 + outfits.Count) % outfits.Count].Name);
                    Refresh();
                });
            List<string> queue = state.GetQueue();
            LeftTextAt(_content.transform, "Queue", -255, 18, 160);
            AddButton(_content.transform, "Clear queue", 300, -255, 130,
                () => { ColonyAssignments.SetQueue(_selectedMember, new List<string>()); Refresh(); });
            List<ColonyJobConfig> jobs = _colony.State.GetEffectiveJobs();
            for (int i=0; i<queue.Count && i<6; i++)
            {
                ColonyJobConfig queued = jobs.Find(j => j.Id == queue[i]);
                LeftTextAt(_content.transform, $"{i+1}. {(queued != null ? queued.Name : "(missing job)")}",
                    -295-i*38, 15, 420,
                    i == state.QueuePosition ? GUIManager.Instance.ValheimOrange : Color.white);
            }
            for (int i = 0; i < jobs.Count; i++)
            {
                ColonyJobConfig job = jobs[i];
                ColonyJobConfig captured=job;
                int row = i / 4;
                int column = i % 4;
                AddButton(_content.transform, "+ " + QueueButtonName(job), -270 + column * 180,
                    -535 - row * 40, 155,
                    () => { ColonyAssignments.AppendJob(_selectedMember, captured.Id); Refresh(); });
            }
        }

        private void BuildJobs()
        {
            List<ColonyJobConfig> jobs = _colony.State.GetEffectiveJobs();
            if (_selectedJob != null) { BuildJobCard(jobs); return; }
            AddButton(_content.transform, _showPresets ? "Configured jobs" : "Saved presets", 310, -160, 160,
                () => { _showPresets = !_showPresets; _page = 0; Refresh(); });
            if (_showPresets) { BuildPresets(jobs); return; }
            LeftTextAt(_content.transform, "Configured jobs", -160, 18, 240);
            AddButton(_content.transform, "New job", 150, -160, 140, () =>
            {
                jobs.Add(new ColonyJobConfig { Name = "New job", Type = ColonyJobType.HaulLoose });
                SaveJobs(jobs);
            });
            int start = _page * Rows;
            for (int row=0; row<Rows && start+row<jobs.Count; row++)
            {
                ColonyJobConfig job=jobs[start+row];
                TextAt(_content.transform, job.Name, -220, -210-row*48, 16, 390, TextAnchor.MiddleLeft);
                TextAt(_content.transform, JobSummary(job), 100, -210-row*48, 14, 250, TextAnchor.MiddleLeft, Color.gray);
                AddButton(_content.transform, "Configure", 310, -210-row*48, 130, () => { _selectedJob=job; Refresh(); });
            }
            Pager(jobs.Count);
            LeftTextAt(_content.transform, $"{_colony.State.GetPresets().Count} saved presets",
                -555, 14, 260, Color.gray);
        }

        private void BuildPresets(List<ColonyJobConfig> jobs)
        {
            List<JobPreset> presets = _colony.State.GetPresets();
            LeftTextAt(_content.transform, "Saved presets", -160, 18, 240);
            int start = _page * Rows;
            for (int row = 0; row < Rows && start + row < presets.Count; row++)
            {
                JobPreset preset = presets[start + row];
                TextAt(_content.transform, preset.Name, -220, -210-row*48, 16, 340, TextAnchor.MiddleLeft);
                TextAt(_content.transform, preset.ColonyLocal ? "colony-local targets" : "portable settings",
                    85, -210-row*48, 14, 220, TextAnchor.MiddleLeft, preset.ColonyLocal ? GUIManager.Instance.ValheimOrange : Color.gray);
                AddButton(_content.transform, "Apply", 310, -210-row*48, 110, () =>
                {
                    jobs.Add(ColonyOperations.ApplyPreset(preset));
                    _colony.State.SetJobs(jobs);
                    _showPresets = false;
                    Refresh();
                });
            }
            Pager(presets.Count);
            LeftTextAt(_content.transform, "Portable presets can be reused without stale ZDO targets.",
                -555, 14, 520, Color.gray);
        }

        private void BuildJobCard(List<ColonyJobConfig> jobs)
        {
            if (_showTargetPicker) { BuildTargetPicker(jobs); return; }
            AddButton(_content.transform, "← Jobs", -330, -160, 110,
                () => { _selectedJob=null; Refresh(); });
            InputField jobName = InputAt(_content.transform, _selectedJob.Name, -110, -160, 330);
            jobName.onEndEdit.AddListener(value => { if (!string.IsNullOrWhiteSpace(value)) _selectedJob.Name=value.Trim(); SaveJobs(jobs); });
            LeftTextAt(_content.transform, ColonyJobCatalog.Describe(_selectedJob.Type), -215, 13, 330, Color.gray);
            LeftTextAt(_content.transform, "Needs: " + CapabilityName(ColonyJobCatalog.RequiredCapability(_selectedJob.Type)),
                -242, 12, 330, Color.gray);
            AddButton(_content.transform, "Work: " + ColonyJobCatalog.DisplayName(_selectedJob.Type), 200, -215, 380,
                () => { _selectedJob.Type = ColonyJobCatalog.Next(_selectedJob.Type); SaveJobs(jobs); });
            AddButton(_content.transform, "Duplicate", 310, -160, 110, () => { ColonyJobConfig copy=_selectedJob.Clone(true); copy.Id=System.Guid.NewGuid().ToString("N"); copy.Name += " copy"; jobs.Add(copy); SaveJobs(jobs); });
            LeftTextAt(_content.transform, "Targets: " + _selectedJob.Targets, -275, 16, 420);
            AddButton(_content.transform, "Target mode", 270, -255, 150, () =>
            { _selectedJob.Targets=(TargetMode)(((int)_selectedJob.Targets+1)%3); SaveJobs(jobs); });
            LeftTextAt(_content.transform, $"Count: {_selectedJob.Count}", -305, 16, 180);
            AddButton(_content.transform, "−", -80, -305, 45, () => { _selectedJob.Count=Mathf.Max(1,_selectedJob.Count-1); SaveJobs(jobs); });
            AddButton(_content.transform, "+", -25, -305, 45, () => { _selectedJob.Count++; SaveJobs(jobs); });
            TextAt(_content.transform, $"Stock limit: {_selectedJob.StockLimit}", 115, -305, 16, 220, TextAnchor.MiddleLeft);
            AddButton(_content.transform, "−", 300, -305, 45, () => { _selectedJob.StockLimit=Mathf.Max(0,_selectedJob.StockLimit-1); SaveJobs(jobs); });
            AddButton(_content.transform, "+", 355, -305, 45, () => { _selectedJob.StockLimit++; SaveJobs(jobs); });
            LeftTextAt(_content.transform, "Items: " + (_selectedJob.ItemFilters.Count == 0 ? "any" : string.Join(", ", _selectedJob.ItemFilters)),
                -360, 15, 400, Color.gray);
            InputField items = InputAt(_content.transform, string.Join(",", _selectedJob.ItemFilters), 170, -360, 300);
            items.onEndEdit.AddListener(value =>
            {
                _selectedJob.ItemFilters.Clear();
                foreach (string item in value.Split(',')) if (!string.IsNullOrWhiteSpace(item)) _selectedJob.ItemFilters.Add(item.Trim());
                SaveJobs(jobs);
            });
            LeftTextAt(_content.transform, "Selected structures: " + _selectedJob.SelectedStructures.Count,
                -405, 15, 360, Color.gray);
            AddButton(_content.transform, "Choose targets", 210, -405, 180,
                () => { _showTargetPicker = true; _search = string.Empty; _page = 0; Refresh(); });
            // Only what this work reads. A control that does nothing is worse than a missing
            // one: the player changes it, nothing happens, and nothing says why.
            JobSetting reads = Reads(_selectedJob);
            if ((reads & JobSetting.Source) != 0)
                AddButton(_content.transform, "Source: " + Short(StructureName(_selectedJob.Source), 16), -195, -445, 250,
                    () => { _selectedJob.Source = NextStructure(_selectedJob.Source, StructureCapability.Container); SaveJobs(jobs); });
            if ((reads & JobSetting.Destination) != 0)
                AddButton(_content.transform, "Destination: " + Short(StructureName(_selectedJob.Destination), 16), 110, -445, 270,
                    () => { _selectedJob.Destination = NextStructure(_selectedJob.Destination, StructureCapability.Container); SaveJobs(jobs); });
            AddButton(_content.transform, _selectedJob.Reservations ? "Reservations: on" : "Reservations: off", 325, -445, 150,
                () => { _selectedJob.Reservations = !_selectedJob.Reservations; SaveJobs(jobs); });
            if ((reads & JobSetting.SearchRadius) != 0)
            {
                TextAt(_content.transform, $"Search: {_selectedJob.SearchRadius:F0}m", -255, -485, 16, 150, TextAnchor.MiddleLeft);
                AddButton(_content.transform, "−", -130, -485, 45, () => { _selectedJob.SearchRadius=Mathf.Max(4,_selectedJob.SearchRadius-4); SaveJobs(jobs); });
                AddButton(_content.transform, "+", -75, -485, 45, () => { _selectedJob.SearchRadius=Mathf.Min(128,_selectedJob.SearchRadius+4); SaveJobs(jobs); });
            }
            TextAt(_content.transform, $"Stop: {_selectedJob.StopDistance:F1}m", 40, -485, 16, 150, TextAnchor.MiddleLeft);
            AddButton(_content.transform, "−", 175, -485, 45, () => { _selectedJob.StopDistance=Mathf.Max(.5f,_selectedJob.StopDistance-.5f); SaveJobs(jobs); });
            AddButton(_content.transform, "+", 230, -485, 45, () => { _selectedJob.StopDistance=Mathf.Min(8,_selectedJob.StopDistance+.5f); SaveJobs(jobs); });
            if ((reads & JobSetting.DropOnGround) != 0)
                AddButton(_content.transform, _selectedJob.DropOnGround ? "Result: ground" : "Result: container",
                    328, -485, 140, () => { _selectedJob.DropOnGround = !_selectedJob.DropOnGround; SaveJobs(jobs); });

            AddButton(_content.transform, "Save portable preset", -150, -530, 220,
                () => { ColonyOperations.SavePreset(_colony, _selectedJob.Name+" portable", _selectedJob, false); Refresh(); });
            AddButton(_content.transform, "Save local preset", 150, -530, 220,
                () => { ColonyOperations.SavePreset(_colony, _selectedJob.Name+" local", _selectedJob, true); Refresh(); });
            LeftTextAt(_content.transform, "Portable presets omit exact structure IDs; local presets retain them.",
                -575, 14, 590, Color.gray);
        }

        private void BuildTargetPicker(List<ColonyJobConfig> jobs)
        {
            StructureCapability required = ColonyJobCatalog.RequiredCapability(_selectedJob.Type);
            AddButton(_content.transform, "← Job", -330, -160, 110,
                () => { _showTargetPicker=false; Refresh(); });
            TextAt(_content.transform, "Choose " + CapabilityName(required) + " targets", -60, -160, 20, 420, TextAnchor.MiddleLeft);
            InputField search = InputAt(_content.transform, _search, -110, -205, 420);
            search.onEndEdit.AddListener(value => { _search=value; _page=0; Refresh(); });
            AddButton(_content.transform, "Sort: " + _sort, 285, -205, 130,
                () => { _sort=(StructureSort)(((int)_sort+1)%4); Refresh(); });
            List<StructureRecord> choices = ColonyOperations.FilterStructures(_colony, _search, required, _sort);
            int start = _page * Rows;
            for (int row = 0; row < Rows && start + row < choices.Count; row++)
            {
                StructureRecord record = choices[start + row];
                bool selected = _selectedJob.SelectedStructures.Contains(record.Id);
                AddButton(_content.transform, selected ? "✓ " + Short(record.Name, 22) : "○ " + Short(record.Name, 22),
                    -190, -260-row*48, 360, () =>
                    {
                        if (!_selectedJob.SelectedStructures.Remove(record.Id)) _selectedJob.SelectedStructures.Add(record.Id);
                        SaveJobs(jobs);
                    });
                TextAt(_content.transform, record.IsLiveIn(_colony) ? "ready" : "unavailable",
                    150, -260-row*48, 14, 150, TextAnchor.MiddleLeft,
                    record.IsLiveIn(_colony) ? Color.green : Color.gray);
            }
            Pager(choices.Count);
            AddButton(_content.transform, "Done", 300, -555, 120,
                () => { _showTargetPicker=false; Refresh(); });
        }

        /// <summary>
        ///     A job row's second column. Which work it does is only worth repeating when the
        ///     player has renamed the job away from it; on a starter job the two are the same
        ///     string and printing both says nothing twice.
        /// </summary>
        /// <summary>
        ///     Everything nearby the colony could use, so a player can register one thing
        ///     rather than sweeping up everything at once.
        /// </summary>
        /// <remarks>
        ///     Registering all of it is still one button away, and is what most bases want. But
        ///     a sweep cannot express "that chest, not that one", and until this existed the
        ///     only way to leave something out was to register it and then remove it.
        /// </remarks>
        private void BuildRegisterPicker()
        {
            AddButton(_content.transform, "← Structures", -310, -160, 150,
                () => { _showRegisterPicker = false; _page = 0; Refresh(); });
            TextAt(_content.transform, "Register something nearby", -35, -160, 20, 400, TextAnchor.MiddleLeft);
            InputField search = InputAt(_content.transform, _search, -170, -205, 300);
            search.onEndEdit.AddListener(value => { _search = value; _page = 0; Refresh(); });
            AddButton(_content.transform, "Register all", 280, -205, 200,
                () => { ColonyOperations.RegisterDiscovered(_colony); Refresh(); });

            List<StructureRecord> known = _colony.State.GetStructures();
            List<StructureRecord> candidates = new List<StructureRecord>();
            foreach (StructureRecord candidate in StructureRegistry.FindRegisterable(_colony))
            {
                if (known.Exists(existing => existing.Id == candidate.Id)) continue;
                if (_search.Length > 0 &&
                    candidate.Name.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    candidate.Prefab.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                candidates.Add(candidate);
            }

            int start = _page * Rows;
            for (int row = 0; row < Rows && start + row < candidates.Count; row++)
            {
                StructureRecord candidate = candidates[start + row];
                float y = -260 - row * 48;
                AddButton(_content.transform, "+ " + Short(candidate.Name, 22), -215, y, 290,
                    () => { _colony.RegisterStructure(candidate); Refresh(); });
                TextAt(_content.transform, candidate.Capabilities.ToString(), 190, y, 13, 200,
                    TextAnchor.MiddleLeft, Color.gray);
            }
            Pager(candidates.Count);
            LeftTextAt(_content.transform, candidates.Count == 0
                    ? "Nothing unregistered within " + Mathf.RoundToInt(_colony.EffectiveRadius) + "m."
                    : "You can also look at something and press " + ModConfig.MarkStructureHotkey.Value + ".",
                -575, 14, 590, Color.gray);
        }

        /// <summary>
        ///     The colony's outfits, and the one being edited.
        /// </summary>
        /// <remarks>
        ///     An outfit is a preference, so this edits names rather than items: nothing here
        ///     moves anything, and naming a piece the colony does not own is allowed. The
        ///     villagers go and find it, and go without until they do.
        /// </remarks>
        private void BuildOutfits()
        {
            List<Outfit> outfits = _colony.State.GetEffectiveOutfits();
            if (_selectedOutfit >= 0 && _selectedOutfit < outfits.Count)
            {
                BuildOutfitCard(outfits, outfits[_selectedOutfit]);
                return;
            }
            _selectedOutfit = -1;
            AddButton(_content.transform, "← Villagers", -315, -160, 140,
                () => { _showOutfits = false; _page = 0; Refresh(); });
            TextAt(_content.transform, "Outfits", -55, -160, 20, 300, TextAnchor.MiddleLeft);
            AddButton(_content.transform, "+ New outfit", 300, -160, 150, () =>
            {
                outfits.Add(new Outfit { Name = "Outfit " + (outfits.Count + 1) });
                SaveOutfits(outfits);
            });
            int start = _page * Rows;
            for (int row = 0; row < Rows && start + row < outfits.Count; row++)
            {
                int index = start + row;
                Outfit outfit = outfits[index];
                float y = -210 - row * 48;
                TextAt(_content.transform, Short(outfit.Name, 22), -225, y, 16, 290, TextAnchor.MiddleLeft);
                TextAt(_content.transform, Describe(outfit), 55, y, 13, 230, TextAnchor.MiddleLeft, Color.gray);
                AddButton(_content.transform, "Edit", 300, y, 120, () => { _selectedOutfit = index; Refresh(); });
            }
            Pager(outfits.Count);
            LeftTextAt(_content.transform, "Villagers fetch what their outfit names and go without until they do.",
                -575, 14, 590, Color.gray);
        }

        private void BuildOutfitCard(List<Outfit> outfits, Outfit outfit)
        {
            AddButton(_content.transform, "← Outfits", -320, -160, 130,
                () => { _selectedOutfit = -1; Refresh(); });
            InputField name = InputAt(_content.transform, outfit.Name, -80, -160, 330);
            name.onEndEdit.AddListener(value =>
            { if (!string.IsNullOrWhiteSpace(value)) outfit.Name = value.Trim(); SaveOutfits(outfits); });
            // The starter outfit is not stored until something is changed, so there is always
            // one left to fall back to and nothing has to guard against an empty list.
            AddButton(_content.transform, "Delete", 300, -160, 120, () =>
            {
                if (outfits.Count <= 1) return;
                outfits.Remove(outfit);
                _selectedOutfit = -1;
                SaveOutfits(outfits);
            });
            for (int slot = 0; slot < Outfit.SlotCount; slot++)
            {
                int index = slot;
                float y = -215 - slot * 48;
                LeftTextAt(_content.transform, SlotLabel((OutfitSlot)slot), y, 15, 220);
                InputField item = InputAt(_content.transform, outfit.Items[index] ?? string.Empty, 40, y, 330);
                item.onEndEdit.AddListener(value =>
                { outfit.Items[index] = (value ?? string.Empty).Trim(); SaveOutfits(outfits); });
            }
            LeftTextAt(_content.transform, "Item prefab names, such as ArmorLeatherChest or AxeFlint. Blank leaves the slot alone.",
                -560, 14, 600, Color.gray);
        }

        private void SaveOutfits(List<Outfit> outfits) { _colony.State.SetOutfits(outfits); Refresh(); }

        private static string SlotLabel(OutfitSlot slot)
        {
            switch (slot)
            {
                case OutfitSlot.Helmet: return "Head";
                case OutfitSlot.Chest: return "Chest";
                case OutfitSlot.Legs: return "Legs";
                case OutfitSlot.Shoulder: return "Cape";
                case OutfitSlot.Utility: return "Utility";
                case OutfitSlot.RightHand: return "Right hand";
                default: return "Left hand";
            }
        }

        private static string Describe(Outfit outfit)
        {
            List<string> worn = outfit.Wanted();
            return worn.Count == 0 ? "nothing set" : Short(string.Join(", ", worn.ToArray()), 30);
        }

        /// <summary>
        ///     Which of the varying settings this job reads. Work the registry does not know
        ///     shows none of them rather than all of them - guessing would be the mistake this
        ///     exists to prevent.
        /// </summary>
        private static JobSetting Reads(ColonyJobConfig job)
        {
            IColonyWork work = WorkRegistry.For(job.Type);
            return work != null ? work.Settings : JobSetting.None;
        }

        private static string JobSummary(ColonyJobConfig job)
        {
            string work = ColonyJobCatalog.DisplayName(job.Type);
            string tail = $"count {job.Count} • {job.Targets}";
            return job.Name == work ? tail : Short(work, 18) + " • " + tail;
        }

        private void SaveJobs(List<ColonyJobConfig> jobs) { _colony.State.SetJobs(jobs); Refresh(); }
        private void Pager(int count)
        {
            int pages=Mathf.Max(1,Mathf.CeilToInt(count/(float)Rows)); _page=Mathf.Clamp(_page,0,pages-1);
            AddButton(_content.transform, "‹", -65, -600, 50, () => { _page=Mathf.Max(0,_page-1); Refresh(); });
            TextAt(_content.transform, $"{_page+1} / {pages}", 0, -600, 14, 80);
            AddButton(_content.transform, "›", 65, -600, 50, () => { _page=Mathf.Min(pages-1,_page+1); Refresh(); });
        }


        private static IEnumerable<Transform> Children(Transform parent) { foreach(Transform child in parent) yield return child; }
        private static string Short(string value,int length) => value.Length<=length ? value : value.Substring(0,length-1)+"…";
        private string StructureName(ZDOID id)
        {
            if (id.IsNone()) return "automatic";
            StructureRecord record = _colony.State.GetStructures().Find(r => r.Id == id);
            return record != null ? record.Name : "missing";
        }
        private ZDOID NextStructure(ZDOID current, StructureCapability capability)
        {
            List<StructureRecord> records = ColonyOperations.FilterStructures(_colony, string.Empty, capability, StructureSort.Name);
            if (records.Count == 0) return ZDOID.None;
            int index = records.FindIndex(r => r.Id == current);
            return index < 0 ? records[0].Id : index + 1 < records.Count ? records[index + 1].Id : ZDOID.None;
        }
        private static StructureCapability NextCapability(StructureCapability current)
        {
            if (current == StructureCapability.None) return StructureCapability.Container;
            int next = (int)current << 1;
            return next > (int)StructureCapability.BeeHive ? StructureCapability.None : (StructureCapability)next;
        }
        private static string CapabilityName(StructureCapability capability) => capability == StructureCapability.None ? "All" : capability.ToString();
        private static string QueueButtonName(ColonyJobConfig job)
        {
            switch (job.Type)
            {
                case ColonyJobType.HaulLoose: return "Haul";
                case ColonyJobType.Transfer: return "Transfer";
                case ColonyJobType.FuelFireplaces: return "Fireplaces";
                case ColonyJobType.OperateSmelters: return job.Name.IndexOf("kiln", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "Kilns" : "Smelters";
                case ColonyJobType.OperateCookingStations: return "Cooking";
                case ColonyJobType.OperateFermenters: return "Fermenters";
                case ColonyJobType.CollectBeehives: return "Beehives";
                default: return Short(job.Name, 12);
            }
        }
        private static Button AddButton(Transform parent,string text,float x,float y,float width,UnityEngine.Events.UnityAction action)
        {
            Button button=GUIManager.Instance.CreateButton(text,parent,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(x,y),width,30).GetComponent<Button>();
            button.onClick.AddListener(action); return button;
        }
        private static InputField InputAt(Transform parent, string value, float x, float y, float width)
        {
            InputField input = GUIManager.Instance.CreateInputField(parent, new Vector2(.5f,1), new Vector2(.5f,1),
                new Vector2(x,y), InputField.ContentType.Standard, string.Empty, 16, width, 30).GetComponent<InputField>();
            input.text = value ?? string.Empty;
            return input;
        }
        /// <summary>
        ///     Left edge of the panel's content column. The wood panel is 860 wide, so a
        ///     centre-pivoted element may not extend past ±430; this keeps a visible margin.
        /// </summary>
        private const float ContentLeft = -400f;
        private const float ContentWidth = 800f;
        /// <summary>Places left-aligned copy on the content column so it cannot spill outside the panel.</summary>
        private static Text LeftTextAt(Transform parent,string text,float y,int size,float width,Color? colour=null)
        {
            float clamped = Mathf.Min(width, ContentWidth);
            return TextAt(parent, text, ContentLeft + clamped / 2f, y, size, clamped, TextAnchor.MiddleLeft, colour);
        }
        private static Text TextAt(Transform parent,string text,float x,float y,int size,float width,
            TextAnchor anchor=TextAnchor.MiddleCenter,Color? colour=null)
        {
            Text label=GUIManager.Instance.CreateText(text,parent,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(x,y),
                GUIManager.Instance.AveriaSerifBold,size,colour??Color.white,true,Color.black,width,30,false).GetComponent<Text>();
            label.alignment=anchor; return label;
        }
    }
}
