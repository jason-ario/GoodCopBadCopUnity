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
    /// Regression guard for <see cref="ExamNotebook"/>'s sequential-capture fix: firing every
    /// page's RenderTexture snapshot in the same frame let overlapping capture windows bleed one
    /// page's checkmarks into another's, or leave a page's camera blank because its capture never
    /// got a clean, unobstructed window. SnapshotAllPages must always capture pages one at a time.
    /// </summary>
    public sealed class ExamNotebookSnapshotSequencingTests
    {
        private const string DocumentationNotebookPrefabPath =
            "Assets/_GoodCopBadCop/_Prefabs/Interactables/Documents/Exam Notebooks/Exam Notebooks/Documentation Exam Notebook.prefab";

        private const string DocumentationPagePrefabPath =
            "Assets/_GoodCopBadCop/_Prefabs/Interactables/Documents/Exam Pages/Documentation Exam page.prefab";

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

        private GameObject Instantiate(string assetPath, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            Assert.IsNotNull(prefab, $"Could not load prefab at {assetPath}");

            GameObject instance = Object.Instantiate(prefab);
            instance.name = name;
            _spawned.Add(instance);
            return instance;
        }

        private static void SetPrivateField(object obj, string fieldName, object value)
        {
            FieldInfo field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected private field '{fieldName}' on {obj.GetType()}");
            field.SetValue(obj, value);
        }

        private static T GetPrivateField<T>(object obj, string fieldName)
        {
            FieldInfo field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected private field '{fieldName}' on {obj.GetType()}");
            return (T)field.GetValue(obj);
        }

        private static void InvokePrivateMethod(object obj, string methodName)
        {
            MethodInfo method = obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"Expected private method '{methodName}' on {obj.GetType()}");
            method.Invoke(obj, null);
        }

        [UnityTest]
        public IEnumerator SnapshotAllPages_CapturesPagesOneAtATime_NeverOverlapping()
        {
            GameObject notebookGO = Instantiate(DocumentationNotebookPrefabPath, "Notebook_Sequencing");
            ExamNotebook notebook = notebookGO.GetComponent<ExamNotebook>();
            Assert.IsNotNull(notebook, "Notebook prefab has no ExamNotebook component");

            var pageA = Instantiate(DocumentationPagePrefabPath, "Page_0").GetComponent<ExamPage>();
            var pageB = Instantiate(DocumentationPagePrefabPath, "Page_1").GetComponent<ExamPage>();
            var pageC = Instantiate(DocumentationPagePrefabPath, "Page_2").GetComponent<ExamPage>();
            pageA.gameObject.SetActive(true);
            pageB.gameObject.SetActive(true);
            pageC.gameObject.SetActive(true);
            yield return null;

            // Wire the notebook's private pages[] directly rather than going through the
            // networked spawn/RPC flow, which requires a running NetworkManager.
            SetPrivateField(notebook, "pages", new[] { pageA, pageB, pageC });

            InvokePrivateMethod(notebook, "SnapshotAllPages");

            bool sawOverlap = false;
            int previouslyCapturingCount = 0;
            int framesObserved = 0;

            // Poll every frame while the sequencing coroutine runs (bounded so a stuck sequence
            // fails the test instead of hanging it forever).
            for (int i = 0; i < 600; i++)
            {
                int capturingCount = 0;
                if (pageA.IsCapturing) capturingCount++;
                if (pageB.IsCapturing) capturingCount++;
                if (pageC.IsCapturing) capturingCount++;

                if (capturingCount > 1)
                    sawOverlap = true;

                previouslyCapturingCount = capturingCount;
                framesObserved++;

                Coroutine handle = GetPrivateField<Coroutine>(notebook, "_snapshotAllPagesCoroutine");
                if (handle == null && !pageA.IsCapturing && !pageB.IsCapturing && !pageC.IsCapturing)
                    break;

                yield return null;
            }

            Assert.IsFalse(sawOverlap,
                "SnapshotAllPages must never have more than one page capturing at the same time — " +
                "overlapping captures are what caused the checklist camera to bake a sibling page's " +
                "content into the wrong RenderTexture.");
            Assert.Greater(framesObserved, 0, "Test loop should have observed at least one frame.");
            Assert.AreEqual(0, previouslyCapturingCount, "All pages should have finished capturing by the end of the sequence.");
        }

        [UnityTest]
        public IEnumerator SnapshotAllPages_SkipsRippedOutAndInactivePages()
        {
            GameObject notebookGO = Instantiate(DocumentationNotebookPrefabPath, "Notebook_SkipInactive");
            ExamNotebook notebook = notebookGO.GetComponent<ExamNotebook>();

            var activePage = Instantiate(DocumentationPagePrefabPath, "Page_Active").GetComponent<ExamPage>();
            var inactivePage = Instantiate(DocumentationPagePrefabPath, "Page_Inactive").GetComponent<ExamPage>();
            activePage.gameObject.SetActive(true);
            inactivePage.gameObject.SetActive(false);
            yield return null;

            SetPrivateField(notebook, "pages", new[] { activePage, inactivePage });

            InvokePrivateMethod(notebook, "SnapshotAllPages");
            yield return null;

            Assert.IsTrue(activePage.IsCapturing, "Active page should have started capturing.");
            Assert.IsFalse(inactivePage.IsCapturing, "Inactive page must be skipped, never activated by the sequence.");

            float duration = GetPrivateField<float>(activePage, "_drawAnimationDuration");
            yield return new WaitForSeconds(duration + 0.2f);

            Assert.IsFalse(activePage.IsCapturing);
            Assert.IsFalse(inactivePage.IsCapturing);
        }
    }
}
