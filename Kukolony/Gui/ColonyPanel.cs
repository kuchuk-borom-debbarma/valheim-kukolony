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
        private readonly HashSet<ZDOID> _selectedMembers = new HashSet<ZDOID>();
        private int _memberJobIndex;
        private ColonyJobConfig _selectedJob;
        private bool _showPresets;
        private bool _showTargetPicker;
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
            _root.SetActive(true);
            Block(true);
            Refresh();
        }

        internal void Close()
        {
            if (_root != null) _root.SetActive(false);
            _colony = null; Block(false);
        }

        internal void ShowTabForTest(string tab)
        {
            if (Enum.TryParse(tab, true, out Tab parsed)) SetTab(parsed);
        }

        internal void ShowMemberDetailForTest(int index)
        {
            List<ZDOID> members = _colony?.State.GetMembers(ColonyMemberKind.Villager);
            if (members != null && index >= 0 && index < members.Count)
            { _selectedMember = members[index]; _tab = Tab.Members; Refresh(); }
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
            _tab = tab; _page = 0;
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
            AddButton(_content.transform, "Register nearby", 155, -155, 160, () =>
            { ColonyOperations.RegisterDiscovered(_colony); Refresh(); });
            AddButton(_content.transform, "Sort: " + _sort, 285, -155, 100, () =>
            { _sort = (StructureSort)(((int)_sort + 1) % 4); Refresh(); });
            AddButton(_content.transform, "Filter: " + CapabilityName(_capabilityFilter), 375, -155, 85, () =>
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
            TextAt(_content.transform, $"{records.Count} registered • radius {_colony.EffectiveRadius:F0}m",
                -250, -570, 14, 320, TextAnchor.MiddleLeft, Color.gray);
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
                AddButton(_content.transform, "Details", 300, -210-row*48, 120, () => { _selectedMember=id; Refresh(); });
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
            if (ModConfig.DebugSpawnEnabled.Value)
                AddButton(_content.transform, "+ Debug villager", 275, -570, 180, SpawnVillager);
            Pager(members.Count);
        }

        private void BuildMemberDetail(List<ZDOID> members)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(_selectedMember) : null;
            VillagerState state = new VillagerState(zdo);
            AddButton(_content.transform, "← Members", -315, -160, 140, () => { _selectedMember=ZDOID.None; Refresh(); });
            TextAt(_content.transform, ColonyAssignments.NameOf(_selectedMember), -120, -160, 22, 280, TextAnchor.MiddleLeft);
            TextAt(_content.transform, "Current activity: " + ColonyAssignments.DescribeActivity(_selectedMember),
                -250, -210, 15, 560, TextAnchor.MiddleLeft, Color.gray);
            List<string> queue = state.GetQueue();
            TextAt(_content.transform, "Queue", -300, -255, 18, 160, TextAnchor.MiddleLeft);
            List<ColonyJobConfig> jobs = _colony.State.GetEffectiveJobs();
            for (int i=0; i<queue.Count && i<6; i++)
            {
                ColonyJobConfig queued = jobs.Find(j => j.Id == queue[i]);
                TextAt(_content.transform, $"{i+1}. {(queued != null ? queued.Name : "(missing job)")}",
                    -220, -295-i*38, 15, 420, TextAnchor.MiddleLeft,
                    i == state.QueuePosition ? GUIManager.Instance.ValheimOrange : Color.white);
            }
            int x=-300;
            foreach (ColonyJobConfig job in jobs)
            {
                ColonyJobConfig captured=job;
                AddButton(_content.transform, "+ "+Short(job.Name,15), x, -555, 130,
                    () => { ColonyAssignments.AppendJob(_selectedMember, captured.Id); Refresh(); });
                x += 140; if (x > 300) break;
            }
            AddButton(_content.transform, "Clear queue", 300, -600, 130,
                () => { ColonyAssignments.SetQueue(_selectedMember, new List<string>()); Refresh(); });
        }

        private void BuildJobs()
        {
            List<ColonyJobConfig> jobs = _colony.State.GetEffectiveJobs();
            if (_selectedJob != null) { BuildJobCard(jobs); return; }
            AddButton(_content.transform, _showPresets ? "Configured jobs" : "Saved presets", 300, -155, 160,
                () => { _showPresets = !_showPresets; _page = 0; Refresh(); });
            if (_showPresets) { BuildPresets(jobs); return; }
            TextAt(_content.transform, "Configured jobs", -290, -160, 18, 240, TextAnchor.MiddleLeft);
            int start = _page * Rows;
            for (int row=0; row<Rows && start+row<jobs.Count; row++)
            {
                ColonyJobConfig job=jobs[start+row];
                TextAt(_content.transform, job.Name, -220, -210-row*48, 16, 390, TextAnchor.MiddleLeft);
                TextAt(_content.transform, $"count {job.Count} • limit {job.StockLimit} • {job.Targets}",
                    100, -210-row*48, 14, 250, TextAnchor.MiddleLeft, Color.gray);
                AddButton(_content.transform, "Configure", 310, -210-row*48, 130, () => { _selectedJob=job; Refresh(); });
            }
            Pager(jobs.Count);
            TextAt(_content.transform, $"{_colony.State.GetPresets().Count} saved presets",
                -285, -555, 14, 260, TextAnchor.MiddleLeft, Color.gray);
        }

        private void BuildPresets(List<ColonyJobConfig> jobs)
        {
            List<JobPreset> presets = _colony.State.GetPresets();
            TextAt(_content.transform, "Saved presets", -290, -160, 18, 240, TextAnchor.MiddleLeft);
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
            TextAt(_content.transform, "Portable presets can be reused without stale ZDO targets.",
                -260, -555, 14, 520, TextAnchor.MiddleLeft, Color.gray);
        }

        private void BuildJobCard(List<ColonyJobConfig> jobs)
        {
            if (_showTargetPicker) { BuildTargetPicker(jobs); return; }
            AddButton(_content.transform, "← Jobs", -330, -160, 110, () => { _selectedJob=null; Refresh(); });
            InputField jobName = InputAt(_content.transform, _selectedJob.Name, -110, -160, 330);
            jobName.onEndEdit.AddListener(value => { if (!string.IsNullOrWhiteSpace(value)) _selectedJob.Name=value.Trim(); SaveJobs(jobs); });
            TextAt(_content.transform, "Type: " + ColonyJobCatalog.DisplayName(_selectedJob.Type), -250, -215, 16, 520, TextAnchor.MiddleLeft);
            TextAt(_content.transform, "Targets: " + _selectedJob.Targets, -250, -255, 16, 420, TextAnchor.MiddleLeft);
            AddButton(_content.transform, "Target mode", 270, -255, 150, () =>
            { _selectedJob.Targets=(TargetMode)(((int)_selectedJob.Targets+1)%3); SaveJobs(jobs); });
            TextAt(_content.transform, $"Count: {_selectedJob.Count}", -250, -305, 16, 180, TextAnchor.MiddleLeft);
            AddButton(_content.transform, "−", -80, -305, 45, () => { _selectedJob.Count=Mathf.Max(1,_selectedJob.Count-1); SaveJobs(jobs); });
            AddButton(_content.transform, "+", -25, -305, 45, () => { _selectedJob.Count++; SaveJobs(jobs); });
            TextAt(_content.transform, $"Stock limit: {_selectedJob.StockLimit}", 100, -305, 16, 220, TextAnchor.MiddleLeft);
            AddButton(_content.transform, "−", 300, -305, 45, () => { _selectedJob.StockLimit=Mathf.Max(0,_selectedJob.StockLimit-1); SaveJobs(jobs); });
            AddButton(_content.transform, "+", 355, -305, 45, () => { _selectedJob.StockLimit++; SaveJobs(jobs); });
            TextAt(_content.transform, "Items: " + (_selectedJob.ItemFilters.Count == 0 ? "any" : string.Join(", ", _selectedJob.ItemFilters)),
                -250, -360, 15, 560, TextAnchor.MiddleLeft, Color.gray);
            InputField items = InputAt(_content.transform, string.Join(",", _selectedJob.ItemFilters), 170, -360, 300);
            items.onEndEdit.AddListener(value =>
            {
                _selectedJob.ItemFilters.Clear();
                foreach (string item in value.Split(',')) if (!string.IsNullOrWhiteSpace(item)) _selectedJob.ItemFilters.Add(item.Trim());
                SaveJobs(jobs);
            });
            TextAt(_content.transform, "Selected structures: " + _selectedJob.SelectedStructures.Count,
                -250, -405, 15, 360, TextAnchor.MiddleLeft, Color.gray);
            AddButton(_content.transform, "Choose targets", 210, -405, 180,
                () => { _showTargetPicker = true; _search = string.Empty; _page = 0; Refresh(); });
            AddButton(_content.transform, "Source: " + Short(StructureName(_selectedJob.Source), 16), -195, -445, 250,
                () => { _selectedJob.Source = NextStructure(_selectedJob.Source, StructureCapability.Container); SaveJobs(jobs); });
            AddButton(_content.transform, "Destination: " + Short(StructureName(_selectedJob.Destination), 16), 120, -445, 280,
                () => { _selectedJob.Destination = NextStructure(_selectedJob.Destination, StructureCapability.Container); SaveJobs(jobs); });
            AddButton(_content.transform, _selectedJob.Reservations ? "Reservations: on" : "Reservations: off", 330, -445, 150,
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
            TextAt(_content.transform, "Portable presets omit exact structure IDs; local presets retain them.",
                -250, -575, 14, 590, TextAnchor.MiddleLeft, Color.gray);
        }

        private void BuildTargetPicker(List<ColonyJobConfig> jobs)
        {
            StructureCapability required = ColonyJobCatalog.RequiredCapability(_selectedJob.Type);
            AddButton(_content.transform, "← Job", -330, -160, 110, () => { _showTargetPicker=false; Refresh(); });
            TextAt(_content.transform, "Choose " + CapabilityName(required) + " targets", -135, -160, 20, 370, TextAnchor.MiddleLeft);
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
            AddButton(_content.transform, "Done", 300, -555, 120, () => { _showTargetPicker=false; Refresh(); });
        }

        private void SaveJobs(List<ColonyJobConfig> jobs) { _colony.State.SetJobs(jobs); Refresh(); }
        private void Pager(int count)
        {
            int pages=Mathf.Max(1,Mathf.CeilToInt(count/(float)Rows)); _page=Mathf.Clamp(_page,0,pages-1);
            AddButton(_content.transform, "‹", -65, -600, 50, () => { _page=Mathf.Max(0,_page-1); Refresh(); });
            TextAt(_content.transform, $"{_page+1} / {pages}", 0, -600, 14, 80);
            AddButton(_content.transform, "›", 65, -600, 50, () => { _page=Mathf.Min(pages-1,_page+1); Refresh(); });
        }

        private void SpawnVillager()
        {
            GameObject prefab=ZNetScene.instance?.GetPrefab(VillagerPrefab.PrefabName);
            if (prefab == null) return;
            GameObject spawned=Instantiate(prefab,_colony.transform.position+_colony.transform.forward*3f+Vector3.up,Quaternion.identity);
            if (spawned != null && spawned.TryGetComponent(out ZNetView view)) _colony.Register(ColonyMemberKind.Villager,view);
            Refresh();
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
        private static Text TextAt(Transform parent,string text,float x,float y,int size,float width,
            TextAnchor anchor=TextAnchor.MiddleCenter,Color? colour=null)
        {
            Text label=GUIManager.Instance.CreateText(text,parent,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(x,y),
                GUIManager.Instance.AveriaSerifBold,size,colour??Color.white,true,Color.black,width,30,false).GetComponent<Text>();
            label.alignment=anchor; return label;
        }
    }
}
