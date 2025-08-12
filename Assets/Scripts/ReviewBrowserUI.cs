// ReviewBrowserUI.cs
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class ReviewBrowserUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Transform contentParent;      // ScrollView/Viewport/Content
    [SerializeField] private Button buttonPrefab;          // Simple Button with a Text child
    [SerializeField] private string filePrefix = "Green";  // Must match exporter

    [Header("Refs")]
    [SerializeField] private ARMeshCaptureExporter exporter;
    [SerializeField] private AppModeManager appMode;

    void OnEnable() => Refresh();

    public void Refresh()
    {
        // Clear existing
        for (int i = contentParent.childCount - 1; i >= 0; i--)
            Destroy(contentParent.GetChild(i).gameObject);

        var dir = Application.persistentDataPath;
        if (!Directory.Exists(dir)) return;

        var files = Directory.GetFiles(dir, $"{filePrefix}_*.obj")
                             .OrderByDescending(f => File.GetLastWriteTime(f))
                             .ToArray();

        foreach (var path in files)
        {
            var btn = Instantiate(buttonPrefab, contentParent);
            var label = btn.GetComponentInChildren<Text>();
            label.text = Path.GetFileName(path);
            btn.onClick.AddListener(() =>
            {
                // Enter Review with the chosen file
                if (appMode != null) appMode.EnterReviewWithPath(path);
                else exporter.EnterReviewModeWithPath(path);
            });
        }
    }
}
