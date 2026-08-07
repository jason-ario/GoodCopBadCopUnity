using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoodCopBadCop.Editor.Tests
{
    /// <summary>
    /// Regression guard for the checklist RenderTexture snapshot system on <see cref="ExamPage"/>
    /// (cross-page bleed, cut-off checkmark draw animation, and blank-page bugs fixed together —
    /// see ExamPage.SnapshotChecklist / BeginSnapshot / EndSnapshot / IsCapturing).
    /// </summary>
    public sealed class ExamPageSnapshotTests
    {
        private const string DocumentationPagePrefabPath =
            "Assets/_GoodCopBadCop/_Prefabs/Interactables/Documents/Exam Pages/Documentation Exam page.prefab";

        // End-to-end time from a checkbox click to the "Cross Drawn In" clip finishing:
        // Checkbox.WaitAndShowCheckmark's 0.15s delay + the ~0.5167s Animator clip.
        private const float MinimumAnimationCoverageSeconds = 0.667f;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }
            _spawned.Clear();
        }

        private ExamPage InstantiatePage(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DocumentationPagePrefabPath);
            Assert.IsNotNull(prefab, $"Could not load exam page prefab at {DocumentationPagePrefabPath}");

            GameObject instance = Object.Instantiate(prefab);
            instance.name = name;
            _spawned.Add(instance);

            ExamPage page = instance.GetComponent<ExamPage>();
            Assert.IsNotNull(page, "Instantiated prefab has no ExamPage component");
            return page;
        }

        private static T GetPrivateField<T>(object obj, string fieldName)
        {
            FieldInfo field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected private field '{fieldName}' on {obj.GetType()}");
            return (T)field.GetValue(obj);
        }

        [Test]
        public void DrawAnimationDuration_CoversFullCheckmarkDrawAnimation()
        {
            ExamPage page = InstantiatePage("Page_DurationCheck");

            float duration = GetPrivateField<float>(page, "_drawAnimationDuration");

            Assert.GreaterOrEqual(duration, MinimumAnimationCoverageSeconds,
                "_drawAnimationDuration must be long enough to cover Checkbox's 0.15s delay plus " +
                "the full 'Cross Drawn In' Animator clip (~0.5167s), or the checkmark draw " +
                "animation gets frozen/cut off mid-stroke on the baked RenderTexture.");
        }

        [UnityTest]
        public IEnumerator SnapshotChecklist_ActivatesCameraImmediately_AndDeactivatesAfterDuration()
        {
            ExamPage page = InstantiatePage("Page_SingleCapture");
            Camera checklistCamera = GetPrivateField<Camera>(page, "_checklistCamera");
            Assert.IsNotNull(checklistCamera, "Prefab is missing its checklist camera reference");

            float duration = GetPrivateField<float>(page, "_drawAnimationDuration");

            page.SnapshotChecklist();

            Assert.IsTrue(page.IsCapturing, "Page should be capturing immediately after SnapshotChecklist()");
            Assert.IsTrue(checklistCamera.gameObject.activeSelf, "Checklist camera should be active during capture");

            yield return new WaitForSeconds(duration + 0.1f);

            Assert.IsFalse(page.IsCapturing, "Page should stop capturing once the draw-animation window elapses");
            Assert.IsFalse(checklistCamera.gameObject.activeSelf, "Checklist camera should be deactivated once capture ends");
        }

        [UnityTest]
        public IEnumerator SnapshotChecklist_CalledAgainMidCapture_RestartsCleanly()
        {
            ExamPage page = InstantiatePage("Page_Restart");
            float duration = GetPrivateField<float>(page, "_drawAnimationDuration");

            page.SnapshotChecklist();
            yield return new WaitForSeconds(duration * 0.5f);
            Assert.IsTrue(page.IsCapturing, "Page should still be mid-capture before restarting");

            // Simulates a second checkbox click landing while the first capture is still open.
            page.SnapshotChecklist();
            Assert.IsTrue(page.IsCapturing, "Restarted capture should immediately be capturing again");

            // Wait out the full window again from this restart point; it must still cleanly finish.
            yield return new WaitForSeconds(duration + 0.1f);
            Assert.IsFalse(page.IsCapturing, "Restarted capture should still terminate on its own after its full duration");
        }

        [UnityTest]
        public IEnumerator SnapshotChecklist_HidesSiblingPageRenderers_AndRestoresThemAfterCapture()
        {
            ExamPage capturingPage = InstantiatePage("Page_Capturing");
            ExamPage siblingPage = InstantiatePage("Page_Sibling");
            float duration = GetPrivateField<float>(capturingPage, "_drawAnimationDuration");

            // Both pages must be enabled (OnEnable registers them in the shared _activePages list
            // that BeginSnapshot uses to find peers to hide).
            capturingPage.gameObject.SetActive(true);
            siblingPage.gameObject.SetActive(true);
            yield return null;

            capturingPage.SnapshotChecklist();
            yield return null; // let BeginSnapshot's peer hide-request run inside the coroutine.

            int siblingHideCount = GetPrivateField<int>(siblingPage, "_hideRequestCount");
            Assert.AreEqual(1, siblingHideCount,
                "Sibling page should have exactly one active hide request while the capturing page's " +
                "checklist camera is open, so its checklist artwork cannot bleed into the capture.");

            yield return new WaitForSeconds(duration + 0.1f);

            siblingHideCount = GetPrivateField<int>(siblingPage, "_hideRequestCount");
            Assert.AreEqual(0, siblingHideCount,
                "Sibling page's hide request must be released once the capturing page's snapshot ends.");
        }

        [UnityTest]
        public IEnumerator SnapshotChecklist_TwoPagesCapturingSimultaneously_NeitherHidesTheOther()
        {
            ExamPage pageA = InstantiatePage("Page_A_Simultaneous");
            ExamPage pageB = InstantiatePage("Page_B_Simultaneous");
            float duration = GetPrivateField<float>(pageA, "_drawAnimationDuration");

            pageA.gameObject.SetActive(true);
            pageB.gameObject.SetActive(true);
            yield return null;

            // Both pages start capturing in the same frame — BeginSnapshot must skip peers that
            // are themselves mid-snapshot, otherwise they would blind each other's captures.
            pageA.SnapshotChecklist();
            pageB.SnapshotChecklist();
            yield return null;

            Assert.AreEqual(0, GetPrivateField<int>(pageA, "_hideRequestCount"),
                "A page that is itself capturing must never be hidden by another page's capture request.");
            Assert.AreEqual(0, GetPrivateField<int>(pageB, "_hideRequestCount"),
                "A page that is itself capturing must never be hidden by another page's capture request.");

            yield return new WaitForSeconds(duration + 0.1f);

            Assert.IsFalse(pageA.IsCapturing);
            Assert.IsFalse(pageB.IsCapturing);
        }

        [UnityTest]
        public IEnumerator DisablingPageMidCapture_ReleasesAnyPeersItWasHiding()
        {
            ExamPage capturingPage = InstantiatePage("Page_DisabledMidCapture");
            ExamPage siblingPage = InstantiatePage("Page_SiblingOfDisabled");

            capturingPage.gameObject.SetActive(true);
            siblingPage.gameObject.SetActive(true);
            yield return null;

            capturingPage.SnapshotChecklist();
            yield return null;

            Assert.AreEqual(1, GetPrivateField<int>(siblingPage, "_hideRequestCount"),
                "Sanity check: sibling should be hidden while capture is in flight.");

            // Simulates the page being ripped out / deactivated while its camera is still open.
            capturingPage.gameObject.SetActive(false);
            yield return null;

            Assert.AreEqual(0, GetPrivateField<int>(siblingPage, "_hideRequestCount"),
                "OnDisable must release any peer hide requests an interrupted capture made, or the " +
                "sibling page's checklist renderers get permanently stranded hidden.");
        }
    }
}
