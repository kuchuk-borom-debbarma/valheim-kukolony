using System;
using System.Collections.Generic;
using Jotunn.Managers;
using Kukolony.Colonies;
using Kukolony.Jobs;
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
        /// <summary>Sub-screens for editing a job's pipeline; cleared on every navigation.</summary>
        private bool _showPieceEditor;
        private bool _showPiecePicker;
        private int _selectedPiece = -1;
        /// <summary>True while the structure picker is choosing a container for one piece.</summary>
        private bool _pickingPieceContainer;
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

        /// <summary>Opens a job's pipeline editor for UI evidence.</summary>
        internal void ShowPieceEditorForTest()
        {
            if (_selectedJob == null) return;
            ClosePieceScreens();
            _showPieceEditor = true;
            _page = 0;
            Refresh();
        }

        /// <summary>Opens the add-a-piece list. Adds nothing; the capture is the subject.</summary>
        internal void ShowPiecePickerForTest()
        {
            if (_selectedJob == null) return;
            _showPieceEditor = true;
            _showPiecePicker = true;
            _page = 0;
            Refresh();
        }

        /// <summary>Opens one piece's settings, choosing the first configurable piece.</summary>
        internal void ShowPieceSettingsForTest()
        {
            if (_selectedJob == null) return;
            ClosePieceScreens();
            _showPieceEditor = true;
            for (int i = 0; i < _selectedJob.Pieces.Count; i++)
            {
                if (PieceCustomisation.Uses(_selectedJob.Pieces[i].Kind) == JobCustomisation.None) continue;
                _selectedPiece = i;
                break;
            }
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
            _tab = tab; _page = 0; _pendingRemoval = ZDOID.None; ClosePieceScreens();
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
            InputField search = GUIManager.Instance.CreateInputField(_content.transform, new Vector2(.5f,1),
                new Vector2(.5f,1), new Vector2(-150,-155), InputField.ContentType.Standard,
                "search structures", 24, 360, 30).GetComponent<InputField>();
            search.text = _search;
            search.onEndEdit.AddListener(value => { _search = value; _page = 0; Refresh(); });
            AddButton(_content.transform, "Register nearby", 115, -155, 150, () =>
            { ColonyOperations.RegisterDiscovered(_colony); Refresh(); });
            AddButton(_content.transform, "Sort: " + _sort, 250, -155, 100, () =>
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
            if (!_selectedMember.IsNone())
            {
                BuildMemberDetail(members);
                return;
            }
            TextAt(_content.transform, "Villagers", -310, -160, 18, 180, TextAnchor.MiddleLeft);
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
                -210, 15, 560, Color.gray);
            List<string> queue = state.GetQueue();
            LeftTextAt(_content.transform, "Queue", -255, 18, 160);
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
            AddButton(_content.transform, "Clear queue", 300, -600, 130,
                () => { ColonyAssignments.SetQueue(_selectedMember, new List<string>()); Refresh(); });
        }

        private void BuildJobs()
        {
            List<ColonyJobConfig> jobs = _colony.State.GetEffectiveJobs();
            if (_selectedJob != null) { BuildJobCard(jobs); return; }
            AddButton(_content.transform, _showPresets ? "Configured jobs" : "Saved presets", 310, -160, 160,
                () => { _showPresets = !_showPresets; _page = 0; Refresh(); });
            if (_showPresets) { BuildPresets(jobs); return; }
            LeftTextAt(_content.transform, "Configured jobs", -160, 18, 240);
            AddButton(_content.transform, "New pipeline", 150, -160, 140, () =>
            {
                ColonyJobConfig created = new ColonyJobConfig { Name = "New job", Type = ColonyJobType.HaulLoose };
                created.Pieces.Add(new JobPiece { Kind = JobPieceKind.Start });
                created.Pieces.Add(new JobPiece { Kind = JobPieceKind.End });
                jobs.Add(created); SaveJobs(jobs);
            });
            int start = _page * Rows;
            for (int row=0; row<Rows && start+row<jobs.Count; row++)
            {
                ColonyJobConfig job=jobs[start+row];
                TextAt(_content.transform, job.Name, -220, -210-row*48, 16, 390, TextAnchor.MiddleLeft);
                TextAt(_content.transform, $"{job.Pieces.Count} pieces • count {job.Count} • {job.Targets}",
                    100, -210-row*48, 14, 250, TextAnchor.MiddleLeft, Color.gray);
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
            if (_showPiecePicker) { BuildPiecePicker(jobs); return; }
            if (_selectedPiece >= 0) { BuildPieceSettings(jobs); return; }
            if (_showPieceEditor) { BuildPieceEditor(jobs); return; }
            AddButton(_content.transform, "← Jobs", -330, -160, 110,
                () => { _selectedJob=null; ClosePieceScreens(); Refresh(); });
            InputField jobName = InputAt(_content.transform, _selectedJob.Name, -110, -160, 330);
            jobName.onEndEdit.AddListener(value => { if (!string.IsNullOrWhiteSpace(value)) _selectedJob.Name=value.Trim(); SaveJobs(jobs); });
            bool valid = JobPipeline.IsValid(_selectedJob, out string validation);
            LeftTextAt(_content.transform, valid ? "Pipeline valid — compatible customisation auto-connects." : validation,
                -215, 14, 560, valid ? Color.green : Color.red);
            LeftTextAt(_content.transform, "Pieces: " + string.Join(" → ",
                _selectedJob.Pieces.ConvertAll(piece => PieceLabel(piece.Kind)).ToArray()),
                -242, 12, 430, Color.gray);
            AddButton(_content.transform, $"Edit pieces ({_selectedJob.Pieces.Count})", 300, -215, 180,
                () => { _showPieceEditor=true; _page=0; Refresh(); });
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
            AddButton(_content.transform, "Source: " + Short(StructureName(_selectedJob.Source), 16), -195, -445, 250,
                () => { _selectedJob.Source = NextStructure(_selectedJob.Source, StructureCapability.Container); SaveJobs(jobs); });
            AddButton(_content.transform, "Destination: " + Short(StructureName(_selectedJob.Destination), 16), 110, -445, 270,
                () => { _selectedJob.Destination = NextStructure(_selectedJob.Destination, StructureCapability.Container); SaveJobs(jobs); });
            AddButton(_content.transform, _selectedJob.Reservations ? "Reservations: on" : "Reservations: off", 325, -445, 150,
                () => { _selectedJob.Reservations = !_selectedJob.Reservations; SaveJobs(jobs); });
            TextAt(_content.transform, $"Search: {_selectedJob.SearchRadius:F0}m", -255, -485, 16, 150, TextAnchor.MiddleLeft);
            AddButton(_content.transform, "−", -130, -485, 45, () => { _selectedJob.SearchRadius=Mathf.Max(4,_selectedJob.SearchRadius-4); SaveJobs(jobs); });
            AddButton(_content.transform, "+", -75, -485, 45, () => { _selectedJob.SearchRadius=Mathf.Min(128,_selectedJob.SearchRadius+4); SaveJobs(jobs); });
            TextAt(_content.transform, $"Stop: {_selectedJob.StopDistance:F1}m", 40, -485, 16, 150, TextAnchor.MiddleLeft);
            AddButton(_content.transform, "−", 175, -485, 45, () => { _selectedJob.StopDistance=Mathf.Max(.5f,_selectedJob.StopDistance-.5f); SaveJobs(jobs); });
            AddButton(_content.transform, "+", 230, -485, 45, () => { _selectedJob.StopDistance=Mathf.Min(8,_selectedJob.StopDistance+.5f); SaveJobs(jobs); });
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
            AddButton(_content.transform, "← " + (_pickingPieceContainer ? "Piece" : "Job"), -330, -160, 110,
                () => { _showTargetPicker=false; _pickingPieceContainer=false; Refresh(); });
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
                bool forPiece = _pickingPieceContainer && _selectedPiece >= 0 && _selectedPiece < _selectedJob.Pieces.Count;
                AddButton(_content.transform, selected && !forPiece ? "✓ " + Short(record.Name, 22) : "○ " + Short(record.Name, 22),
                    -190, -260-row*48, 360, () =>
                    {
                        if (forPiece)
                        {
                            _selectedJob.Pieces[_selectedPiece].Container = record.Id;
                            _pickingPieceContainer = false;
                            _showTargetPicker = false;
                        }
                        else if (!_selectedJob.SelectedStructures.Remove(record.Id)) _selectedJob.SelectedStructures.Add(record.Id);
                        SaveJobs(jobs);
                    });
                TextAt(_content.transform, record.IsLiveIn(_colony) ? "ready" : "unavailable",
                    150, -260-row*48, 14, 150, TextAnchor.MiddleLeft,
                    record.IsLiveIn(_colony) ? Color.green : Color.gray);
            }
            Pager(choices.Count);
            AddButton(_content.transform, "Done", 300, -555, 120,
                () => { _showTargetPicker=false; _pickingPieceContainer=false; Refresh(); });
        }

        /// <summary>Leaves every pipeline sub-screen, so navigation cannot strand the player.</summary>
        private void ClosePieceScreens()
        {
            _showPieceEditor = false;
            _showPiecePicker = false;
            _selectedPiece = -1;
            _pickingPieceContainer = false;
        }

        /// <summary>
        ///     The pipeline itself: what the job does, in order. Start and End cannot be moved
        ///     or removed, so the shape rule that a job runs between them cannot be broken from
        ///     here.
        /// </summary>
        private void BuildPieceEditor(List<ColonyJobConfig> jobs)
        {
            List<JobPiece> pieces = _selectedJob.Pieces;
            AddButton(_content.transform, "← Job", -330, -160, 110,
                () => { ClosePieceScreens(); Refresh(); });
            TextAt(_content.transform, "Pieces — " + Short(_selectedJob.Name, 24), -60, -160, 20, 420, TextAnchor.MiddleLeft);
            bool valid = JobPipeline.IsValid(_selectedJob, out string validation);
            LeftTextAt(_content.transform, valid ? "Pipeline valid — every piece has what it needs." : validation,
                -205, 14, 560, valid ? Color.green : Color.red);
            AddButton(_content.transform, "Add piece", 300, -205, 180,
                () => { _showPiecePicker=true; _page=0; Refresh(); });

            int start = _page * Rows;
            for (int row = 0; row < Rows && start + row < pieces.Count; row++)
            {
                int index = start + row;
                JobPiece piece = pieces[index];
                bool fixedPiece = piece.Kind == JobPieceKind.Start || piece.Kind == JobPieceKind.End;
                float y = -260 - row * 48;
                TextAt(_content.transform, $"{index + 1}. {PieceLabel(piece.Kind)}", -330, y, 16, 130, TextAnchor.MiddleLeft);
                TextAt(_content.transform, SettingsSummary(piece), -150, y, 13, 200, TextAnchor.MiddleLeft, Color.gray);
                if (fixedPiece) continue;
                AddButton(_content.transform, "↑", 15, y, 45, () => { Reorder(jobs, index, index - 1); });
                AddButton(_content.transform, "↓", 70, y, 45, () => { Reorder(jobs, index, index + 1); });
                AddButton(_content.transform, "Configure", 175, y, 130, () => { _selectedPiece=index; Refresh(); });
                AddButton(_content.transform, "Remove", 320, y, 120,
                    () => { pieces.RemoveAt(index); SaveJobs(jobs); });
            }
            Pager(pieces.Count);
            AddButton(_content.transform, "Done", 300, -555, 120, () => { ClosePieceScreens(); Refresh(); });
        }

        /// <summary>Moves a piece, refusing to push it outside Start and End.</summary>
        private void Reorder(List<ColonyJobConfig> jobs, int from, int to)
        {
            List<JobPiece> pieces = _selectedJob.Pieces;
            if (to < 1 || to > pieces.Count - 2 || from < 1 || from > pieces.Count - 2) return;
            JobPiece moved = pieces[from];
            pieces.RemoveAt(from);
            pieces.Insert(to, moved);
            SaveJobs(jobs);
        }

        /// <summary>Pieces a player can add, inserted before End so the shape stays valid.</summary>
        private void BuildPiecePicker(List<ColonyJobConfig> jobs)
        {
            AddButton(_content.transform, "← Pieces", -325, -160, 130,
                () => { _showPiecePicker=false; _page=0; Refresh(); });
            TextAt(_content.transform, "Add a piece", -50, -160, 20, 420, TextAnchor.MiddleLeft);
            int start = _page * Rows;
            for (int row = 0; row < Rows && start + row < Addable.Length; row++)
            {
                JobPieceKind kind = Addable[start + row];
                AddButton(_content.transform, PieceLabel(kind), -215, -260 - row * 48, 290, () =>
                {
                    _selectedJob.Pieces.Insert(Mathf.Max(1, _selectedJob.Pieces.Count - 1), new JobPiece { Kind = kind });
                    _showPiecePicker = false;
                    SaveJobs(jobs);
                });
                TextAt(_content.transform, SettingNames(kind), 200, -260 - row * 48, 12, 180,
                    TextAnchor.MiddleLeft, Color.gray);
            }
            Pager(Addable.Length);
            AddButton(_content.transform, "Cancel", 300, -555, 120, () => { _showPiecePicker=false; Refresh(); });
        }

        /// <summary>
        ///     One piece's settings. Only what this kind actually reads is shown, taken from
        ///     the piece contract rather than a list kept in the UI, so a kind cannot end up
        ///     offering a setting it ignores.
        /// </summary>
        private void BuildPieceSettings(List<ColonyJobConfig> jobs)
        {
            List<JobPiece> pieces = _selectedJob.Pieces;
            if (_selectedPiece >= pieces.Count) { _selectedPiece = -1; return; }
            JobPiece piece = pieces[_selectedPiece];
            AddButton(_content.transform, "← Pieces", -325, -160, 130, () => { _selectedPiece=-1; Refresh(); });
            TextAt(_content.transform, PieceLabel(piece.Kind), -50, -160, 20, 420, TextAnchor.MiddleLeft);
            LeftTextAt(_content.transform, "Blank settings follow the job.", -205, 13, 420, Color.gray);

            JobCustomisation uses = PieceCustomisation.Uses(piece.Kind);
            float y = -250;
            if ((uses & JobCustomisation.ItemFilter) != 0)
            {
                LeftTextAt(_content.transform, "Items: " + (piece.ItemFilters.Count == 0 ? "job" : string.Join(", ", piece.ItemFilters)), y, 14, 300, Color.gray);
                InputField items = InputAt(_content.transform, string.Join(",", piece.ItemFilters), 190, y, 300);
                items.onEndEdit.AddListener(value =>
                {
                    piece.ItemFilters.Clear();
                    foreach (string item in value.Split(',')) if (!string.IsNullOrWhiteSpace(item)) piece.ItemFilters.Add(item.Trim());
                    SaveJobs(jobs);
                });
                y -= 45;
            }
            if ((uses & JobCustomisation.Container) != 0)
            {
                LeftTextAt(_content.transform, "Container: " + (piece.Container.IsNone() ? "job" : Short(StructureName(piece.Container), 18)), y, 14, 300);
                AddButton(_content.transform, "Pick", 210, y, 100, () =>
                { _pickingPieceContainer = true; _showTargetPicker = true; _page = 0; Refresh(); });
                AddButton(_content.transform, "Clear", 315, y, 100, () =>
                { piece.Container = ZDOID.None; SaveJobs(jobs); });
                y -= 45;
            }
            if ((uses & JobCustomisation.Amount) != 0) y = Stepper(jobs, "Amount", piece.Amount, y, v => piece.Amount = v, -1);
            if ((uses & JobCustomisation.StockLimit) != 0) y = Stepper(jobs, "Stock limit", piece.StockLimit, y, v => piece.StockLimit = v, -1);
            if ((uses & JobCustomisation.SearchRadius) != 0)
                y = Stepper(jobs, "Search radius", Mathf.RoundToInt(piece.SearchRadius), y, v => piece.SearchRadius = v, -1, 4);
            if ((uses & JobCustomisation.StopDistance) != 0)
                y = Stepper(jobs, "Stop distance", Mathf.RoundToInt(piece.StopDistance), y, v => piece.StopDistance = v, -1);
            if ((uses & JobCustomisation.TargetScope) != 0)
            {
                LeftTextAt(_content.transform, "Targets: " + (piece.Targets < 0 ? "job" : ((TargetMode)piece.Targets).ToString()), y, 14, 300);
                AddButton(_content.transform, "Change", 250, y, 130, () =>
                { piece.Targets = piece.Targets >= 2 ? -1 : piece.Targets + 1; SaveJobs(jobs); });
                y -= 45;
            }
            if ((uses & JobCustomisation.Reservation) != 0)
            {
                LeftTextAt(_content.transform, "Reserves target: " + (piece.Reservations < 0 ? "job" : piece.Reservations != 0 ? "yes" : "no"), y, 14, 300);
                AddButton(_content.transform, "Change", 250, y, 130, () =>
                { piece.Reservations = piece.Reservations >= 1 ? -1 : piece.Reservations + 1; SaveJobs(jobs); });
                y -= 45;
            }
            if (uses == JobCustomisation.None)
                LeftTextAt(_content.transform, "This piece has nothing to configure.", y, 14, 420, Color.gray);

            AddButton(_content.transform, "Clear overrides", 290, -555, 200, () =>
            {
                piece.ItemFilters.Clear(); piece.SelectedStructures.Clear();
                piece.Container = ZDOID.None; piece.StockLimit = -1; piece.Amount = -1;
                piece.SearchRadius = -1f; piece.StopDistance = -1f; piece.Targets = -1; piece.Reservations = -1;
                SaveJobs(jobs);
            });
        }

        /// <summary>A labelled number with decrement and increment, returning the next row's y.</summary>
        private float Stepper(List<ColonyJobConfig> jobs, string label, int value, float y,
            System.Action<int> set, int inherit, int step = 1)
        {
            LeftTextAt(_content.transform, label + ": " + (value < 0 ? "job" : value.ToString()), y, 14, 300);
            AddButton(_content.transform, "−", 205, y, 45, () => { set(value - step < 0 ? inherit : value - step); SaveJobs(jobs); });
            AddButton(_content.transform, "+", 260, y, 45, () => { set(value < 0 ? step : value + step); SaveJobs(jobs); });
            return y - 45;
        }

        /// <summary>Short note of which settings a piece has had changed from the job's.</summary>
        private static string SettingsSummary(JobPiece piece)
        {
            List<string> parts = new List<string>();
            if (piece.ItemFilters.Count > 0) parts.Add(string.Join("/", piece.ItemFilters.ToArray()));
            if (piece.Amount > 0) parts.Add("x" + piece.Amount);
            if (piece.StockLimit >= 0) parts.Add("limit " + piece.StockLimit);
            if (piece.SearchRadius >= 0f) parts.Add("r" + Mathf.RoundToInt(piece.SearchRadius));
            if (piece.StopDistance >= 0f) parts.Add("d" + Mathf.RoundToInt(piece.StopDistance));
            if (piece.Targets >= 0) parts.Add(((TargetMode)piece.Targets).ToString());
            if (piece.Reservations >= 0) parts.Add(piece.Reservations != 0 ? "reserves" : "shares");
            return parts.Count == 0 ? "follows the job" : Short(string.Join(" · ", parts.ToArray()), 26);
        }

        /// <summary>What a kind can be configured by, for the add list.</summary>
        private static string SettingNames(JobPieceKind kind)
        {
            List<JobCustomisation> settings = PieceCustomisation.Settings(kind);
            if (settings.Count == 0) return "no settings";
            List<string> names = new List<string>();
            foreach (JobCustomisation setting in settings) names.Add(SettingLabel(setting));
            return Short(string.Join(", ", names.ToArray()), 22);
        }

        private static string SettingLabel(JobCustomisation setting)
        {
            switch (setting)
            {
                case JobCustomisation.ItemFilter: return "items";
                case JobCustomisation.Container: return "container";
                case JobCustomisation.StockLimit: return "limit";
                case JobCustomisation.Amount: return "amount";
                case JobCustomisation.SearchRadius: return "radius";
                case JobCustomisation.StopDistance: return "distance";
                case JobCustomisation.TargetScope: return "targets";
                case JobCustomisation.Reservation: return "reserving";
                default: return "setting";
            }
        }

        /// <summary>Kinds a player may add. Start and End are structural and never listed.</summary>
        private static readonly JobPieceKind[] Addable =
        {
            JobPieceKind.StopAtStockLimit, JobPieceKind.FindLooseItem, JobPieceKind.SelectSource,
            JobPieceKind.SelectTarget, JobPieceKind.MoveToTarget, JobPieceKind.PickUp,
            JobPieceKind.TakeItem, JobPieceKind.PutItem, JobPieceKind.OperateStation
        };

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
        private static string PieceLabel(JobPieceKind kind)
        {
            switch (kind)
            {
                case JobPieceKind.Start: return "Start";
                case JobPieceKind.StopAtStockLimit: return "Limit";
                case JobPieceKind.FindLooseItem: return "Find item";
                case JobPieceKind.SelectSource: return "Pick source";
                case JobPieceKind.SelectTarget: return "Pick target";
                case JobPieceKind.MoveToTarget: return "Move";
                case JobPieceKind.PickUp: return "Pick up";
                case JobPieceKind.TakeItem: return "Take";
                case JobPieceKind.PutItem: return "Store";
                case JobPieceKind.OperateStation: return "Operate";
                case JobPieceKind.End: return "End";
                default: return kind.ToString();
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
