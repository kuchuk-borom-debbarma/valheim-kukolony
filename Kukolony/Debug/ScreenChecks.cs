using System.Collections;
using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Gui;
using Kukolony.Jobs;
using UnityEngine;

namespace Kukolony.Debug
{
    /// <summary>
    ///     The colony screen, checked against the things it is easy to ship without.
    /// </summary>
    /// <remarks>
    ///     A user interface is the easiest thing in this project to verify vacuously: it is
    ///     visible, so it looks tested. Every claim here is therefore paired with a control
    ///     that must fail, and the layout audit gets a fixture built wrong on purpose - an
    ///     audit nobody has seen reject anything is indistinguishable from one that inspects
    ///     nothing.
    ///
    ///     Checks write into the functional run's report rather than printing their own.
    ///     TestReport.LastText is a single static slot and the controller writes only the last
    ///     report printed, so a second report here would silently replace the first in the
    ///     artifact.
    /// </remarks>
    internal static class ScreenChecks
    {
        internal static IEnumerator Run(TestReport report, Colony colony, Vector3 origin)
        {
            ColonyScreen screen = ColonyScreen.Instance;
            report.Check(screen != null, "the colony screen exists once Jotunn's GUI is available");
            if (screen == null)
            {
                yield break;
            }

            yield return Cursor(report, screen, colony);
            yield return Context(report, screen, colony, origin);
            yield return Layout(report, screen, colony);
            yield return StructureStatusReads(report, screen, colony);
            yield return Paging(report, screen, colony);
            yield return BackStack(report, screen, colony);
            yield return SubjectVanishes(report, screen, origin);

            screen.Close();
            yield return null;
        }

        /// <summary>
        ///     A panel drawn without releasing the mouse is visible and completely unusable.
        ///     Asserted on Unity's own cursor state rather than on our bookkeeping flag, which
        ///     would only prove we remembered to set our own variable.
        /// </summary>
        private static IEnumerator Cursor(TestReport report, ColonyScreen screen, Colony colony)
        {
            screen.Close();
            yield return null;
            bool capturedBefore = !UnityEngine.Cursor.visible;

            screen.Open(colony, null);
            yield return null;
            report.Check(screen.IsOpen, "the screen opens");
            report.Check(UnityEngine.Cursor.visible, "opening the screen releases the cursor",
                $"visible={UnityEngine.Cursor.visible} lock={UnityEngine.Cursor.lockState}");

            screen.Close();
            yield return null;
            report.Check(capturedBefore && !UnityEngine.Cursor.visible,
                "control: the cursor is captured again once the screen closes",
                $"beforeCaptured={capturedBefore} visibleAfter={UnityEngine.Cursor.visible}");
        }

        /// <summary>
        ///     What the player was looking at is what makes registration possible without a
        ///     held tool. Looking at nothing is an ordinary answer, not an error - the control
        ///     is what proves the screen still opens and builds in that case.
        /// </summary>
        private static IEnumerator Context(TestReport report, ColonyScreen screen, Colony colony, Vector3 origin)
        {
            GameObject chest = Fixture(origin);
            yield return null;

            screen.Open(colony, chest);
            yield return null;
            report.Check(screen.LookedAt == chest, "the screen records what the player was looking at",
                $"recorded={(screen.LookedAt == null ? "nothing" : screen.LookedAt.name)}");

            screen.Close();
            screen.Open(colony, null);
            yield return null;
            report.Check(screen.IsOpen && screen.LookedAt == null,
                "control: looking at nothing is not an error and still opens the screen",
                $"open={screen.IsOpen}");

            if (chest != null)
            {
                if (chest.TryGetComponent(out ZNetView view) && view.IsValid())
                {
                    view.ClaimOwnership();
                }

                ZNetScene.instance.Destroy(chest);
            }

            yield return null;
        }

        /// <summary>
        ///     The layout audit, and the fixture that proves it can fail.
        /// </summary>
        private static IEnumerator Layout(TestReport report, ColonyScreen screen, Colony colony)
        {
            screen.Close();
            screen.Open(colony, null);
            yield return null;

            ScreenAudit.Result home = ScreenAudit.Inspect(screen.Content);
            report.Check(home.Elements.Count > 0, "the layout audit finds something to inspect",
                home.Summary);
            report.Check(home.Clean, "the colony screen has no layout faults",
                home.Clean ? home.Summary : home.FirstFault);

            screen.Push(new GalleryScreen());
            yield return null;
            ScreenAudit.Result gallery = ScreenAudit.Inspect(screen.Content);
            report.Check(gallery.Clean, "the widget gallery has no layout faults",
                gallery.Clean ? gallery.Summary : gallery.FirstFault);

            screen.Push(new PickerScreen("Choose an item", Search, null, false, _ => { }));
            yield return null;
            ScreenAudit.Result picker = ScreenAudit.Inspect(screen.Content);
            report.Check(picker.Clean, "the picker has no layout faults",
                picker.Clean ? picker.Summary : picker.FirstFault);

            // The villager screens, which were built without an audit entry and shipped a
            // truncated label on their first run - "something they no longer have" rendered as
            // "something they no". A screen nobody audits is a screen with no clipping check.
            screen.Root(new ColonyHomeScreen());
            screen.Push(new VillagerListScreen());
            yield return null;
            ScreenAudit.Result roster = ScreenAudit.Inspect(screen.Content);
            report.Check(roster.Clean, "the villagers list has no layout faults",
                roster.Clean ? roster.Summary : roster.FirstFault);

            List<ZDOID> people = colony.State.GetMembers(ColonyMemberKind.Villager);
            if (people.Count > 0)
            {
                screen.Push(new VillagerDetailScreen(people[0]));
                yield return null;
                ScreenAudit.Result person = ScreenAudit.Inspect(screen.Content);
                report.Check(person.Clean, "a villager's screen has no layout faults",
                    person.Clean ? person.Summary : person.FirstFault);

                screen.ShowPageForTest(1);
                yield return null;
                ScreenAudit.Result paged = ScreenAudit.Inspect(screen.Content);
                report.Check(paged.Clean, "a villager's second page has no layout faults",
                    paged.Clean ? paged.Summary : paged.FirstFault);
                screen.ShowPageForTest(0);
            }
            else
            {
                report.Check(false, "control: there was a villager whose screen could be audited");
            }

            // The job screens, and with a job built to be as long as the format allows: two
            // places named, a count for the rest and a "gone" warning, on a row whose cell is
            // the narrowest on any screen here. A character budget is a proxy for pixels, and
            // this is the check that makes it answerable - the previous shape of this row was
            // measured by nobody and drew itself across the Open button.
            List<JobDefinition> before = colony.State.GetJobs();
            List<StructureRecord> places = colony.State.GetStructures();
            List<string> areas = new List<string> { string.Empty, "kukolony.audit.gone.1" };
            foreach (StructureRecord record in places)
            {
                if (areas.Count >= 4) break;
                if (!string.IsNullOrEmpty(record.PersistentId)) areas.Add(record.PersistentId);
            }

            // A second missing place, so the count behind the "+N" and the warning are both
            // exercised whatever the fixture colony happens to have registered.
            areas.Add("kukolony.audit.gone.2");

            // The fixture alone, not appended to what is there. A settlement with a page of
            // jobs already would have pushed this one to page two, where the audit never
            // sees it and the check passes without inspecting its own fixture.
            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition
                {
                    Id = "audit-job",
                    Name = "Haul everything to the shed by the docks",
                    Kind = JobKind.Haul,
                    Repeat = 4,
                    Areas = areas
                }
            });

            // Restored whatever happens in between. The jobs live in the colony's ZDO, which
            // is about to be saved for the reload phase - a fixture leaked here is a debug
            // job in the player's world, and a screen build that throws kills the coroutine
            // without running another statement. A finally may hold a yield; a catch may not.
            try
            {
                screen.Root(new ColonyHomeScreen());
                screen.Push(new JobListScreen());
                yield return null;
                ScreenAudit.Result jobs = ScreenAudit.Inspect(screen.Content);
                report.Check(jobs.Clean, "the jobs list has no layout faults, with a job named at length",
                    jobs.Clean ? jobs.Summary : jobs.FirstFault);

                screen.Push(new JobDetailScreen("audit-job"));
                yield return null;
                ScreenAudit.Result detail = ScreenAudit.Inspect(screen.Content);
                report.Check(detail.Clean, "a job's own screen has no layout faults",
                    detail.Clean ? detail.Summary : detail.FirstFault);
            }
            finally
            {
                colony.State.SetJobs(before);
            }

            screen.Root(new ColonyHomeScreen());
            yield return null;

            // The control. Without it a silently empty audit passes forever.
            screen.Push(new BrokenScreen());
            yield return null;
            ScreenAudit.Result broken = ScreenAudit.Inspect(screen.Content);
            report.Check(broken.OutOfBounds.Count > 0,
                "control: the audit catches an element outside the content column",
                broken.Summary);
            report.Check(broken.Overlaps.Count > 0,
                "control: the audit catches two elements drawn on top of each other",
                broken.Summary);
            report.Check(broken.Clipped.Count > 0,
                "control: the audit catches a string too long for its cell",
                broken.Summary);

            screen.Root(new ColonyHomeScreen());
            yield return null;
        }

        /// <summary>
        ///     A structure that cannot be found must not be reported as merely distant.
        /// </summary>
        /// <remarks>
        ///     Found by looking at a screenshot: a destroyed chest read "out of reach", which
        ///     is a sentence about a chest that still exists somewhere. The two states came
        ///     from one boolean, so every caller had to pick one of the two meanings and be
        ///     wrong half the time.
        ///
        ///     Asserted on the row the player reads rather than on the enum behind it, because
        ///     the fault was in the rendering and an enum check would have passed throughout.
        /// </remarks>
        private static IEnumerator StructureStatusReads(TestReport report, ColonyScreen screen, Colony colony)
        {
            screen.Close();
            screen.Open(colony, null);
            screen.Push(new StructureListScreen());
            yield return null;

            // Whatever is actually ready, rather than a fixture by name. Naming one made this
            // check depend on that chest surviving every other check in the run - and the
            // reaper legitimately removes a structure that something else destroyed.
            string ready = string.Empty;
            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if (record.StatusIn(colony) != StructureStatus.Ready) continue;
                ready = record.Name;
                break;
            }

            string live = ready.Length == 0 ? "<nothing ready>" : RowBeside(screen, ready);
            // The unfindable record, not a destroyed one: a destroyed structure's record is
            // reaped, so it is no longer on screen to read. What must render as "not found" is
            // the record whose object cannot be located and is not known to be dead.
            string gone = RowBeside(screen, "Unfindable storage");

            report.Check(gone.Contains("not found"), "a structure that cannot be found says so",
                $"row read '{gone}'");
            report.Check(live.Contains("ready") && !live.Contains("not found"),
                "control: a structure that is present reads differently from one that is not",
                $"row read '{live}'");

            screen.Root(new ColonyHomeScreen());
            yield return null;
        }

        /// <summary>
        ///     Everything drawn on the same row as a named structure.
        /// </summary>
        /// <remarks>
        ///     Returns the whole row rather than the first cell beside the name. A row now
        ///     carries a capability as well as a status, and "the first other text on this
        ///     row" silently became the wrong column the moment that was added - a check that
        ///     keeps passing while reading something else is worse than one that breaks.
        ///
        ///     Still matched by row position, so a word appearing anywhere else on the screen
        ///     cannot satisfy it.
        /// </remarks>
        private static string RowBeside(ColonyScreen screen, string name)
        {
            ScreenAudit.Result built = ScreenAudit.Inspect(screen.Content);
            float y = float.NaN;
            foreach (ScreenAudit.Element element in built.Elements)
            {
                if (element.Text == name)
                {
                    y = element.Rect.center.y;
                    break;
                }
            }

            if (float.IsNaN(y))
            {
                return "<no such row>";
            }

            System.Text.StringBuilder row = new System.Text.StringBuilder();
            foreach (ScreenAudit.Element element in built.Elements)
            {
                if (Mathf.Abs(element.Rect.center.y - y) < 2f && element.Text != name &&
                    !string.IsNullOrEmpty(element.Text))
                {
                    if (row.Length > 0) row.Append(" | ");
                    row.Append(element.Text);
                }
            }

            return row.Length == 0 ? "<nothing beside it>" : row.ToString();
        }

        /// <summary>
        ///     A long list pages; a short one does not. The control is what stops a pager that
        ///     always reports two pages from looking correct.
        /// </summary>
        private static IEnumerator Paging(TestReport report, ColonyScreen screen, Colony colony)
        {
            screen.Close();
            screen.Open(colony, null);
            yield return null;

            // The control uses the colony list rather than the colony screen: the latter grows
            // a row per registered structure, so whether it fits on one page depends on what
            // the rest of the run happened to register. A control that can be broken by an
            // unrelated check is not a control.
            screen.Root(new ColonyListScreen());
            yield return null;
            int shortPages = screen.LastPages;
            report.Check(shortPages == 1 && screen.LastRows <= Gui.Panel.RowsPerPage,
                "control: a screen that fits reports a single page and shows no pager",
                $"rows={screen.LastRows} perPage={Gui.Panel.RowsPerPage} pages={shortPages}");

            screen.Root(new ColonyHomeScreen());
            yield return null;

            screen.Push(new GalleryScreen());
            yield return null;
            report.Check(screen.LastPages > 1, "a list longer than a page reports more than one",
                $"rows={screen.LastRows} pages={screen.LastPages}");

            string first = Fingerprint(screen);
            screen.ShowPageForTest(1);
            yield return null;
            string second = Fingerprint(screen);
            report.Check(first != second && !string.IsNullOrEmpty(second),
                "turning the page shows different rows",
                $"page1 {Differing(first, second)} | page2 {Differing(second, first)}");

            ScreenAudit.Result paged = ScreenAudit.Inspect(screen.Content);
            report.Check(paged.Clean, "a turned page has no layout faults",
                paged.Clean ? paged.Summary : paged.FirstFault);

            screen.ShowPageForTest(0);
            yield return null;
        }

        /// <summary>
        ///     Every sub-screen returns where it came from, and switching top-level screens
        ///     clears sub-screens rather than stranding the player inside one.
        /// </summary>
        private static IEnumerator BackStack(TestReport report, ColonyScreen screen, Colony colony)
        {
            screen.Close();
            screen.Open(colony, null);
            yield return null;
            int atRoot = screen.Depth;

            screen.Push(new GalleryScreen());
            yield return null;
            bool descended = screen.Depth == atRoot + 1 && screen.Current is GalleryScreen;

            screen.Pop();
            yield return null;
            report.Check(descended && screen.Depth == atRoot && screen.Current is ColonyHomeScreen,
                "a sub-screen returns to the screen it came from",
                $"depth={screen.Depth} current={Name(screen.Current)}");

            screen.Push(new GalleryScreen());
            screen.Push(new PickerScreen("Choose", Search, null, false, _ => { }));
            yield return null;
            int nested = screen.Depth;

            screen.Root(new ColonyHomeScreen());
            yield return null;
            report.Check(nested > atRoot && screen.Depth == 1,
                "control: switching top-level screens clears the sub-screens below",
                $"nested={nested} afterSwitch={screen.Depth}");
        }

        /// <summary>
        ///     The screen must survive its subject vanishing. Uses a throwaway hearth, because
        ///     the claim can only be tested by destroying one and every later check needs the
        ///     benchmark's own colony alive.
        /// </summary>
        private static IEnumerator SubjectVanishes(TestReport report, ColonyScreen screen, Vector3 origin)
        {
            Vector3 where = origin + new Vector3(0f, 0f, 26f);
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetSolidHeight(where, out float _))
            {
                report.Check(false, "vanishing-colony check found ground for its own hearth");
                yield break;
            }

            GameObject spawned = Spawn(ColonyPrefab.PrefabName, where);
            yield return new WaitForSecondsRealtime(.3f);
            Colony temporary = spawned != null ? spawned.GetComponent<Colony>() : null;
            if (temporary == null)
            {
                report.Check(false, "vanishing-colony check kept its own hearth alive");
                yield break;
            }

            temporary.EnsureNamed();
            screen.Close();
            screen.Open(temporary, null);
            yield return null;
            report.Check(screen.IsOpen, "control: the screen stays open while its colony is alive");

            if (temporary.TryGetComponent(out ZNetView view) && view.IsValid())
            {
                view.ClaimOwnership();
                ZNetScene.instance.Destroy(temporary.gameObject);
            }

            // Two frames: one for the destroy to take effect, one for the screen's own Update
            // to notice. Checking in the same frame would test the destroy, not the screen.
            yield return null;
            yield return null;
            report.Check(!screen.IsOpen, "the screen closes when its colony is destroyed");
            report.Check(!UnityEngine.Cursor.visible,
                "closing on a destroyed colony still gives the cursor back",
                $"visible={UnityEngine.Cursor.visible}");
        }

        private static System.Collections.Generic.List<PickerScreen.Option> Search(string filter)
        {
            System.Collections.Generic.List<PickerScreen.Option> options =
                new System.Collections.Generic.List<PickerScreen.Option>();
            foreach (ItemCatalogue.Entry entry in ItemCatalogue.Search(filter, 40))
            {
                options.Add(new PickerScreen.Option(entry.PrefabName, entry.DisplayName));
            }

            return options;
        }

        /// <summary>Every string the screen is currently drawing, in order.</summary>
        private static string Fingerprint(ColonyScreen screen)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            foreach (ScreenAudit.Element element in ScreenAudit.Inspect(screen.Content).Elements)
            {
                if (!string.IsNullOrEmpty(element.Text))
                {
                    text.Append(element.Text).Append('|');
                }
            }

            return text.ToString();
        }

        private static string Name(ScreenView screen) => screen == null ? "none" : screen.GetType().Name;

        /// <summary>
        ///     The part of one page that the other does not share.
        /// </summary>
        /// <remarks>
        ///     Both pages carry the same title and subtitle, so printing each from the start
        ///     showed two identical-looking strings whether the check passed or failed - and a
        ///     failure that prints the same thing as a pass explains nothing. Report the part
        ///     the assertion actually turned on.
        /// </remarks>
        private static string Differing(string text, string other)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "empty";
            }

            int shared = 0;
            while (shared < text.Length && shared < other.Length && text[shared] == other[shared])
            {
                shared++;
            }

            string tail = text.Substring(shared);
            return tail.Length <= 40 ? tail : tail.Substring(0, 40) + "...";
        }

        private static GameObject Fixture(Vector3 origin) => Spawn("piece_chest_wood", origin + new Vector3(0f, 0f, 6f));

        private static GameObject Spawn(string prefabName, Vector3 position)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
            if (prefab == null)
            {
                return null;
            }

            position.y = ZoneSystem.instance.GetSolidHeight(position) + .2f;
            return Object.Instantiate(prefab, position, Quaternion.identity);
        }
    }
}
